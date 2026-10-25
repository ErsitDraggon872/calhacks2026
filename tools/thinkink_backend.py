#!/usr/bin/env python3
"""ThinkInk demo backend: POST /interpret, stdlib only, secrets stay server-side.

REAL MODE: BITDEER_API_KEY=... python3 tools/thinkink_backend.py
SMOKE TEST: THINKINK_FAKE=1 python3 tools/thinkink_backend.py
"""
from __future__ import annotations
import base64
import io
import copy
import hashlib
from collections import OrderedDict
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import math
import os
from pathlib import Path
import re
import time
import subprocess
import tempfile

PROMPT = Path(__file__).with_name('thinkink_system_prompt.txt').read_text()
# Demo mode trims answers to support/bounce/thrust/lift at tested strengths; THINKINK_DEMO=0 keeps every ability.
DEMO_MODE = os.getenv('THINKINK_DEMO', '1') == '1'
MODEL = os.getenv('BITDEER_MODEL', 'Qwen/Qwen3.8-27B')
ENDPOINT = os.getenv('BITDEER_ENDPOINT', 'https://api-inference.bitdeer.ai/v1/chat/completions')
FAKE = os.getenv('THINKINK_FAKE', '0') == '1'
USE_SCHEMA = os.getenv('BITDEER_USE_SCHEMA', '0') == '1'
# JSON object mode is lighter than the long strict attribute schema; backend still validates.
JSON_MODE = os.getenv('BITDEER_JSON_MODE', '1') == '1'
# Vendor-specific option: if Bitdeer rejects it, retry without it automatically.
DISABLE_THINKING = os.getenv('BITDEER_DISABLE_THINKING', '1') == '1'
MAX_BODY = 10 * 1024 * 1024
# Vision inference can take longer than a text-only call. The Unity client
# timeout must exceed this backend limit (configured to 40 seconds).
MAX_LATENCY_S = max(5.0, min(90.0, float(os.getenv('BITDEER_TIMEOUT_S', '30'))))
PNG = b'\x89PNG\r\n\x1a\n'
_CACHE = OrderedDict()
_CACHE_LOCK = threading.Lock()
_CACHE_CAPACITY = 48

FIELDS = ['type','activation','anchor_x','anchor_y','launch_speed','angle_deg','acceleration','duration_s']
# No tether: the grappling rope is cut, and a drawn rope is just a solid shape.
ABILITIES = ('bounce', 'thrust', 'lift', 'glide', 'roll', 'attach', 'hinge', 'push_pull')
# Identity (docs/interpret-contract.md): what the drawing IS. Puzzles check these traits, never the name,
# so a gatekeeper takes an apple and a banana alike because both are edible. Unity adds material-implied traits.
MATERIALS = ('wood','stone','metal','rubber','cloth','paper','glass','ice','jelly','plant','flesh','cloud')
TRAITS = ('glows','hot','cold','wet','flammable','fireproof','waterproof',
          'floats','lighter_than_air','bouncy','slippery','sticky','soft','fragile','sharp','conductive','magnetic',
          'alive','edible','hostile','friendly','noisy','smelly',
          'climbable','rideable','holdable','container')
MAX_TRAITS = 8
# width_m is the sixteenth universal attribute and remains at top level for compatibility.
ATTR_BOUNDS = {
    'mass_kg': (0.5,20,3), 'load_capacity_kg': (0,20,8),
    'friction': (0,1,0.6), 'elasticity': (0,1,0.1), 'drag': (0,1,0.2),
    'stability': (0,1,0.7), 'durability': (0,1,0.7),
    'force': (0,1,0.6), 'speed': (0,1,0.5),
    'direction_deg': (-180,180,0), 'duration_s': (0,10,3),
    'energy_capacity': (0,1,0.7), 'carry_capacity_kg': (0,20,3),
    'range_m': (0.2,8,3), 'cooldown_s': (0,5,0.5),
}

def attributes(raw, width):
    if not isinstance(raw, dict): raw = {}
    result = {k:clamp(raw.get(k),*bounds) for k,bounds in ATTR_BOUNDS.items()}
    result['direction_deg'] = round(result['direction_deg'])
    # When the old prompt supplied no carrying estimate, physical scale is a
    # conservative fallback; small animals should not carry an adult explorer.
    if 'carry_capacity_kg' not in raw:
        result['carry_capacity_kg'] = 1 if width < 1.5 else (8 if width > 3.5 else 3)
    return result

PART_SCHEMA = {'type':'object','properties':{
    'type':{'type':'string','enum':['support',*ABILITIES]},
    'activation':{'type':'string','enum':['always','contact','button']},
    'anchor_x':{'type':'number'}, 'anchor_y':{'type':'number'},
    'launch_speed':{'type':'number'}, 'angle_deg':{'type':'integer'},
    'acceleration':{'type':'number'}, 'duration_s':{'type':'number'}
},'required':FIELDS,'additionalProperties':False}
SCHEMA = {'type':'object','properties':{
    'status':{'type':'string','enum':['ok','rejected']},
    'name':{'type':'string'}, 'message':{'type':'string'},
    'adjectives':{'type':'array','items':{'type':'string'}},
    'material':{'type':'string','enum':list(MATERIALS)},
    'traits':{'type':'array','items':{'type':'string','enum':list(TRAITS)}},
    'width_m':{'type':'number'}, 'components':{'type':'array','items':PART_SCHEMA},
    'attributes':{'type':'object','properties':{k:{'type':'integer' if k == 'direction_deg' else 'number'} for k in ATTR_BOUNDS},
                 'required':list(ATTR_BOUNDS),'additionalProperties':False}
},'required':['status','name','message','adjectives','material','traits','width_m','components','attributes'],
   'additionalProperties':False}


def clamp(value, lo, hi, default):
    try:
        num = float(value)
        if not math.isfinite(num): return default
        return max(lo, min(hi, num))
    except (ValueError, TypeError, OverflowError): return default


def support():
    return {'type':'support','activation':'always','anchor_x':0.5,'anchor_y':0.5,
            'launch_speed':0,'angle_deg':0,'acceleration':0,'duration_s':0}


def extract_json(text):
    if not isinstance(text, str): raise ValueError('model content was not text')
    text = re.sub(r'<think\b[^>]*>.*?</think>', '', text, flags=re.I|re.S)
    text = re.sub(r'```(?:json)?', '', text, flags=re.I)
    decoder = json.JSONDecoder()
    for i, ch in enumerate(text):
        if ch == '{':
            try:
                obj, _ = decoder.raw_decode(text[i:])
                if isinstance(obj, dict): return obj
            except json.JSONDecodeError: pass
    raise ValueError('no JSON object in AI reply')


def validate(raw):
    if not isinstance(raw, dict): raise ValueError('not an object')
    status = str(raw.get('status','ok')).lower()
    if status not in ('ok','rejected'): status = 'rejected'
    name = str(raw.get('name') or 'mystery shape').lower().strip()[:40]
    message = str(raw.get('message') or 'Use the invention to solve the puzzle.').strip()[:120]
    width = clamp(raw.get('width_m'), 0.5, 6, 2.5)
    parts = raw.get('components')
    if not isinstance(parts, list) or not parts: raise ValueError('no components')
    result = [support()]
    seen = set()
    for part in parts:
        if not isinstance(part,dict): continue
        kind = str(part.get('type','')).strip().lower()
        if kind not in ABILITIES or kind in seen or len(result) >= 4: continue
        seen.add(kind)
        mode = str(part.get('activation','')).strip().lower()
        allowed = ('always','contact','button','hold','toggle')
        if mode not in allowed: mode = 'contact' if kind == 'bounce' else 'button'
        a = {'type':kind,'activation':mode,
             'anchor_x':clamp(part.get('anchor_x'),0,1,.5),
             'anchor_y':clamp(part.get('anchor_y'),0,1,.5),
             'launch_speed':0, 'angle_deg':0, 'acceleration':0, 'duration_s':0}
        if kind == 'bounce':
            a.update(launch_speed=clamp(part.get('launch_speed'),3,14,10),
                     angle_deg=round(clamp(part.get('angle_deg'),-60,60,0)))
        elif kind in ('thrust','lift','roll','push_pull'):
            # No angle given: lift and a rocket go straight up; wheels and pistons push right.
            a.update(angle_deg=round(clamp(part.get('angle_deg'),-180,180,90 if kind in ('lift','thrust') else 0)),
                     acceleration=clamp(part.get('acceleration'),2,14 if kind=='roll' else 25,12),
                     duration_s=clamp(part.get('duration_s'),0,10,3))
            if kind=='thrust': a['duration_s']=clamp(part.get('duration_s'),.5,3,1.5)
        elif kind=='glide':
            # launch_speed is a terminal descent speed for GLIDE (m/s).
            a.update(launch_speed=clamp(part.get('launch_speed'),1,6,2.5))
        elif kind in ('attach','hinge'):
            a['duration_s']=0
        result.append(a)
    if status == 'rejected': result = [support()]
    return {'status':status,'name':name,'message':message,**identity(raw),'width_m':width,
            'components':result,'attributes':attributes(raw.get('attributes'),width)}


def words(value):
    """Lower-case strings from a list, with spaces or hyphens folded to underscores."""
    if isinstance(value, str): value = [value]
    if not isinstance(value, list): return []
    return [re.sub(r'[\s-]+','_',str(v).strip().lower()) for v in value if isinstance(v,(str,int,float))]


def identity(raw):
    # Unknown words are dropped, not errors; Unity checks the same vocabulary again.
    material = (words(raw.get('material')) or ['wood'])[0]
    traits = []
    for trait in words(raw.get('traits')):
        if trait in TRAITS and trait not in traits: traits.append(trait)
    if 'hot' in traits and 'cold' in traits: traits.remove('cold')
    adjectives = [a.replace('_',' ')[:24] for a in words(raw.get('adjectives'))][:4]
    return {'adjectives':adjectives,'material':material if material in MATERIALS else 'wood',
            'traits':traits[:MAX_TRAITS]}


def demo_normalize(spec):
    """Andy's demo scope: only SUPPORT / BOUNCE / THRUST / LIFT, power snapped to tested tiers.

    Identity (name, material, traits) passes through untouched so food still feeds the gatekeeper.
    Never adds an ability just to make the puzzle solvable. Riding needs no ATTACH: any moving body carries the player.
    """
    if spec['status'] != 'ok':
        return spec
    result = [support()]
    # Two optional mechanics are enough for bird+rocket hybrids.
    for original in [c for c in spec['components'] if c['type'] in ('bounce','thrust','lift')][:2]:
        c = copy.deepcopy(original)
        if c['type'] == 'bounce':
            c['launch_speed'] = min((6, 10, 13), key=lambda v: abs(v-c['launch_speed']))
            c['activation'] = 'contact' if c['activation'] == 'always' else c['activation']
        elif c['type'] == 'thrust':
            c['acceleration'] = min((8, 14, 20), key=lambda v: abs(v-c['acceleration']))
            c['duration_s'] = min((0.9, 1.6, 2.3), key=lambda v: abs(v-c['duration_s']))
        elif c['type'] == 'lift':
            c.update(activation='button', angle_deg=90, duration_s=6.0, acceleration=12.0)
        result.append(c)
    if any(c['type'] == 'lift' for c in result):
        # Gameplay carrying tiers from drawn width, not real aerodynamics: a small flyer can't carry the 6 kg player.
        w = spec['width_m']
        spec['attributes'].update(carry_capacity_kg=1.0 if w < 1.8 else (3.5 if w < 3.5 else 9.0),
                                  force=0.8, speed=0.7, energy_capacity=1.0, mass_kg=max(2.0, min(5.0, w)))
    if len(result) == 1 and len(spec['components']) > 1:
        # The AI's own message may promise the abilities just stripped, so just name it with its first adjective.
        phrase = ' '.join(spec['adjectives'][:1] + [spec['name'] or 'drawing'])
        spec['message'] = ('An ' if phrase[0].lower() in 'aeiou' else 'A ') + phrase + '!'
    spec['components'] = result
    return spec


def ink_cost(output):
    # PROVISIONAL MVP formula. The design contract specifies 5..100 but no precise formula.
    # Replace this with the team's agreed Ink pricing before scoring or competitive gameplay.
    if output['status'] != 'ok': return 0
    cost = 5 + 4*output['width_m']
    for c in output['components']:
        if c['type'] == 'bounce': cost += c['launch_speed']*1.2
        if c['type'] == 'thrust': cost += c['acceleration']*c['duration_s']/3
        if c['type'] in ('lift','roll','push_pull'): cost += c['acceleration'] * .7
        if c['type'] in ('glide','attach','hinge'): cost += 8
    traits = output.get('attributes') or {}
    cost += traits.get('force',.5)*5 + traits.get('carry_capacity_kg',0)*.3
    return int(clamp(round(cost),5,100,5))


FAKE_FOODS = ('apple','banana','sandwich','pizza','carrot')
# THINKINK_FAKE_SEQUENCE=bridge,bridge,apple makes fake mode answer each BUILD in turn (then repeats), so a
# whole route can be played in Unity without a key. Unset: fixtures are chosen from the goal and intent text.
_FAKE_SEQUENCE = [s.strip() for s in os.getenv('THINKINK_FAKE_SEQUENCE','').split(',') if s.strip()]
_FAKE_STEP = [0]


def next_fake_text(text):
    if not _FAKE_SEQUENCE: return text
    with _CACHE_LOCK:
        step = _FAKE_SEQUENCE[_FAKE_STEP[0] % len(_FAKE_SEQUENCE)]
        _FAKE_STEP[0] += 1
    return step


def fake_demo(goal):
    """Deterministic integration fixtures for testing Unity, NOT image recognition."""
    goal = goal.lower()
    food = next((f for f in FAKE_FOODS if f in goal), None)
    if food:
        spec = {'status':'ok','name':food,'message':f'A {food}! Someone hungry will love this.',
                'material':'plant','traits':['edible'],'width_m':0.8,'components':[support()]}
    elif 'bridge' in goal or 'plank' in goal:
        spec = {'status':'ok','name':'rope bridge','message':'A rope bridge. Walk across!',
                'material':'wood','traits':['climbable'],'width_m':5,'components':[support()]}
    elif 'blank' in goal:
        spec = {'status':'rejected','name':'','message':'The board looks blank. Draw something!',
                'components':[support()]}
    elif 'bird' in goal or 'eagle' in goal:
        large = 'large' in goal or 'giant' in goal or 'eagle' in goal
        width = 4.2 if large else 1.1
        spec = {'status':'ok','name':'giant bird' if large else 'tiny bird',
                'message':'Press E to ride the bird. Large wings lift more weight.',
                'width_m':width,
                'components':[support(),
                    {'type':'lift','activation':'button','angle_deg':90,'acceleration':12,'duration_s':6},
                    {'type':'attach','activation':'button'},
                    {'type':'glide','activation':'always','launch_speed':2}],
                'attributes':{'mass_kg':4 if large else 1,
                              'carry_capacity_kg':10 if large else 1,
                              'speed':.65,'energy_capacity':.8}}
    elif 'umbrella' in goal or 'parachute' in goal:
        spec={'status':'ok','name':'umbrella','message':'Press E to hold the umbrella and glide.',
              'width_m':2.5,'components':[support(),
                {'type':'glide','activation':'always','launch_speed':2},
                {'type':'attach','activation':'button'}]}
    elif 'trampoline' in goal or 'spring' in goal:
        spec={'status':'ok','name':'spring pad','message':'Land on the spring to bounce.',
              'width_m':1.5,'components':[support(),
                {'type':'bounce','activation':'contact','launch_speed':11,'angle_deg':25}]}
    elif 'skateboard' in goal or 'car' in goal:
        spec={'status':'ok','name':'roller cart','message':'Press E to ride the wheels.',
              'width_m':2.5,'components':[support(),
                {'type':'roll','activation':'button','acceleration':10,'angle_deg':0,'duration_s':4},
                {'type':'attach','activation':'button'}]}
    else:
        spec={'status':'ok','name':'mock rocket','message':'Mock mode: press E to fire.',
              'width_m':2.5,'components':[support(),
                {'type':'thrust','activation':'button','angle_deg':90,
                 'acceleration':15,'duration_s':1.5,'anchor_x':.3,'anchor_y':.35}]}
    return validate(spec)


def prepare_vision_image(raw_png):
    """Crop empty whiteboard, pad the full drawing, and resize to a compact PNG.

    PIL is optional: without it, requests still work using the original image.
    The Unity collider continues to use the original full-resolution cutout.
    """
    try:
        from PIL import Image, ImageOps
    except ImportError:
        return base64.b64encode(raw_png).decode(), 'image/png', 'Pillow not installed'
    try:
        with Image.open(io.BytesIO(raw_png)) as source:
            source.load()
            if source.width * source.height > 16_000_000:
                raise ValueError('Image exceeds safe preprocessing size')
            if source.mode == 'RGBA':
                rgb = Image.new('RGB', source.size, 'white')
                rgb.paste(source, mask=source.getchannel('A'))
            else:
                rgb = source.convert('RGB')
        gray = rgb.convert('L')
        # Only use visibly dark ink to locate the drawing; retains separate arrows and labels.
        mark = gray.point(lambda value: 255 if value < 205 else 0)
        box = mark.getbbox()
        if box:
            left, top, right, bottom = box
            pad = max(18, int(max(right - left, bottom - top) * 0.13))
            rgb = rgb.crop((max(0,left-pad), max(0,top-pad),
                            min(rgb.width,right+pad), min(rgb.height,bottom+pad)))
        max_dim = max(320, min(1280, int(os.getenv('BITDEER_IMAGE_MAX_PX', '768'))))
        if max(rgb.size) > max_dim:
            scale = max_dim / max(rgb.size)
            rgb = rgb.resize((max(1,round(rgb.width*scale)),
                              max(1,round(rgb.height*scale))),Image.Resampling.LANCZOS)
        # A clean black-and-white sketch compresses particularly well as 8-bit grayscale PNG.
        gray = rgb.convert('L')
        out = io.BytesIO()
        gray.save(out, format='PNG', optimize=True)
        data = out.getvalue()
        mime = 'image/png'
        if len(data) > 600_000:
            out = io.BytesIO()
            rgb.save(out,format='JPEG',quality=76,optimize=True)
            data, mime = out.getvalue(), 'image/jpeg'
        return base64.b64encode(data).decode(), mime, f'{rgb.width}x{rgb.height} {len(data)//1024}KB'
    except Exception as exc:
        print(f'[Vision] Image optimization skipped: {type(exc).__name__}', flush=True)
        return base64.b64encode(raw_png).decode(), 'image/png', 'original PNG'


# Explicitly chosen Creator actions are more reliable than asking vision to guess
# whether a rough squiggle means a spring, a wing, or something else.
INTENT_DEFAULTS = {
    'bounce': {'type':'bounce', 'activation':'contact', 'launch_speed':10, 'angle_deg':0},
    'thrust': {'type':'thrust', 'activation':'button', 'acceleration':12,'angle_deg':90,'duration_s':1.5},
    'lift': {'type':'lift', 'activation':'button', 'acceleration':12,'angle_deg':90,'duration_s':5},
    'glide': {'type':'glide','activation':'always','launch_speed':2.5},
    'roll': {'type':'roll','activation':'button','acceleration':8,'duration_s':5},
    'attach': {'type':'attach','activation':'button'},
    'hinge': {'type':'hinge','activation':'always'},
    'push_pull': {'type':'push_pull','activation':'button','acceleration':12,'duration_s':2}
}


def infer_action_from_text(intent):
    """Very conservative fallback only when AI said mystery/support-only."""
    if not intent:
        return 'auto'
    t = intent.lower()
    if re.search(r"\b(?:don't|do not|not)\s+(?:want\s+to\s+)?(?:bounce|swing|fly|roll|glide)\b", t):
        return 'auto'
    patterns = (
        ('bounce', r'\b(?:bounce|trampoline|springboard|launch pad|spring pad)\b'),
        ('thrust', r'\b(?:rocket|boost|propel|thruster)\b'),
        ('lift', r'\b(?:lift|carry me|fly me|flap|balloon|helicopter)\b'),
        ('glide', r'\b(?:glide|parachute|float down|slow my fall|umbrella)\b'),
        ('roll', r'\b(?:roll|wheels|skateboard|drive|cart)\b'),
        ('hinge', r'\b(?:pivot|seesaw|hinge|lever)\b'),
        ('push_pull', r'\b(?:magnet|vacuum|pull me|push me|suction)\b'),
    )
    for category, pattern in patterns:
        if re.search(pattern, t):
            return category
    return 'auto'


def apply_explicit_intent(spec, intent_mode):
    """Optional rescue for Creator-selected intent; AUTO never fabricates a category."""
    if spec['status'] != 'ok':
        return spec
    if intent_mode == 'support':
        spec['components'] = [support()]
        return spec
    if intent_mode not in INTENT_DEFAULTS:
        return spec
    if any(c['type'] == intent_mode for c in spec['components']):
        return spec
    new_parts = list(spec['components'])
    if len(new_parts) >= 4:
        new_parts.pop()  # Preserve the creator-selected action within the 3-special limit.
    new_parts.append(INTENT_DEFAULTS[intent_mode].copy())
    spec = validate({**spec,'components':new_parts})
    if spec['name'] == 'mystery shape':
        spec['name'] = intent_mode.replace('_',' ') + ' invention'
    spec['message'] = ('Using your selected ' + intent_mode.replace('_',' ') +
                       ' function. Place it in the world and interact.')[:120]
    return spec


def model_call(image_b64, goal, timeout, intent="", intent_mode="auto", mime="image/png"):
    key = os.getenv('BITDEER_API_KEY','').strip()
    if not key:
        raise RuntimeError('BITDEER_API_KEY is not set.')
    # The image goes first and the goal last, as a tie-breaker: read first, the goal leaks into recognition
    # ("get across the gap" turns every drawing into a bridge; docs/interpret-prompt.md).
    prompt_text = ('Identify the drawing first. Analyze it, including functional clues, labels, and arrows.\n'
                   'Creator intended action: ' + (intent or 'not supplied') + '\n'
                   'Creator selected mechanic: ' + intent_mode + '\n'
                   'Puzzle goal (tie-breaker only, never changes what was drawn): ' + goal + '\n'
                   'Return only the required JSON. /no_think')
    body = {
        'model': MODEL,
        'messages': [
            {'role':'system','content':PROMPT},
            {'role':'user','content':[
                {'type':'image_url','image_url':{'url':f'data:{mime};base64,{image_b64}'}},
                {'type':'text','text':prompt_text}
            ]}
        ],
        'max_tokens':max(650,min(2200,int(os.getenv('BITDEER_MAX_TOKENS','1100')))),
        'temperature':0.1, 'stream':False
    }
    if USE_SCHEMA:
        body['response_format']={'type':'json_schema','json_schema':{
            'name':'thinkink_invention','strict':True,'schema':SCHEMA}}
    elif JSON_MODE:
        body['response_format']={'type':'json_object'}
    # The open-source Qwen3.8 model supports this chat-template option. Bitdeer
    # may not forward it; automatically fall back to plain /no_think on HTTP 400.
    if DISABLE_THINKING and MODEL.startswith('Qwen/'):
        body['chat_template_kwargs']={'enable_thinking':False}

    def cq(value):
        return str(value).replace('\\', '\\\\').replace('"', '\\"').replace('\n','').replace('\r','')

    def request_once(request_body, seconds):
        request_path = None
        try:
            with tempfile.NamedTemporaryFile(prefix='thinkink_request_',suffix='.json',delete=False) as f:
                request_path = f.name
                f.write(json.dumps(request_body).encode('utf-8'))
            curl_config = '\n'.join((
                f'url = "{cq(ENDPOINT)}"', 'request = "POST"',
                f'header = "Authorization: Bearer {cq(key)}"',
                'header = "Content-Type: application/json"',
                f'data-binary = "@{cq(request_path)}"',
            )) + '\n'
            response = subprocess.run(
                ['curl','-q','--silent','--show-error','--max-time',str(max(0.5,seconds)),
                 '--write-out','\n%{http_code}','--config','-'],
                input=curl_config,text=True,capture_output=True,
                timeout=max(1.5, seconds+1))
        except FileNotFoundError as err:
            raise RuntimeError('curl is not installed') from err
        except subprocess.TimeoutExpired as err:
            raise RuntimeError('Bitdeer request timed out') from err
        finally:
            if request_path:
                Path(request_path).unlink(missing_ok=True)
        if response.returncode != 0:
            raise RuntimeError('Bitdeer connection error: '+response.stderr.strip()[:350])
        content, sep, status = response.stdout.rpartition('\n')
        if not sep or not status.strip().isdigit():
            raise RuntimeError('Unexpected Bitdeer HTTP response')
        return int(status.strip()), content

    started = time.monotonic()
    status, response_text = request_once(body, timeout)
    if status in (400,422) and 'chat_template_kwargs' in body:
        # Compatibility fallback for gateways which don't forward Qwen-specific flags.
        body.pop('chat_template_kwargs')
        remaining = timeout - (time.monotonic()-started)
        if remaining > 1:
            print('[Bitdeer] Retrying without vendor-specific thinking flag',flush=True)
            status, response_text = request_once(body, remaining)
    if status != 200:
        raise RuntimeError(f'Bitdeer HTTP {status}: {response_text[:550]}')
    try:
        data = json.loads(response_text)
        message = data['choices'][0]['message']
        content = message['content']
        if isinstance(content,list):
            content=''.join(x.get('text','') for x in content if isinstance(x,dict))
        if not content:
            raise ValueError('Bitdeer returned no final answer (thinking may be consuming tokens)')
        print(f'[Bitdeer] Completed in {time.monotonic()-started:.1f}s '
              f'({data.get("usage",{}).get("total_tokens","?")} tokens)',flush=True)
        return extract_json(content)
    except (IndexError,KeyError,TypeError,json.JSONDecodeError) as err:
        raise ValueError('Bitdeer returned malformed chat response') from err


def interpret(req):
    start = time.monotonic()
    if not isinstance(req,dict): raise ValueError('Expected a JSON object')
    image = req.get('board_png_base64')
    if not isinstance(image,str) or len(image) > MAX_BODY:
        raise ValueError('board_png_base64 is missing or too large')
    try: raw_image = base64.b64decode(image, validate=True)
    except Exception as err: raise ValueError('Invalid base64 PNG') from err
    if not raw_image.startswith(PNG): raise ValueError('board_png_base64 must contain a PNG')
    capture = req.get('capture_id')
    run_id = req.get('run_id')
    if not isinstance(capture,str) or not capture or len(capture)>128:
        raise ValueError('capture_id must be a nonempty string')
    if isinstance(run_id,bool) or not isinstance(run_id,int):
        raise ValueError('run_id must be an integer')
    goal = str(req.get('goal') or 'interpret the drawing')[:200]
    intent = str(req.get('intent') or '')[:180]
    intent_mode = str(req.get('intent_mode') or 'auto').lower().replace('/', '_')
    if intent_mode not in (*INTENT_DEFAULTS, 'auto','support'):
        intent_mode = 'auto'
    base = {'capture_id':capture, 'run_id':run_id,
            'provider':'mock' if FAKE else 'bitdeer'}
    error = None
    spec = None
    fingerprint = hashlib.sha256(raw_image + b'|' + goal.encode() + b'|' +
                                  intent.encode() + b'|' + intent_mode.encode() + b'|' +
                                  MODEL.encode() + b'|' + PROMPT.encode()).hexdigest()
    with _CACHE_LOCK:
        cached = _CACHE.get(fingerprint)
        if cached is not None:
            _CACHE.move_to_end(fingerprint)
    if cached is not None and not FAKE:
        spec = copy.deepcopy(cached)
        print('[Vision] Inference cache hit',flush=True)
    elif FAKE:
        # Only verifies wiring; does NOT recognize the picture.
        spec = fake_demo(next_fake_text(goal + ' ' + intent))
    else:
        image_b64, mime, image_info = prepare_vision_image(raw_image)
        print(f'[Vision] Sending cropped image: {image_info}, mode={intent_mode}',flush=True)
        for attempt in range(2):
            remaining = MAX_LATENCY_S-(time.monotonic()-start)
            if remaining <= 0.25: break
            try:
                spec = validate(model_call(image_b64,goal,remaining, intent,intent_mode,mime))
                inferred_mode = intent_mode
                if (inferred_mode == 'auto' and len(spec['components']) == 1 and
                        spec['name'] == 'mystery shape'):
                    inferred_mode = infer_action_from_text(intent)
                spec = apply_explicit_intent(spec, inferred_mode)
                break
            except Exception as err:
                error = str(err)
                print(f'[Bitdeer] attempt {attempt+1} failed: {error}',flush=True)
    if spec is not None and DEMO_MODE and cached is None:
        spec = demo_normalize(spec)
    if spec is not None and not FAKE and cached is None:
        with _CACHE_LOCK:
            _CACHE[fingerprint] = copy.deepcopy(spec)
            _CACHE.move_to_end(fingerprint)
            while len(_CACHE) > _CACHE_CAPACITY:
                _CACHE.popitem(last=False)
    if spec is None:
        spec = {'status':'error','name':'','message':'AI unavailable. Try again.',**identity({}),
                'width_m':0.5,'components':[support()], 'attributes':attributes({},0.5)}
    spec.update(base)
    spec['ink_cost'] = ink_cost(spec)
    spec['latency_ms'] = round((time.monotonic()-start)*1000)
    return spec


class Handler(BaseHTTPRequestHandler):
    def reply(self, status, data):
        body = json.dumps(data, separators=(',', ':')).encode('utf-8')
        self.send_response(status)
        self.send_header('Content-Type','application/json; charset=utf-8')
        self.send_header('Content-Length',str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == '/health':
            self.reply(200, {'ok':True,'provider':'mock' if FAKE else 'bitdeer',
                             'model':MODEL,'configured':FAKE or bool(os.getenv('BITDEER_API_KEY'))})
        else: self.reply(404,{'error':'Not found'})

    def do_POST(self):
        if self.path != '/interpret': return self.reply(404,{'error':'Not found'})
        length = self.headers.get('Content-Length','')
        try:
            n=int(length)
            if not (1<=n<=MAX_BODY): raise ValueError('Request too large or empty')
            body=json.loads(self.rfile.read(n))
            result=interpret(body)
        except (ValueError, json.JSONDecodeError) as err:
            return self.reply(400,{'error':str(err)})
        except Exception as err:
            print(f'[Backend] unexpected failure: {err}',flush=True)
            return self.reply(200,{'status':'error','message':'AI unavailable. Try again.',
                                   'name':'',**identity({}),'width_m':0.5,'components':[support()], 'attributes':attributes({},0.5),
                                   'capture_id':body.get('capture_id','') if isinstance(body,dict) else '',
                                   'run_id':body.get('run_id',0) if isinstance(body,dict) else 0,
                                   'ink_cost':0,'provider':'bitdeer','latency_ms':0})
        self.reply(200,result)


def main():
    port = int(os.getenv('THINKINK_PORT', '8000'))
    print(f'ThinkInk backend listening on http://127.0.0.1:{port}, model={MODEL}, fake={FAKE}, demo={DEMO_MODE}',flush=True)
    ThreadingHTTPServer(('127.0.0.1',port),Handler).serve_forever()

if __name__=='__main__': main()

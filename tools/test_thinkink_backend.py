import base64
import io
import json
import os
import unittest
from unittest.mock import patch
import thinkink_backend as b

class BackendTests(unittest.TestCase):
    def test_model_reply_cleanup(self):
        data=b.extract_json('<think>ignore me</think>```json\n{"components":[{"type":"support"}]}\n```')
        self.assertEqual(data['components'][0]['type'],'support')
    def test_normalization(self):
        out=b.validate({'status':'ok','name':'ROCKET','width_m':900,'components':[
            {'type':'support'}, {'type':'THRUST','acceleration':55,'duration_s':9,'angle_deg':45},
            {'type':'thrust','acceleration':4}, {'type':'bogus'}]})
        self.assertEqual(out['name'],'rocket')
        self.assertEqual(out['width_m'],6)
        self.assertEqual(len(out['components']),2)
        self.assertEqual(out['components'][1]['acceleration'],25)
        self.assertEqual(out['components'][1]['duration_s'],3)
    def test_error_no_cost(self):
        self.assertEqual(b.ink_cost({'status':'rejected'}),0)
        self.assertEqual(b.ink_cost({'status':'error'}),0)
    def test_fake_integration(self):
        sample=base64.b64encode(b.PNG+b'123').decode()
        with patch.object(b,'FAKE',True):
            out=b.interpret({'board_png_base64':sample,'capture_id':'demo','run_id':2})
        self.assertEqual(out['run_id'],2)
        self.assertEqual(out['provider'],'mock')
        self.assertEqual(out['components'][1]['type'],'thrust')
        self.assertGreater(out['ink_cost'],0)
    def test_invalid_payload(self):
        with self.assertRaises(ValueError):b.interpret({'board_png_base64':'X','run_id':3,'capture_id':'c'})

    def test_eight_core_abilities_and_stretch(self):
        for kind in ('lift','glide','roll','attach','hinge','push_pull'):
            result=b.validate({'status':'ok','name':'weird thing','width_m':2,
                               'components':[b.support(),{'type':kind}]})
            self.assertEqual(result['components'][1]['type'],kind)
            self.assertEqual(len(result['components'][1]),8)
            self.assertEqual(len(result['attributes']),15)
        self.assertEqual(len(b.ATTR_BOUNDS),15)
    def test_max_three_special_abilities(self):
        out=b.validate({'status':'ok','components':[b.support(),
            *({'type':k} for k in ('lift','attach','glide','roll','hinge'))]})
        self.assertEqual(len(out['components']),4)
    def test_character_capacity_tiny_vs_giant_bird(self):
        small=b.fake_demo('tiny bird')
        big=b.fake_demo('giant bird')
        self.assertLess(small['attributes']['carry_capacity_kg'],6)
        self.assertGreater(big['attributes']['carry_capacity_kg'],6)
        self.assertIn('lift',[c['type'] for c in big['components']])
        self.assertIn('attach',[c['type'] for c in big['components']])
    def test_attribute_clamps(self):
        out=b.validate({'status':'ok','width_m':3,'components':[b.support()],
                        'attributes':{'mass_kg':999,'carry_capacity_kg':-30,
                                      'range_m':15,'friction':2}})
        self.assertEqual(out['attributes']['mass_kg'],20)
        self.assertEqual(out['attributes']['carry_capacity_kg'],0)
        self.assertEqual(out['attributes']['range_m'],8)
        self.assertEqual(out['attributes']['friction'],1)
    def test_old_rocket_keeps_working(self):
        out=b.validate({'status':'ok','name':'rocket','width_m':3.5,
            'components':[b.support(),{'type':'thrust','acceleration':15,'duration_s':1.5,'angle_deg':45}]})
        self.assertEqual(out['components'][1]['angle_deg'],45)
        self.assertEqual(out['components'][1]['duration_s'],1.5)
        self.assertEqual(out['attributes']['mass_kg'],3)

    def test_explicit_bounce_corrects_mystery(self):
        specimen = b.validate({'name':'mystery shape','status':'ok','components':[b.support()]})
        fixed = b.apply_explicit_intent(specimen,'bounce')
        self.assertEqual([c['type'] for c in fixed['components']], ['support','bounce'])
        self.assertEqual(fixed['name'],'bounce invention')

    def test_grappling_rope_is_cut(self):
        out=b.validate({'status':'ok','name':'rope','components':[b.support(),{'type':'tether'}]})
        self.assertEqual([c['type'] for c in out['components']],['support'])
        self.assertNotIn('tether',b.INTENT_DEFAULTS)
        self.assertEqual(b.infer_action_from_text('I want to swing across the gap'),'auto')
        self.assertEqual(b.apply_explicit_intent(b.validate({'status':'ok','components':[b.support()]}),'tether')['components'],[b.support()])

    def test_auto_does_not_invent(self):
        specimen = b.validate({'name':'mystery shape','status':'ok','components':[b.support()]})
        self.assertEqual(len(b.apply_explicit_intent(specimen,'auto')['components']),1)
        self.assertEqual(b.infer_action_from_text('I want to bounce over the gap'),'bounce')
        self.assertEqual(b.infer_action_from_text("I don't want to bounce"),'auto')

    def test_cropped_image_preserves_diagonal_arrow(self):
        try:
            from PIL import Image, ImageDraw
        except ImportError:
            self.skipTest('Pillow is optional')
        image = Image.new('RGB',(960,540),'white')
        draw = ImageDraw.Draw(image)
        draw.line((415,380,525,260),fill='black',width=6)
        draw.line((525,260,490,270),fill='black',width=5)
        draw.line((525,260,518,297),fill='black',width=5)
        import io
        buf=io.BytesIO();image.save(buf,format='PNG')
        optimized,mime,description=b.prepare_vision_image(buf.getvalue())
        self.assertEqual(mime,'image/png')
        result=Image.open(io.BytesIO(base64.b64decode(optimized)))
        self.assertLess(result.width,960)
        self.assertLess(result.height,540)
        self.assertLess(result.getpixel((result.width//2,result.height//2)),245)

    def test_image_caching_preserves_per_request_ids(self):
        from unittest.mock import patch
        image = base64.b64encode(b.PNG+b'repeat').decode()
        model_result={'status':'ok','name':'spring','width_m':2,'components':[b.support(),{'type':'bounce'}]}
        with patch.object(b,'FAKE',False),patch.object(b,'prepare_vision_image',return_value=('bytes','image/png','short')),patch.object(b,'model_call',return_value=model_result) as call:
            a=b.interpret({'board_png_base64':image,'goal':'cross gap','capture_id':'1','run_id':1})
            c=b.interpret({'board_png_base64':image,'goal':'cross gap','capture_id':'2','run_id':2})
        self.assertEqual(call.call_count,1)
        self.assertEqual((a['capture_id'],c['capture_id']),('1','2'))
        self.assertEqual(a['components'][1]['type'],'bounce')

    def test_demo_filters_extra_abilities(self):
        spec=b.validate({'status':'ok','name':'rocket umbrella','width_m':3,
            'components':[b.support(),{'type':'glide'}, {'type':'thrust','acceleration':15,'duration_s':1.7}, {'type':'roll'}]})
        demo=b.demo_normalize(spec)
        self.assertEqual([p['type'] for p in demo['components']], ['support','thrust'])
        self.assertEqual(demo['components'][1]['acceleration'],14)
        self.assertEqual(demo['components'][1]['duration_s'],1.6)

    def test_demo_tiny_bird_fails_giant_bird_lifts(self):
        def bird(w):
            return b.demo_normalize(b.validate({'status':'ok','name':'bird','width_m':w,
                'components':[b.support(), {'type':'lift','activation':'button','acceleration':23},
                              {'type':'attach'}, {'type':'glide'}]}))
        small=bird(1.1); big=bird(4.3)
        self.assertEqual(len(small['components']),2)
        self.assertEqual(len(big['components']),2)
        self.assertLess(small['attributes']['carry_capacity_kg'],6)
        self.assertGreater(big['attributes']['carry_capacity_kg'],6)

    def test_demo_support_only_does_not_invent_powers(self):
        spec=b.demo_normalize(b.validate({'status':'ok','name':'odd doodle','width_m':2,
                                          'components':[b.support()]}))
        self.assertEqual([p['type'] for p in spec['components']],['support'])

    def test_demo_keeps_two_special_abilities(self):
        spec=b.demo_normalize(b.validate({'status':'ok','name':'rocket bird','width_m':4,
                                          'components':[b.support(),{'type':'lift'}, {'type':'thrust'}, {'type':'attach'}]}))
        self.assertEqual([p['type'] for p in spec['components']],['support','lift','thrust'])

    def test_demo_keeps_food_identity_for_the_gatekeeper(self):
        sample=base64.b64encode(b.PNG+b'apple').decode()
        with patch.object(b,'FAKE',True),patch.object(b,'DEMO_MODE',True):
            out=b.interpret({'board_png_base64':sample,'goal':'apple','capture_id':'f','run_id':4})
        self.assertIn('edible',out['traits'])
        self.assertEqual([p['type'] for p in out['components']],['support'])

    def test_vendor_flag_falls_back_on_bad_request(self):
        import tempfile, re
        from types import SimpleNamespace
        observed=[]
        def respond(args,input,text,capture_output,timeout):
            path=re.search(r'data-binary = "@([^"\n]+)"',input).group(1)
            with open(path) as stream: payload=json.load(stream)
            observed.append(payload)
            if len(observed)==1:
                return SimpleNamespace(returncode=0,stdout='unsupported option\n400',stderr='')
            return SimpleNamespace(returncode=0,stdout=json.dumps({'choices':[{'message':{'content':'{"status":"ok","components":[{"type":"support"}]}'}}]})+'\n200',stderr='')
        with patch.dict(os.environ,{'BITDEER_API_KEY':'test_not_real'}), patch.object(b,'DISABLE_THINKING',True),patch.object(b,'USE_SCHEMA',False),patch('subprocess.run',side_effect=respond):
            answer=b.model_call('dummy','cross gap',5)
        self.assertEqual(answer['status'],'ok')
        self.assertEqual(len(observed),2)
        self.assertIn('chat_template_kwargs',observed[0])
        self.assertNotIn('chat_template_kwargs',observed[1])

    def test_identity_passes_through_and_is_filtered(self):
        out=b.validate({'status':'ok','name':'Apple','material':'PLANT','traits':['Edible','tasty','edible','lighter-than-air'],
                        'adjectives':['Red'],'components':[b.support()]})
        self.assertEqual((out['name'],out['material']),('apple','plant'))
        self.assertEqual(out['traits'],['edible','lighter_than_air'])
        self.assertEqual(out['adjectives'],['red'])
        self.assertEqual(len(out['attributes']),15)
    def test_identity_defaults_and_limits(self):
        out=b.validate({'status':'ok','material':'unobtainium','traits':['hot','cold',*b.TRAITS],'components':[b.support()]})
        self.assertEqual(out['material'],'wood')
        self.assertIn('hot',out['traits'])
        self.assertNotIn('cold',out['traits'])
        self.assertEqual(len(out['traits']),b.MAX_TRAITS)
        self.assertEqual(b.validate({'status':'ok','components':[b.support()]})['traits'],[])
    def test_fake_foods_are_edible_and_bridge_is_not(self):
        for food in ('apple','banana','sandwich'):
            out=b.fake_demo(food)
            self.assertEqual(out['name'],food)
            self.assertIn('edible',out['traits'])
        self.assertNotIn('edible',b.fake_demo('bridge')['traits'])
        self.assertEqual(b.fake_demo('blank board')['status'],'rejected')
    def test_fake_sequence_answers_each_build_in_turn(self):
        sample=base64.b64encode(b.PNG+b'seq').decode()
        with patch.object(b,'FAKE',True),patch.object(b,'_FAKE_SEQUENCE',['bridge','apple']),patch.object(b,'_FAKE_STEP',[0]):
            names=[b.interpret({'board_png_base64':sample,'capture_id':str(i),'run_id':i,
                                'goal':'something the gatekeeper will accept'})['name'] for i in range(3)]
        self.assertEqual(names,['rope bridge','apple','rope bridge'])
    def test_image_goes_before_goal(self):
        import re
        from types import SimpleNamespace
        seen=[]
        def respond(args,input,text,capture_output,timeout):
            path=re.search(r'data-binary = "@([^"\n]+)"',input).group(1)
            with open(path) as stream: seen.append(json.load(stream))
            return SimpleNamespace(returncode=0,stdout=json.dumps({'choices':[{'message':{'content':'{"status":"ok","components":[{"type":"support"}]}'}}]})+'\n200',stderr='')
        with patch.dict(os.environ,{'BITDEER_API_KEY':'test_not_real'}),patch('subprocess.run',side_effect=respond):
            b.model_call('dummy','get B across the gap',5)
        content=seen[0]['messages'][1]['content']
        self.assertEqual([c['type'] for c in content],['image_url','text'])
        self.assertIn('tie-breaker',content[1]['text'])

if __name__=='__main__':unittest.main()

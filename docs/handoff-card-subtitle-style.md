# Handoff: restyle the "came to life" card and the subtitles as paper notes

This is for a Claude Code (or other) session on Wesley's PC. The project uses Unity 6000.3.18f1, and the Editor is
usually open, so drive it with the `unity` CLI (`unity command ...`). Work on `main`, which is current as of `c02d55a`.

## Goal

The HUD uses a hand-drawn paper style: cream paper boxes with wobbly marker outlines, handwriting, masking tape and
slight tilts. The two remaining UI pieces still use Unity's default look. The drawing card is a flat slate-blue panel
with a system font, and the subtitles are a black 70% box with white text. Restyle both to match, without changing
what they say or when they show.

**Done when** a screenshot of the card over the level, and of a subtitle line, looks like it belongs next to the timer
tag, the top sign and the co-op panel.

## The style to match

Everything is IMGUI, using the helpers in `Assets/Scripts/UI/Sketch.cs`:

| Helper | Use |
|---|---|
| `Sketch.Paper`, `Ink`, `Muted`, `Marker` (yellow), `MarkerHover`, `Spent` | The palette. Don't add new greys or blues. |
| `Sketch.Text(size, color, alignment, bold)` | A style in the handwriting font (Ink Free, falling back to Segoe Print, then Comic Sans). Make styles once, in `EnsureStyles`. |
| `Sketch.Box(rect, fill, seed, shadow)` | A paper box with a wobbly outline. `seed` picks the wobble; give each box its own constant. Textures are cached by size, so avoid sizes that change every frame. |
| `Sketch.Label(rect, text, style)` | Use this instead of `GUI.Label`. It keeps text crisp under the 1080p-scaled `GUI.matrix`. |
| `Sketch.Button(rect, label, fill, seed, style)` | A paper button that turns marker yellow on hover. |
| `Sketch.Tape(centre, degrees, w, h)` / `Sketch.Tilted(pivot, degrees, draw)` | Masking tape on a note, and a slight rotation (−0.6° to −1.5° in the HUD). |

These files show the look in use:

- `Assets/Scripts/UI/Hud.cs`: the timer tag, the centre sign (tilted −0.6°), and the win banner (`Box` plus `Tape`,
  tilted −1.5°, 60 pt bold title). **The win banner is the closest model for the card.**
- `Assets/Scripts/UI/SpeechBubble.cs`: the guard's bubble, a paper `Box` with 28 pt Ink text. **This is the closest
  model for subtitles.**
- `Assets/Scripts/Multiplayer/CoopHud.cs`: paper and marker buttons, and the yellow "bring to life!" button.

All HUDs lay out in a 1080-tall reference space: `scale = Screen.height / 1080`,
`GUI.matrix = Matrix4x4.Scale(scale)`, and `width = Screen.width / scale`. Keep that.

## 1. The card: `Assets/Scripts/UI/InventionCard.cs`

What it is now: `OnGUI` draws a 1×1 slate texture (`panel`, rgba 0.27/0.32/0.39/0.94) as the panel, then centred
labels in the default font:

- the header `CardText.Header` ("✦ YOUR DRAWING CAME TO LIFE!", teal, 32 pt)
- the title (white, 80 pt)
- the body (34 pt)
- the traits line (teal, 30 pt)
- the action line (42 pt; green when `Worked == true`, coral when `false`, yellow when `null`)
- a default-skin `GUI.Button` reading "LET'S PLAY"

The panel is up to 1100 px wide at y 150, with its height summed from the parts.

Restyle it as a large paper note, like the win banner:

- Draw `Sketch.Box(rect, Sketch.Paper, <seed>, shadow: true)` with one or two `Sketch.Tape` strips at the top, all
  inside `Sketch.Tilted(rect.center, about -1f, ...)`. Remove the `panel` texture and its `Destroy`.
- Make every style with `Sketch.Text`:
  - header: `Muted`, about 30 pt
  - title: `Ink` bold, 72–80 pt
  - body: `Ink`, about 32 pt
  - traits: `Muted` or `Ink`, about 28 pt
  - action: bold, about 38 pt, drawn **on a marker swipe** (see below)
  - Body and action can wrap: set `wordWrap = true` on those styles, since `Sketch.Text` turns it off, and size them
    with `CalcHeight` as now.
- Action colour, because pastel text is unreadable on cream paper: keep Ink text and show the verdict as a highlighter
  swipe behind it. Use `GUI.color` plus `Texture2D.whiteTexture`, as `Sketch.Link` does. The swipe is pale green when
  it worked, pale red/coral when it didn't, and `Marker` yellow when there's nothing to judge.
- Replace the button with `Sketch.Button(rect, "LET'S PLAY", Sketch.Marker, <seed>, buttonStyle)`, keeping the same
  300×70 size and centring.
- Optional: draw the actual drawing as a small taped-on thumbnail beside the title. Don't let it change the layout
  height logic much, and treat it as a stretch goal. The card doesn't have the texture today: `InventionCard.Show`
  would need an optional `Texture2D` parameter, and every caller in `RunManager` would have to pass the paper.

Behaviour you **must** keep:

- `Show` sets `Time.timeScale = 0` and dismissing restores it to 1.
- Space dismisses the card in `OnGUI`, and Enter dismisses it through `RunManager`'s Cast binding
  (`if (InventionCard.Visible) InventionCard.Hide()`).
- `GUI.depth = -100` keeps it above the other HUDs.
- `CardText` is pure and shouldn't change. `AutomaticPlacementTests` relies on `InventionCard.Visible` /
  `InventionCard.Hide()`.
- Drawings pop in behind the paused card on unscaled time (`Cutout.PopIn`). Leave enough of the lower screen visible to
  see the drawing appear. The card starts at y 150, so don't make it much taller.

## 2. Subtitles: `Assets/Scripts/UI/Subtitles.cs`

What it is now: a centred box at the bottom of the screen (70 px from the bottom, up to 1400 px wide), drawn as
`GUI.Box` with a black 70% texture and white 44 pt default-font text.

Restyle it as a paper note, like the speech bubble:

- Draw `Sketch.Box(rect, Sketch.Paper, <seed>, shadow: true)`, with optional tape, tilted slightly (about +0.6°) so it
  doesn't line up with the top sign.
- Text: `Sketch.Text(about 32–36 pt, Sketch.Ink, MiddleCenter)` with `wordWrap = true`, drawn with `Sketch.Label`.
- Sizing: `Sketch.Box` caches a texture per pixel size, up to 48, then flushes. Round the width and height up to 20 px
  steps, as `Hud`'s sign does in 40 px steps, so changing lines reuse textures.
- Remove `boxTexture` and `boxStyle`, and their `Destroy`.
- Keep the queue, `Show` / `ShowNow` / `Clear` / `Current`, unscaled timing, and the `[Subtitle]` log line exactly as
  they are.
- The build bar sits at the bottom right (`Hud.DrawBuildBar`, 390×76 at `width-420`, `1080-104`). Make sure a wide
  subtitle doesn't cover it: cap the width or move the note up.

## Verify

1. Recompile through the CLI (`unity command script recompile`, then poll `script recompile_status`), and check that
   `compilationFailed` is false.
2. Enter Play mode (`unity command editor.playmode start`). Show the card and a subtitle with `script eval`:
   - Card: `CalHacks.InventionCard.Show(CalHacks.InventionSpec.FromLabel("rocket"), "PRESS E TO RIDE · SPACE TO JUMP OFF", null)`.
     Also try `true` and `false` for the third argument, to see the green and red swipes.
   - Subtitle: `CalHacks.Subtitles.ShowNow("Couldn't read that drawing. Press BUILD to try again.", 10f)`.
3. Capture `unity command capture game_view --save_path Temp/x.png`. That path lands in `Assets/Temp`, so move the
   screenshot out and delete `Assets/Temp` and `Assets/Temp.meta` afterwards. Compare it against the HUD at 1080p and
   at the Game view's 16:9.
4. Check that Space and Enter still dismiss the card, and that the game resumes.

## Hands off

- `CoopHud.cs` and `SpeechBubble.cs` are other people's recent work. Read them for style, but don't edit them.
- The browser drawing pad (SpacetimeDB whiteboard) is owned by a teammate. It's out of scope.
- ChatGPT has also been editing `RunManager.cs` and the placement files. Run `git status` before you start, and
  don't touch those files for this task.

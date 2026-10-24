# ThinkInk / Bitdeer recognition and latency improvements

This change applies **after** the Universal Abilities patch and the 30-second timeout patch. It keeps the `/interpret` response JSON and all current mechanics unchanged.

## What changed

- In the DRAW canvas, enter a short action in **What should it DO?** (e.g. `Swing me across with this rope`, `Bounce me diagonally right`, `Tiny bird carries me`). The selected ability button remains optional; AUTO still uses the model's judgment.
- The request now sends distinct `goal`, `intent`, and `intent_mode` fields. The prompt tells the vision model to interpret arrows, labels, functional mechanisms, and Creator verbs. The backend only forces a missing ability when the Creator explicitly selects its category; AUTO does not automatically force abilities. If AI returns `mystery shape` and only SUPPORT, an unambiguous action sentence can provide a last-resort ability.
- The Python service **optionally uses Pillow** to crop excess whiteboard background and limit the image to 768 pixels on its largest axis while retaining arrows and labels. The original cutout and Unity collider are untouched. If Pillow isn't installed, the backend sends the original image as before.
- For faster generation, the default response format is `json_object` instead of a large strict JSON Schema, with server-side validation retained. Restore strict schema with `BITDEER_USE_SCHEMA=1` if desired.
- The Qwen3.8 open-source model supports `chat_template_kwargs: {"enable_thinking": false}`; the backend tries this by default and automatically retries without the option if the gateway rejects it with HTTP 400/422. Disable it entirely with `BITDEER_DISABLE_THINKING=0`.
- Model output limit defaults to 1100 tokens instead of 1600. Change via `BITDEER_MAX_TOKENS=1600` if outputs become truncated. The API still has a 30-second overall budget (configurable via `BITDEER_TIMEOUT_S`).
- Repeated requests with exactly the same drawing, goal and intent reuse successful results from an in-memory cache. IDs and latency are still assigned for the current request. Restarting the backend clears the cache.

## Install/start (macOS)

```bash
python3 -m pip install Pillow
python3 tools/thinkink_backend.py
```

Pillow is recommended but optional. Keep `BITDEER_API_KEY` set in the Terminal environment, never in Unity scripts or committed files. Quit the previous backend with Ctrl+C before restarting.

## Test in Unity

1. Start with AUTO and draw a spring with an **up-right arrow**, then describe `Bounce me diagonally right`. Check `bounce.angle_deg` is positive.
2. Draw an ambiguous curved line and describe `I want to swing across the gap`; alternatively select TETHER. Check the backend returns tether; for selected TETHER the server guarantees it even if the model only returns SUPPORT.
3. Draw a tiny bird labeled `tiny` and a giant one labeled `huge`. Describe that each should carry the player. The AI should assign LIFT + ATTACH but different carrying-capacity estimates. Actual success is determined by Unity physics.
4. Draw a rocket with no intent. AUTO should continue to assign THRUST without forcing a mechanic.
5. Check backend logs for `[Vision] Sending cropped image` and `[Bitdeer] Completed in ...s` to compare performance.

## Known limitations

- The model's object understanding still cannot be guaranteed from a truly ambiguous sketch. Ask the Creator for a verb/action when needed.
- Hand-drawn annotation arrows are currently included in the Unity cutout if drawn in the same black-ink layer. Separating object ink from motion-annotation ink is future work.
- This patch does not modify collider geometry, balancing, tether world-anchor placement, or Unity Play-mode physics.
- A `json_object` response is not a substitute for backend validation; the validator continues to clamp, normalize and reject invalid outputs.
- New vendor-specific request options have not been verified with your actual Bitdeer account. They are optional and have a fallback.

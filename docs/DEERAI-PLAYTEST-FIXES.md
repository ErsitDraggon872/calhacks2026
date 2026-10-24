# DeerAI playtest fixes (incremental patch)

- `Cutout` builds a visual-only quad mesh so adding a Rigidbody never sees a temporary non-convex MeshCollider.
- New inventions use 0.10-metre grid cells and 18% cell occupancy for silhouette compound BoxColliders. This improves fidelity but is still an approximation, not pixel-perfect collisions.
- Thrust inventions remain parked until activated, have gravity disabled while their motor burns, and fall after fuel runs out. E automatically boards the nearby invention; Space detaches.
- A 12-second instruction pop-up explains E/contact interactions and can be dismissed.
- Digital drawing toolbar supports AUTO, BOUNCE, THRUST, SUPPORT as optional intent hints; AUTO still uses the AI drawing alone. Diagonal arrow from a spring/pad cues BOUNCE, but a standalone arrow should not.

## Try it
1. Keep your `thinkink_backend.py` process running, with `BITDEER_API_KEY` configured.
2. Open Unity, Play -> DRAW -> AUTO. Draw a roughly recognizable rocket, BUILD, press E near it.
3. Draw a shallow trampoline rectangle plus up-right arrow. If recognition fails, select BOUNCE and rebuild.
4. Press Space while riding the rocket to jump off.
5. Check Unity Console for warnings, and backend logs for model interpretation.

## Limitations
This patch is source-reviewed and patch-applied, but was NOT run in a Unity Editor in this environment. Collider resolution has a performance cost on intricate drawings. Rocket boarding snaps the player to the top of the cutout to make upright rocket sketches playable; this is intentional for the hackathon prototype. Do not use a silhouette with hundreds of disconnected thin strokes if you care about physics performance. The AI remains probabilistic; the optional explicit intent gives players a recovery route.

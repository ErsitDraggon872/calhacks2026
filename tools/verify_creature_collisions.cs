// Run in the existing Play-mode Editor with `unity command script eval_file --file tools/verify_creature_collisions.cs`.
// Deterministic live checks: no AI or drawing server required. Restores the player after verification.
System.Collections.IEnumerator VerifyCreatureCollisions() {
 var run = CalHacks.RunManager.Instance;
 var player = run.Player;
 var start = player.transform.position;
 var rotation = UnityEngine.Quaternion.LookRotation(UnityEngine.Vector3.right * player.FacingSign);
 var locked = player.InputLocked;
 var mat = (UnityEngine.Material)typeof(CalHacks.RunManager).GetField("inventionMaterial", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(run);
 var root = new UnityEngine.GameObject("Creature collision verification");
 var checks = new System.Collections.Generic.List<string>();
 var origin = new UnityEngine.Vector3(150f, 10f, 0f);
 UnityEngine.GameObject Box(string name, UnityEngine.Vector3 center, UnityEngine.Vector3 size) {
  var go = new UnityEngine.GameObject(name); go.transform.SetParent(root.transform);
  go.transform.position = origin + center; go.AddComponent<UnityEngine.BoxCollider>().size = size; return go;
 }
 UnityEngine.Texture2D Paper() {
  var paper = new UnityEngine.Texture2D(8, 8);
  paper.SetPixels(Enumerable.Repeat(UnityEngine.Color.black, 64).ToArray()); paper.Apply(); return paper;
 }
 var floor = Box("Floor", new UnityEngine.Vector3(0f, -.5f, 0f), new UnityEngine.Vector3(30f, 1f, 30f));
 var wall = Box("Wall", new UnityEngine.Vector3(1.3f, 2f, 0f), new UnityEngine.Vector3(.1f, 4f, 4f)).GetComponent<UnityEngine.Collider>();
 var ceiling = Box("Ceiling", new UnityEngine.Vector3(0f, 2.3f, 0f), new UnityEngine.Vector3(12f, .1f, 4f)).GetComponent<UnityEngine.Collider>();
 player.InputLocked = true;
 try {
  foreach (var name in new[] { "bird", "frog", "snake", "turtle" }) {
   var creature = CalHacks.Invention.Create(Paper(), origin, new UnityEngine.Vector2(2f, 2f), 1f, mat, root.transform);
   creature.Apply(CalHacks.InventionSpec.FromLabel(name));
   var motion = creature.GetComponent<CalHacks.InventionMotion>();
   var maxX = creature.transform.position.x; var moved = false; var clear = true;
   var until = UnityEngine.Time.time + 1.8f;
   while (UnityEngine.Time.time < until) {
    yield return new UnityEngine.WaitForFixedUpdate();
    maxX = UnityEngine.Mathf.Max(maxX, creature.transform.position.x);
    moved |= (creature.transform.position - origin).sqrMagnitude > .01f;
    foreach (var shape in creature.GetComponentsInChildren<UnityEngine.BoxCollider>())
    foreach (var terrain in new[] { wall, ceiling }) {
     if (UnityEngine.Physics.ComputePenetration(shape, shape.transform.position, shape.transform.rotation,
         terrain, terrain.transform.position, terrain.transform.rotation, out var separation, out var depth) && depth > .015f) clear = false;
    }
   }
   checks.Add(name + " moves=" + moved);
   checks.Add(name + " avoids wall/ceiling=" + clear);
   checks.Add(name + " turns at wall=" + (creature.Cutout.FacingSign < 0f || creature.transform.position.x < origin.x));
   UnityEngine.Object.Destroy(creature.gameObject); yield return null;
  }
  UnityEngine.Object.Destroy(wall.gameObject); UnityEngine.Object.Destroy(ceiling.gameObject); yield return null;
  var block = Box("Placement blocker", new UnityEngine.Vector3(6f, 2f, -2f), new UnityEngine.Vector3(10f, 4f, 16f));
  player.Teleport(origin + UnityEngine.Vector3.up * .05f, UnityEngine.Quaternion.LookRotation(UnityEngine.Vector3.right));
  UnityEngine.Physics.SyncTransforms();
  var count = run.InventionCount;
  var paper = Paper();
  run.SpawnInvention(paper, CalHacks.InventionSpec.Plain());
  checks.Add("blocked drawing retained=" + (run.PendingBuildCount == 1 && paper != null));
  // Successful placement opens the recognition card and pauses gameplay.
  UnityEngine.Object.Destroy(block); yield return new UnityEngine.WaitForSecondsRealtime(.6f);
  checks.Add("blocked drawing auto-spawns=" + (run.PendingBuildCount == 0 && run.InventionCount == count + 1));
  var placed = UnityEngine.Object.FindObjectsByType<CalHacks.Invention>(UnityEngine.FindObjectsSortMode.None).FirstOrDefault(i => i.Cutout.Texture == paper);
  if (placed != null) run.RemoveInvention(placed);
  checks.Add("free build presents paused card=" + (CalHacks.InventionCard.Visible && UnityEngine.Time.timeScale == 0f));
 } finally {
  CalHacks.InventionCard.Hide();
  player.Teleport(start, rotation); player.InputLocked = locked; UnityEngine.Object.Destroy(root);
  System.IO.File.WriteAllText("Temp/creature-collision-results.txt", string.Join("; ", checks));
 }
}
System.IO.File.WriteAllText("Temp/creature-collision-results.txt", "running");
CalHacks.RunManager.Instance.StartCoroutine(VerifyCreatureCollisions()); return "Creature collision verification started";

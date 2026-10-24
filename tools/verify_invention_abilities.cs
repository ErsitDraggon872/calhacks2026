// Run in Play mode: unity command script eval_file --file tools/verify_invention_abilities.cs
// Results: Temp/ability-regressions-result.txt. Uses deterministic specs, no AI/network.
System.Collections.IEnumerator Verify() {
var player=UnityEngine.Object.FindFirstObjectByType<CalHacks.PlayerController>();
var mat=(UnityEngine.Material)typeof(CalHacks.RunManager).GetField("inventionMaterial",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(CalHacks.RunManager.Instance);
var root=new UnityEngine.GameObject("RuntimeAbilityVerification");
CalHacks.Invention Make(string kind,float carry=3f,string activation="button") {
 var t=new UnityEngine.Texture2D(64,32,UnityEngine.TextureFormat.RGBA32,false);
 t.SetPixels32(Enumerable.Repeat(new UnityEngine.Color32(0,0,0,255),64*32).ToArray()); t.Apply();
 var i=CalHacks.Invention.Create(t,new UnityEngine.Vector3(80,10,0),new UnityEngine.Vector2(2,1),2f,mat,root.transform);
 var spec=CalHacks.InventionSpec.Parse(new CalHacks.InterpretResponse{status="ok",name=kind,attributes=new CalHacks.InventionAttributes{carry_capacity_kg=carry},components=new[]{new CalHacks.InterpretComponent{type="support"},new CalHacks.InterpretComponent{type=kind,activation=activation,acceleration=12,duration_s=3,angle_deg=(kind=="roll"||kind=="bounce")?0:90,launch_speed=kind=="glide"?2.5f:10f}}});
 i.Apply(spec); return i;
}
var results=new System.Collections.Generic.List<string>();
var rocket=Make("thrust");
player.BoardInvention(rocket.Abilities,rocket);
rocket.Abilities.TryInteract(player);
results.Add("standing rocket activated="+!rocket.GetComponent<UnityEngine.Rigidbody>().isKinematic);
UnityEngine.Object.Destroy(rocket.gameObject);
yield return new UnityEngine.WaitForSeconds(.1f);
var glider=Make("glide"); glider.Abilities.TryInteract(player);
glider.GetComponent<UnityEngine.Rigidbody>().linearVelocity=UnityEngine.Vector3.down*12f;
yield return new UnityEngine.WaitForSeconds(.8f);
var glideSpeed=glider.GetComponent<UnityEngine.Rigidbody>().linearVelocity.y;
results.Add("loaded glide capped="+(glideSpeed >= -2.6f)+" (vy="+glideSpeed+")");
UnityEngine.Object.Destroy(glider.gameObject);
yield return new UnityEngine.WaitForSeconds(.1f);
var platform=Make("attach"); platform.Abilities.TryInteract(player);
player.GetComponent<UnityEngine.CharacterController>().Move(UnityEngine.Vector3.right*5f);
yield return new UnityEngine.WaitForSeconds(.5f);
results.Add("walking off detached="+!player.IsRiding(platform.Abilities));
UnityEngine.Object.Destroy(platform.gameObject);
yield return new UnityEngine.WaitForSeconds(.1f);
foreach(var kind in new[]{"thrust","lift","roll","car","bounce"}) {
 var i=Make(kind,12f);
 var start=i.transform.position;
 if(kind=="car") {
  var roll=i.Spec.Parts.FirstOrDefault(p=>p.Type==CalHacks.PartType.Roll);
  results.Add("car fallback has roll="+(roll!=null));
 }
 var beforePlayerY=player.transform.position.y;
 var interacted=i.Abilities.TryInteract(player);
 if(kind=="car") {
  var deadline=(float)typeof(CalHacks.InventionPhysics).GetField("poweredUntil",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(i.Abilities);
  results.Add("car engine lasts 15 seconds="+(deadline-UnityEngine.Time.time>=14.9f));
 }
 yield return new UnityEngine.WaitForSeconds(kind=="bounce"?.2f:.6f);
 var moved=i.transform.position-start;
 if(kind=="bounce") results.Add("button bounce launched="+(player.transform.position.y>beforePlayerY+.2f));
 else results.Add(kind+" moves="+((kind=="roll"||kind=="car")?Mathf.Abs(moved.x)>.1f:moved.y>.1f));
 UnityEngine.Object.Destroy(i.gameObject);
 yield return new UnityEngine.WaitForSeconds(.1f);
}
var weak=Make("lift",1f); weak.Abilities.TryInteract(player);
var weakStart=weak.transform.position.y;
yield return new UnityEngine.WaitForSeconds(.6f);
results.Add("weak lift sinks="+(weak.transform.position.y<weakStart));
UnityEngine.Object.Destroy(weak.gameObject);
yield return new UnityEngine.WaitForSeconds(.1f);
var contactLift=Make("lift",1f,"contact");
player.Teleport(new UnityEngine.Vector3(80,11.2f,0),UnityEngine.Quaternion.identity);
var contactStart=contactLift.transform.position.y;
yield return new UnityEngine.WaitForSeconds(.8f);
results.Add("weak contact lift sinks="+(contactLift.transform.position.y<contactStart));
UnityEngine.Object.Destroy(contactLift.gameObject);
player.Teleport(new UnityEngine.Vector3(7.5f,.05f,0),UnityEngine.Quaternion.identity);
UnityEngine.Object.Destroy(root);
System.IO.File.WriteAllText("Temp/ability-regressions-result.txt",string.Join("; ",results));

}
System.IO.File.WriteAllText("Temp/ability-regressions-result.txt", "running");
CalHacks.RunManager.Instance.StartCoroutine(Verify()); return "verification started";

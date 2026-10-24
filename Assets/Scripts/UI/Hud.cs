using UnityEngine;

namespace CalHacks
{
    /// <summary>Compact demo HUD. The complex developer diagnostics remain behind F3.</summary>
    public class Hud : MonoBehaviour
    {
        GUIStyle small, heading, caption, buildText, winHeading, winBody;
        Texture2D dark, teal, amber, pale;

        public static string FormatTime(float seconds)
        {
            var minutes = (int)(seconds / 60f);
            return $"{minutes}:{seconds - minutes * 60f:00.00}";
        }

        void OnGUI()
        {
            var run = RunManager.Instance;
            if (run == null) return;
            EnsureStyles();
            GUI.depth = -40;
            float sw = Screen.width, sh = Screen.height;
            // HUD is intentionally behind the drawing modal / invention reveal.
            GUI.DrawTexture(new Rect(16, 14, 146, 47), dark);
            GUI.Label(new Rect(28, 15, 120, 24), "THE GREAT GAP", small);
            GUI.Label(new Rect(28, 38, 118, 20), FormatTime(run.State.Elapsed), caption);

            var objectiveW = Mathf.Min(450f, sw - 34f);
            GUI.DrawTexture(new Rect((sw - objectiveW) * .5f, 71, objectiveW, 48), dark);
            GUI.Label(new Rect((sw-objectiveW)*.5f + 12, 76, objectiveW-24, 23),
                "THE PUZZLE IS FIXED. THE SOLUTION ISN'T.", heading);
            GUI.Label(new Rect((sw-objectiveW)*.5f + 12, 97, objectiveW-24, 17),
                "Draw an invention. Help your teammate reach the other side.", caption);

            var state=run.State;
            float barWidth = Mathf.Min(270f, sw - 36f);
            var box = new Rect(sw-barWidth-16f, sh-77f, barWidth, 59f);
            GUI.DrawTexture(box, dark);
            var ready = !state.InFlight && state.CooldownRemaining <= 0f;
            var label = state.InFlight ? "BRINGING SKETCH TO LIFE..." :
                ready ? "READY TO DRAW  ·  P" : $"NEXT BUILD IN {state.CooldownRemaining:0.0}s";
            GUI.Label(new Rect(box.x+12, box.y+7, box.width-24, 22), label, buildText);
            var track = new Rect(box.x+12,box.y+39,box.width-24,7);
            GUI.DrawTexture(track,pale);
            var fill = state.InFlight ? 0.12f : state.CooldownFraction;
            if (ready) fill = 1f;
            GUI.DrawTexture(new Rect(track.x,track.y,track.width*Mathf.Clamp01(fill),track.height), ready?teal:amber);

            if (state.Finished)
            {
                var w=Mathf.Min(560f,sw-36f);
                var rect=new Rect((sw-w)*.5f,sh*.36f,w,174);
                GUI.DrawTexture(rect,dark);
                GUI.Label(new Rect(rect.x+14,rect.y+20,w-28,55),"YOU MADE IT!",winHeading);
                GUI.Label(new Rect(rect.x+14,rect.y+84,w-28,61),
                    $"Solved in {FormatTime(run.FinalTime)}  ·  Press F5 to try another invention", winBody);
            }
        }

        static Texture2D Solid(Color color)
        {
            var t=new Texture2D(1,1,TextureFormat.RGBA32,false);
            t.SetPixel(0,0,color);
            t.Apply();
            return t;
        }

        void EnsureStyles()
        {
            if (small != null) return;
            small=new GUIStyle(GUI.skin.label) { fontSize=12,fontStyle=FontStyle.Bold,
                normal={textColor=new Color(.60f,.95f,.86f)} };
            heading=new GUIStyle(GUI.skin.label) { fontSize=14,fontStyle=FontStyle.Bold,
                alignment=TextAnchor.MiddleCenter,normal={textColor=Color.white} };
            caption=new GUIStyle(GUI.skin.label) { fontSize=11,
                alignment=TextAnchor.MiddleCenter,normal={textColor=new Color(.80f,.85f,.90f)} };
            buildText=new GUIStyle(GUI.skin.label) { fontSize=12,fontStyle=FontStyle.Bold,
                normal={textColor=Color.white} };
            winHeading=new GUIStyle(heading) { fontSize=38 };
            winBody=new GUIStyle(caption) { fontSize=15, wordWrap=true };
            dark=Solid(new Color(.07f,.12f,.19f,.94f));
            teal=Solid(new Color(.21f,.85f,.67f));
            amber=Solid(new Color(.98f,.73f,.34f));
            pale=Solid(new Color(.32f,.38f,.45f));
        }

        void OnDestroy()
        {
            foreach (var t in new[]{dark,teal,amber,pale})
                if (t != null) Destroy(t);
        }
    }
}

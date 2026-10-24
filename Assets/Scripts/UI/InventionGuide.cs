using UnityEngine;

namespace CalHacks
{
    /// <summary>Readable invention reveal after a successful DeerAI interpretation.</summary>
    public sealed class InventionGuide : MonoBehaviour
    {
        static InventionGuide instance;
        string inventionName, description, action, abilities;
        float shownUntil;
        bool visible;
        GUIStyle kickerStyle, nameStyle, descriptionStyle, actionStyle, buttonStyle;
        Texture2D background, buttonBackground, buttonHover, rule;

        public static void Show(InterpretResponse spec)
        {
            if (instance == null || spec == null) return;
            instance.inventionName=(spec.name ?? "invention").ToUpperInvariant();
            instance.description=spec.message ?? "";
            bool powered=false, bounce=false, lift=false;
            var tags=new System.Collections.Generic.List<string>();
            if (spec.components != null)
                foreach (var part in spec.components)
                {
                    if (part == null || part.type == "support") continue;
                    tags.Add(part.type.ToUpperInvariant());
                    powered |= part.activation == "button" || part.activation == "hold" || part.activation == "toggle";
                    bounce |= part.type == "bounce";
                    lift |= part.type == "lift";
                }
            var motion = (spec.motion_behavior ?? "static").Trim().ToUpperInvariant();
            if (motion != "" && motion != "STATIC" && motion != "ROLL_MOVE")
                tags.Add("LIVING: " + motion.Replace('_', ' '));
            instance.abilities = tags.Count > 0 ? string.Join("  +  ",tags) : "SOLID SHAPE";
            instance.action = powered ? "EXPLORER  ·  GET CLOSE + PRESS E  /  SPACE TO JUMP OFF" :
                bounce ? "EXPLORER  ·  LAND ON IT TO BOUNCE" :
                motion == "FOLLOW" ? "EXPLORER  ·  YOUR COMPANION FOLLOWS YOU" :
                motion == "HOP" ? "EXPLORER  ·  WATCH IT HOP AROUND" :
                motion == "FLY" ? "EXPLORER  ·  WATCH IT FLY" :
                motion == "WANDER" ? "EXPLORER  ·  WATCH IT WANDER" :
                "EXPLORER  ·  WALK ON OR JUMP ON IT";
            if (lift && spec.attributes != null &&
                spec.attributes.carry_capacity_kg < InventionPhysics.DemoPlayerMassKg)
                instance.action="EXPLORER  ·  SMALL WINGS MAY NOT CARRY YOUR WEIGHT";
            instance.shownUntil=Time.unscaledTime+10f;
            instance.visible=true;
        }

        public static void Close() { if (instance != null) instance.visible=false; }
        void Awake() { instance=this; }
        void OnDestroy()
        {
            if (instance==this) instance=null;
            foreach (var t in new[]{background,buttonBackground,buttonHover,rule})
                if (t!=null) Destroy(t);
        }

        void OnGUI()
        {
            if (!visible) return;
            if (Time.unscaledTime>shownUntil) {visible=false;return;}
            EnsureStyle();
            GUI.depth=-170;
            float w=Mathf.Min(580f,Screen.width-28f);
            float h=192f;
            var box=new Rect((Screen.width-w)*.5f, Mathf.Max(132,Screen.height*.16f),w,h);
            GUI.DrawTexture(box,background);
            GUI.Label(new Rect(box.x+22,box.y+11,w-44,21),"✦  INVENTION BROUGHT TO LIFE",kickerStyle);
            GUI.Label(new Rect(box.x+22,box.y+35,w-44,33),inventionName,nameStyle);
            GUI.Label(new Rect(box.x+22,box.y+70,w-44,19),abilities,kickerStyle);
            GUI.Label(new Rect(box.x+22,box.y+93,w-44,40),description,descriptionStyle);
            GUI.DrawTexture(new Rect(box.x+20,box.y+137,w-40,1),rule);
            GUI.Label(new Rect(box.x+21,box.y+144,w-155,35),action,actionStyle);
            if (GUI.Button(new Rect(box.x+w-120,box.y+150,98,29),"LET'S PLAY",buttonStyle))
                visible=false;
        }
        static Texture2D Solid(Color c)
        {
            var t=new Texture2D(1,1,TextureFormat.RGBA32,false);
            t.SetPixel(0,0,c);t.Apply();return t;
        }
        void EnsureStyle()
        {
            if (nameStyle != null) return;
            background=Solid(new Color(.07f,.12f,.19f,.97f));
            buttonBackground=Solid(new Color(.2f,.77f,.63f));
            buttonHover=Solid(new Color(.33f,.91f,.76f));
            rule=Solid(new Color(.30f,.40f,.46f));
            kickerStyle=new GUIStyle(GUI.skin.label) {fontSize=12,fontStyle=FontStyle.Bold,
                normal={textColor=new Color(.45f,.94f,.78f)}};
            nameStyle=new GUIStyle(GUI.skin.label){fontSize=25,fontStyle=FontStyle.Bold,
                normal={textColor=Color.white}};
            descriptionStyle=new GUIStyle(GUI.skin.label) {fontSize=14,wordWrap=true,
                normal={textColor=new Color(.90f,.93f,.96f)}};
            actionStyle=new GUIStyle(GUI.skin.label) {fontSize=12,fontStyle=FontStyle.Bold,
                wordWrap=true,normal={textColor=new Color(1f,.83f,.46f)}};
            buttonStyle=new GUIStyle(GUI.skin.button) {fontSize=12,fontStyle=FontStyle.Bold,
                alignment=TextAnchor.MiddleCenter};
            buttonStyle.normal.background=buttonBackground;
            buttonStyle.hover.background=buttonHover;
            buttonStyle.active.background=buttonHover;
            buttonStyle.normal.textColor=new Color(.05f,.12f,.17f);
            buttonStyle.hover.textColor=new Color(.05f,.12f,.17f);
        }
    }
}

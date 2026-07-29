using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AiGameStudio.ArcadeHub.EditorTools
{
    /// <summary>
    /// Generates the themed launch-rain sprites for the six non-LadyBug slots (founder's gate-2 wave:
    /// "по прямоугольникам не понятно, что запускается"). Pure code — each sprite is rasterized from
    /// signed-distance functions into a 512×512 PNG with a transparent background and imported as a UI
    /// sprite, matching the Lady Bug flower set's import settings (that set is owned elsewhere and is
    /// NOT touched here). Clean single-read silhouettes, one uniform soft style, tuned to stay readable
    /// at rain-cell size. Deterministic: re-running overwrites the same six files in Resources/RainSprites.
    /// </summary>
    public static class RainSpriteGenerator
    {
        private const int Size = 512;
        private const string OutDir = "Assets/Resources/RainSprites";

        [MenuItem("Hub/Generate Themed Rain Sprites")]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(OutDir);

            Write("Boulder", DrawBoulder);         // Crank — Endless Sisyphus
            Write("MilkBottle", DrawMilkBottle);   // RedButton — Последняя смена (Factory)
            Write("ChoiceCard", DrawChoiceCard);   // GreenButton — Life Choices
            Write("ArcadeStar", DrawArcadeStar);   // BangButton — Arcade Prototype (soon)
            Write("ZenStones", DrawZenStones);     // Joystick — Медитация (soon)
            Write("CatPaw", DrawCatPaw);           // HeightB — Home Alone

            AssetDatabase.Refresh();
            Debug.Log($"[RainSpriteGenerator] Wrote 6 themed rain sprites to {OutDir}.");
        }

        /// <summary>
        /// Batch screenshot: the launcher posed with the Sisyphus slot (Crank, slot 0) half-charged, so
        /// the boulder rain is visibly piling. Mirrors HubEditorTools' posed-capture flow (that file is
        /// owned by a parallel increment and is not modified here). Writes HUB_SHOT_DIR/hub-themed-rain.png.
        /// </summary>
        [MenuItem("Hub/Capture Themed Rain (Sisyphus)")]
        public static void CaptureThemedRain()
        {
            const int w = 1920, h = 1080;
            string dir = Environment.GetEnvironmentVariable("HUB_SHOT_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
            string path = Path.Combine(dir, "hub-themed-rain.png");

            LauncherConfig config = LauncherConfigLoader.LoadFromStreamingAssets();

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            rt.Create();
            var camGO = new GameObject("CaptureCamera", typeof(Camera));
            var cam = camGO.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.10f, 1f);
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.targetTexture = rt;
            camGO.transform.position = new Vector3(0f, 0f, -10f);

            // (attract screen: the menu no longer draws a list — dark camera clear is the backdrop here;
            // the live scene's backdrop is the attract video, which cannot decode in an edit-mode pose.)
            var htlGO = new GameObject("CaptureHTL", typeof(HoldToLaunchController));
            var htl = htlGO.GetComponent<HoldToLaunchController>();
            htl.EditorPose(config, 0, 0.55f, false); // slot 0 = Crank/Sisyphus — boulders piling
            Retarget(htl.Canvas, cam, 10f);

            Canvas.ForceUpdateCanvases();
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            int magenta = 0;
            Color32[] px32 = tex.GetPixels32();
            for (int i = 0; i < px32.Length; i++)
                if (px32[i].r > 220 && px32[i].g < 40 && px32[i].b > 220) magenta++;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[RainSpriteGenerator] Wrote screenshot '{path}' ({w}x{h}); magentaPixels={magenta}");

            UnityEngine.Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(camGO);
            UnityEngine.Object.DestroyImmediate(htlGO);
        }

        private static void Retarget(Canvas c, Camera cam, float planeDistance)
        {
            if (c == null) return;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
            c.planeDistance = planeDistance;
        }

        // ---------------------------------------------------------------- canvas

        private static void Write(string name, Action<Color[]> draw)
        {
            var px = new Color[Size * Size]; // starts fully transparent
            draw(px);

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            string path = $"{OutDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
        }

        /// <summary>Alpha-blend an SDF layer (negative inside) onto the canvas with ~1.5px AA.</summary>
        private static void Layer(Color[] px, Func<Vector2, float> sdf, Color color)
        {
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // Centered coords, y up.
                    var p = new Vector2(x - Size / 2f + 0.5f, y - Size / 2f + 0.5f);
                    float d = sdf(p);
                    float a = Mathf.Clamp01(0.5f - d / 1.5f) * color.a;
                    if (a <= 0f) continue;
                    int i = y * Size + x;
                    Color dst = px[i];
                    float outA = a + dst.a * (1f - a);
                    if (outA <= 0f) continue;
                    px[i] = new Color(
                        (color.r * a + dst.r * dst.a * (1f - a)) / outA,
                        (color.g * a + dst.g * dst.a * (1f - a)) / outA,
                        (color.b * a + dst.b * dst.a * (1f - a)) / outA,
                        outA);
                }
            }
        }

        // ---------------------------------------------------------------- SDF toolkit

        private static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

        // Scaled-circle ellipse approximation — fine for AA at these sizes.
        private static float Ellipse(Vector2 p, Vector2 c, float rx, float ry)
        {
            Vector2 q = p - c;
            return (new Vector2(q.x / rx, q.y / ry).magnitude - 1f) * Mathf.Min(rx, ry);
        }

        private static float RoundBox(Vector2 p, Vector2 c, float hw, float hh, float r)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - c.x) - (hw - r), Mathf.Abs(p.y - c.y) - (hh - r));
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude
                   + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        // Ring segment between angles a0..a1 (radians, CCW, -pi..pi space), radius R, half-thickness T.
        private static float Arc(Vector2 p, Vector2 c, float a0, float a1, float R, float T)
        {
            Vector2 q = p - c;
            float ang = Mathf.Atan2(q.y, q.x);
            bool inside = a0 <= a1 ? (ang >= a0 && ang <= a1) : (ang >= a0 || ang <= a1);
            if (inside)
                return Mathf.Abs(q.magnitude - R) - T;
            Vector2 e0 = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * R;
            Vector2 e1 = c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * R;
            return Mathf.Min((p - e0).magnitude, (p - e1).magnitude) - T;
        }

        // Classic 5-point star (IQ), r = outer radius, rf = inner/outer ratio factor.
        private static float Star5(Vector2 p, Vector2 c, float r, float rf)
        {
            p -= c;
            var k1 = new Vector2(0.809016994f, -0.587785252f);
            var k2 = new Vector2(-k1.x, k1.y);
            p.x = Mathf.Abs(p.x);
            p -= 2f * Mathf.Max(Vector2.Dot(k1, p), 0f) * k1;
            p -= 2f * Mathf.Max(Vector2.Dot(k2, p), 0f) * k2;
            p.x = Mathf.Abs(p.x);
            p.y -= r;
            Vector2 ba = rf * new Vector2(-k1.y, k1.x) - new Vector2(0f, 1f);
            float h = Mathf.Clamp(Vector2.Dot(p, ba) / Vector2.Dot(ba, ba), 0f, r);
            return (p - ba * h).magnitude * Mathf.Sign(p.y * ba.x - p.x * ba.y);
        }

        private static float Union(float a, float b) => Mathf.Min(a, b);
        private static float Cut(float a, float b) => Mathf.Max(a, -b); // subtract b from a

        // ---------------------------------------------------------------- the six sprites

        // Сизиф: круглый серый валун с парой сколов и трещинкой.
        private static void DrawBoulder(Color[] px)
        {
            var body = new Color(0.62f, 0.63f, 0.66f);
            var shade = new Color(0.48f, 0.49f, 0.53f);
            var crack = new Color(0.38f, 0.39f, 0.43f);

            // Body: big circle, flattened bottom, two edge chips (straight facet bites).
            // Cut(a, b) keeps the half-plane where b > 0 — so pass the SIGNED KEEP-side expression.
            Func<Vector2, float> rock = p =>
            {
                float d = Circle(p, new Vector2(0f, 10f), 195f);
                d = Cut(d, p.y + 165f);                                             // keep above y=-165 (flat bottom)
                d = Cut(d, -Vector2.Dot(p - new Vector2(120f, 135f), new Vector2(0.707f, 0.707f)));   // top-right chip
                d = Cut(d, -Vector2.Dot(p - new Vector2(-160f, 60f), new Vector2(-0.906f, 0.423f)));  // left chip
                return d;
            };
            Layer(px, rock, body);

            // Bottom-left shading crescent (offset copy of the body cut against itself).
            Layer(px, p => Cut(rock(p), Circle(p, new Vector2(55f, 65f), 215f)), shade);

            // Two short cracks.
            Layer(px, p => Capsule(p, new Vector2(35f, 95f), new Vector2(95f, 35f), 7f), crack);
            Layer(px, p => Capsule(p, new Vector2(-85f, -40f), new Vector2(-25f, -95f), 6f), crack);
        }

        // Factory: белая молочная бутылка с голубой крышкой (молочный завод).
        private static void DrawMilkBottle(Color[] px)
        {
            var milk = new Color(0.96f, 0.97f, 0.98f);
            var shade = new Color(0.82f, 0.86f, 0.90f);
            var cap = new Color(0.55f, 0.75f, 0.95f);

            // Silhouette: wide body + narrow neck bridged by shoulder circles.
            Func<Vector2, float> bottle = p =>
            {
                float d = RoundBox(p, new Vector2(0f, -70f), 105f, 125f, 38f);       // body
                d = Union(d, RoundBox(p, new Vector2(0f, 120f), 48f, 65f, 18f));     // neck
                d = Union(d, Circle(p, new Vector2(-55f, 45f), 48f));                // left shoulder
                d = Union(d, Circle(p, new Vector2(55f, 45f), 48f));                 // right shoulder
                return d;
            };
            Layer(px, bottle, milk);

            // Right-side shading sliver for volume (keep where x > 62 — see Cut semantics above).
            Layer(px, p => Cut(bottle(p), p.x - 62f), shade);

            // Cap on top.
            Layer(px, p => RoundBox(p, new Vector2(0f, 185f), 56f, 22f, 10f), cap);
        }

        // Life Choices: игральная карточка со знаком «?».
        private static void DrawChoiceCard(Color[] px)
        {
            var face = new Color(0.97f, 0.96f, 0.93f);
            var border = new Color(0.30f, 0.65f, 0.40f); // the slot's green accent
            var ink = new Color(0.22f, 0.30f, 0.26f);

            Func<Vector2, float> card = p => RoundBox(p, Vector2.zero, 128f, 178f, 26f);
            Layer(px, card, face);
            Layer(px, p => Mathf.Abs(card(p) + 9f) - 9f, border); // thick border band just inside the edge

            // «?»: open ring + stem + dot.
            Layer(px, p => Arc(p, new Vector2(0f, 52f), -2.44f, 1.92f, 52f, 17f), ink); // ring, opening lower-left
            Layer(px, p => Capsule(p, new Vector2(0f, 6f), new Vector2(0f, -32f), 17f), ink);
            Layer(px, p => Circle(p, new Vector2(0f, -88f), 20f), ink);
        }

        // Arcade Prototype: жёлтая звезда платформера.
        private static void DrawArcadeStar(Color[] px)
        {
            var gold = new Color(1.00f, 0.82f, 0.28f);
            var rim = new Color(0.90f, 0.62f, 0.15f);

            Func<Vector2, float> star = p => Star5(p, new Vector2(0f, -10f), 200f, 0.48f);
            Layer(px, star, gold);
            // Lower shading for a hint of volume (cut with an offset copy).
            Layer(px, p => Cut(star(p), Star5(p, new Vector2(-26f, 18f), 200f, 0.48f)), rim);
        }

        // Медитация: стопка из трёх плоских дзен-камушков.
        private static void DrawZenStones(Color[] px)
        {
            var dark = new Color(0.42f, 0.43f, 0.47f);
            var mid = new Color(0.58f, 0.58f, 0.61f);
            var light = new Color(0.76f, 0.74f, 0.71f);

            Layer(px, p => Ellipse(p, new Vector2(0f, -110f), 165f, 62f), dark);   // base stone
            Layer(px, p => Ellipse(p, new Vector2(8f, -5f), 128f, 52f), mid);      // middle stone
            Layer(px, p => Ellipse(p, new Vector2(-6f, 85f), 88f, 42f), light);    // top stone
        }

        // Home Alone: кошачья лапка — большая подушечка + четыре пальца.
        private static void DrawCatPaw(Color[] px)
        {
            var pad = new Color(0.94f, 0.62f, 0.42f); // warm cat-orange

            Layer(px, p => Ellipse(p, new Vector2(0f, -75f), 118f, 96f), pad);     // main pad
            Layer(px, p => Ellipse(p, new Vector2(-125f, 55f), 44f, 56f), pad);    // outer toe L
            Layer(px, p => Ellipse(p, new Vector2(-45f, 115f), 44f, 58f), pad);    // inner toe L
            Layer(px, p => Ellipse(p, new Vector2(45f, 115f), 44f, 58f), pad);     // inner toe R
            Layer(px, p => Ellipse(p, new Vector2(125f, 55f), 44f, 56f), pad);     // outer toe R
        }
    }
}

using System.Collections;
using System.IO;
using UnityEngine;

namespace UmbraShift
{
    /// <summary>
    /// "-trailer": plays a scripted showcase at a locked 30 fps, saves every frame as JPG and
    /// mixes the synthesized audio to WAV in ./trailer. Tools/make_trailer.ps1 turns it into an MP4.
    /// </summary>
    public partial class Game
    {
        bool trailer;
        string capTitle = "", capSub = "";
        float capA, capTarget;
        string cardTitle = "", cardSub = "", cardSmall = "";
        float cardA, cardTarget;
        float cardScale = 1f;
        float camZoom = 1f, camShiftX;   // framing overrides for recorded shots

        void DrawTrailer(float W, float H)
        {
            if (cardA > 0.001f)
            {
                Box(new Rect(0, 0, W, H), new Color(0.02f, 0.015f, 0.05f, cardA * 0.55f));
                var t = new GUIStyle(sTitle) { fontSize = Mathf.RoundToInt(84 * cardScale) };
                var s = new GUIStyle(sSub) { fontSize = 28 };
                var sm = new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
                Shadowed(new Rect(0, H / 2 - 110, W, 130), cardTitle, t, Ink, cardA);
                Shadowed(new Rect(0, H / 2 + 20, W, 44), cardSub, s, Dim, cardA);
                Shadowed(new Rect(0, H / 2 + 110, W, 34), cardSmall, sm, Accent, cardA);
            }
            if (capA > 0.001f)
            {
                var band = new Rect(0, H - 170, W, 170);
                for (int i = 0; i < 17; i++)
                    Box(new Rect(0, band.y + i * 10, W, 10), new Color(0.02f, 0.015f, 0.05f, capA * 0.75f * (i / 17f)));
                var t = new GUIStyle(sTitle) { fontSize = 46 };
                var s = new GUIStyle(sHint) { fontSize = 24 };
                Shadowed(new Rect(0, H - 128, W, 60), capTitle, t, Ink, capA);
                Shadowed(new Rect(0, H - 70, W, 36), capSub, s, Dim, capA);
            }
        }

        void Shadowed(Rect r, string text, GUIStyle st, Color c, float a)
        {
            if (string.IsNullOrEmpty(text)) return;
            var sh = new GUIStyle(st);
            sh.normal.textColor = new Color(0, 0, 0, a * 0.8f);
            GUI.Label(new Rect(r.x + 2, r.y + 3, r.width, r.height), text, sh);
            var fg = new GUIStyle(st);
            fg.normal.textColor = new Color(c.r, c.g, c.b, a);
            GUI.Label(r, text, fg);
        }

        class TrailerDirector : MonoBehaviour
        {
            public Game G;
            const int FPS = 30;
            int frame;
            bool capturing;
            string dir, framesDir;

            void Update()
            {
                float dt = Time.deltaTime;
                G.capA = Mathf.MoveTowards(G.capA, G.capTarget, dt * 3.5f);
                G.cardA = Mathf.MoveTowards(G.cardA, G.cardTarget, dt * 2.2f);
            }

            IEnumerator Capture()
            {
                while (capturing)
                {
                    yield return new WaitForEndOfFrame();
                    if (!capturing) break;
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(framesDir, $"f{frame:D5}.jpg"), tex.EncodeToJPG(94));
                    Destroy(tex);
                    frame++;
                }
            }

            void Caption(string title, string sub = "") { G.capTitle = title; G.capSub = sub; G.capTarget = 1; }
            void NoCaption() => G.capTarget = 0;
            IEnumerator Wait(float s) { yield return new WaitForSeconds(s); }

            IEnumerator FadeTo(float to, float dur)
            {
                float from = G.fade;
                for (float t = 0; t < 1; t += Time.deltaTime / dur) { G.fade = Mathf.Lerp(from, to, t); yield return null; }
                G.fade = to;
            }

            IEnumerator Cut(int level, bool title = false, float outDur = 0.3f, float inDur = 0.45f)
            {
                yield return FadeTo(1, outDur);
                G.Halt();
                G.mode = title ? Mode.Title : Mode.Play;
                G.LoadLevel(level);
                G.fade = 1;
                yield return null;
                yield return FadeTo(0, inDur);
            }

            IEnumerator Moves(string moves, float pause)
            {
                foreach (var m in moves)
                {
                    G.Command(m);
                    yield return null;
                    while (G.busy) yield return null;
                    yield return Wait(pause);
                }
            }

            IEnumerator Start()
            {
                G.trailer = true;
                Time.captureFramerate = FPS;
                dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "trailer"));
                framesDir = Path.Combine(dir, "frames");
                if (Directory.Exists(framesDir)) Directory.Delete(framesDir, true);
                Directory.CreateDirectory(framesDir);
                G.unlocked = G.levels.Count - 1;

                // --- opening: a world slowly turning
                G.Halt();
                G.mode = Mode.Title;
                G.LoadLevel(11);
                G.roll = -20;
                G.fade = 1;
                yield return null;
                G.synth.BeginRecording();
                capturing = true;
                StartCoroutine(Capture());

                StartCoroutine(FadeTo(0, 2f));
                yield return Wait(0.9f);
                G.cardTitle = ""; G.cardSub = "Gravity is a matter of perspective."; G.cardSmall = "";
                G.cardTarget = 1;
                yield return Wait(2.6f);
                G.cardTarget = 0;
                yield return Wait(0.7f);

                // --- 1: turn the world
                yield return Cut(0);
                Caption("TURN THE WORLD", "Walls become floors. Gravity always pulls toward the bottom of your screen.");
                yield return Wait(0.8f);
                yield return Moves("QQQ", 0.35f);
                yield return Wait(0.5f);
                NoCaption();

                // --- 2: shadows are solid
                yield return Cut(1);
                Caption("THE LAMP SHINES FROM THE RIGHT", "Obsidian casts shadows toward the left of your screen...");
                yield return Wait(1.6f);
                yield return Moves("Q", 0.6f);
                Caption("...AND SHADOWS ARE SOLID", "Turn the world and every shadow shifts into a new platform.");
                yield return Wait(0.6f);
                yield return Moves("LLER", 0.3f);
                yield return Wait(0.6f);
                NoCaption();

                // --- 3: danger and rewind
                yield return Cut(4);
                Caption("SHIFT CAREFULLY", "");
                yield return Wait(0.6f);
                yield return Moves("QQ", 0.3f);
                yield return Wait(0.3f);
                yield return Moves("L", 0.2f);
                Caption("SHIFT CAREFULLY", "Void crystals shatter you. Every mistake rewinds.");
                yield return Wait(0.5f);
                yield return Moves("QRREREL", 0.18f);
                yield return Wait(0.5f);
                NoCaption();

                // --- 4: a full solve
                yield return Cut(7);
                Caption("EVERY TURN SHIFTS EVERY SHADOW", "You block the lamp too - your own shadow is not solid.");
                yield return Wait(0.6f);
                yield return Moves("QQLLLEERQLQL", 0.12f);
                yield return Wait(0.5f);
                NoCaption();

                // --- 5: montage
                Caption("12 PUZZLES", "each one verified by a solver - can you match par?");
                yield return Cut(9, false, 0.2f, 0.25f);
                yield return Moves("ERERRL", 0.08f);
                yield return Cut(11, false, 0.2f, 0.25f);
                yield return Moves("QQQQLQQ", 0.08f);
                yield return Cut(8, false, 0.2f, 0.25f);
                yield return Moves("EELLLEEL", 0.08f);
                NoCaption();

                // --- end card
                yield return Cut(11, true, 0.35f, 1.2f);
                G.roll = 0;
                G.cardTitle = "UMBRA SHIFT";
                G.cardSub = "turn the world  -  shift the shade";
                G.cardSmall = "GitHub Game Off 2026   |   play free on itch.io";
                G.cardTarget = 1;
                yield return Wait(5.5f);
                yield return FadeTo(1, 1.5f);
                yield return Wait(0.4f);

                capturing = false;
                yield return null;
                float seconds = frame / (float)FPS;
                G.synth.SaveRecording(Path.Combine(dir, "audio.wav"), seconds, 2.5f);
                File.WriteAllText(Path.Combine(dir, "info.txt"), $"frames={frame}\nfps={FPS}\nseconds={seconds:F2}\n");
                Application.Quit();
            }
        }
    
        /// <summary>"-stills": renders clean, UI-free 2560x1440 shots for thumbnails and store art.</summary>
        class StillsDirector : MonoBehaviour
        {
            public Game G;

            IEnumerator Start()
            {
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "stills"));
                Directory.CreateDirectory(dir);
                G.trailer = true;
                G.unlocked = G.levels.Count - 1;
                var shots = new[]
                {
                    (2, "QL", 16f, "cast"),
                    (10, "ERERRL", -14f, "twin"),
                    (12, "QQQQLQQ", 18f, "umbra"),
                    (8, "QQLLLEE", -16f, "undercroft"),
                };
                foreach (var (lvl, moves, tilt, name) in shots)
                {
                    G.Halt();
                    G.mode = Mode.Play;
                    G.LoadLevel(lvl - 1);
                    G.camZoom = 0.82f; G.camShiftX = 0.17f;
                    yield return null;
                    foreach (var m in moves)
                    {
                        G.Command(m);
                        yield return null;
                        while (G.busy) yield return null;
                    }
                    G.mode = Mode.Play;
                    G.roll += tilt;
                    yield return new WaitForSeconds(1.2f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"), 2);
                    yield return new WaitForSeconds(0.6f);
                }
                Application.Quit();
            }
        }
    }
}

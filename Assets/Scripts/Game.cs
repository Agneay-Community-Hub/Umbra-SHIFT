using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UmbraShift
{
    /// <summary>
    /// Runtime: builds levels from Resources/levels.txt, renders them with primitives,
    /// animates the rules from <see cref="Sim"/>, and draws the UI with IMGUI.
    /// </summary>
    public partial class Game : MonoBehaviour
    {
        enum Mode { Title, Play, Won, End }

        Mode mode = Mode.Title;
        List<LevelData> levels;
        int li;
        LevelData L;
        State st;
        readonly List<State> history = new List<State>();

        Camera cam;
        Light sun;
        Synth synth;
        Transform root, playerT, lampT, goalT;
        Light playerLight, goalLight;
        readonly List<GameObject> sparkObjs = new List<GameObject>();
        readonly List<Transform> hazards = new List<Transform>();
        readonly Dictionary<Vector2Int, Transform> shadowBlocks = new Dictionary<Vector2Int, Transform>();
        readonly Dictionary<Vector2Int, float> shadowTarget = new Dictionary<Vector2Int, float>();
        readonly HashSet<Vector2Int> curShadows = new HashSet<Vector2Int>();

        class Bit { public Transform T; public Vector3 V; public float Life, Max; public Vector3 S0; }
        readonly List<Bit> bits = new List<Bit>();

        Material mCore, mWall, mCaster, mShadow, mHazard, mGoal, mSpark, mPlayer, mBack, mLamp;

        float roll;            // camera roll in degrees; up = (-sin, cos)
        bool busy;
        char buffered;
        float flash; Color flashColor = Color.white;
        float fade;            // 0..1 black overlay
        float shake;
        float lastShadeSound;
        float repeatTimer;
        Vector3 playerSquash = Vector3.one;
        float hintAlpha;
        int unlocked;
        bool showHelp;

        // ------------------------------------------------------------------ setup
        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowDistance = 80;
            QualitySettings.antiAliasing = 4;

            var txt = Resources.Load<TextAsset>("levels");
            levels = LevelData.ParseAll(txt.text);
            unlocked = Mathf.Clamp(PlayerPrefs.GetInt("us_unlocked", 0), 0, levels.Count - 1);

            mWall = Mat("Wall"); mCaster = Mat("Caster"); mShadow = Mat("Shadow"); mHazard = Mat("Hazard");
            mGoal = Mat("Goal"); mSpark = Mat("Spark"); mPlayer = Mat("Player"); mBack = Mat("Back"); mLamp = Mat("Lamp"); mCore = Mat("Core");

            cam = Camera.main;
            if (cam == null) { var cg = new GameObject("Main Camera"); cam = cg.AddComponent<Camera>(); cg.tag = "MainCamera"; cg.AddComponent<AudioListener>(); }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.035f, 0.03f, 0.07f);
            cam.fieldOfView = 32;
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 200;

            sun = FindAnyObjectByType<Light>();
            if (sun == null || sun.type != LightType.Directional) { sun = new GameObject("Lamp Light").AddComponent<Light>(); sun.type = LightType.Directional; }
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.intensity = 1.15f;
            sun.color = new Color(1f, 0.9f, 0.75f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.15f, 0.26f);

            synth = gameObject.AddComponent<Synth>();
            synth.SetMusic(PlayerPrefs.GetInt("us_music", 1) == 1);

            li = unlocked;
            LoadLevel(li);

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-trailer") >= 0)
                new GameObject("Trailer").AddComponent<TrailerDirector>().G = this;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autoshot") >= 0)
                new GameObject("AutoShot").AddComponent<AutoShot>().G = this;
        }

        /// <summary>Test driver: "-autoshot" plays scripted moves and saves screenshots, then quits.</summary>
        class AutoShot : MonoBehaviour
        {
            public Game G;
            IEnumerator Start()
            {
                string dir = System.IO.Path.Combine(Application.dataPath, "..", "shots");
                System.IO.Directory.CreateDirectory(dir);
                yield return new WaitForSeconds(1.5f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "0_title.png"));
                yield return new WaitForSeconds(0.5f);
                var plan = new[] { (2, "QL", "a"), (2, "QLLER", "b"), (9, "ERERR", "c"), (12, "QQQQLQQ", "d"), (4, "QQQRRE", "e") };
                foreach (var (lvl, moves, tag) in plan)
                {
                    G.unlocked = G.levels.Count - 1;
                    G.StartLevel(lvl - 1);
                    yield return new WaitForSeconds(1f);
                    foreach (var m in moves)
                    {
                        G.Command(m);
                        yield return new WaitForSeconds(1.6f);
                    }
                    yield return new WaitForSeconds(1.2f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{lvl}_{tag}.png"));
                    yield return new WaitForSeconds(0.5f);
                }
                Application.Quit();
            }
        }

        static Material Mat(string n)
        {
            var m = Resources.Load<Material>("Materials/" + n);
            if (m == null) { m = new Material(Shader.Find("Standard")); Debug.LogWarning("Missing material " + n); }
            return m;
        }

        static readonly Dictionary<PrimitiveType, Mesh> meshCache = new Dictionary<PrimitiveType, Mesh>();
        static Mesh PrimMesh(PrimitiveType t)
        {
            if (meshCache.TryGetValue(t, out var m) && m) return m;
            string file = t == PrimitiveType.Cube ? "Cube.fbx" : t == PrimitiveType.Sphere ? "New-Sphere.fbx" : "Quad.fbx";
            m = Resources.GetBuiltinResource<Mesh>(file);
            meshCache[t] = m;
            return m;
        }

        GameObject Prim(PrimitiveType t, Material m, Vector3 pos, Vector3 scale, Transform parent, bool castShadow = true)
        {
            // Mesh-only objects: no colliders, so the physics module can be stripped from builds.
            var g = new GameObject(t.ToString());
            g.AddComponent<MeshFilter>().sharedMesh = PrimMesh(t);
            var r = g.AddComponent<MeshRenderer>();
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            r.sharedMaterial = m;
            r.shadowCastingMode = castShadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        void Halt()
        {
            StopAllCoroutines();
            busy = false; buffered = '\0'; fade = 0;
        }

        void LoadLevel(int index)
        {
            busy = false; buffered = '\0';
            li = index;
            L = levels[li];
            if (root != null) Destroy(root.gameObject);
            root = new GameObject("Level").transform;
            shadowBlocks.Clear(); shadowTarget.Clear(); curShadows.Clear();
            sparkObjs.Clear(); hazards.Clear();
            foreach (var b in bits) if (b.T) Destroy(b.T.gameObject);
            bits.Clear();

            // backdrop that receives the lamp's real shadows
            var back = Prim(PrimitiveType.Quad, mBack, new Vector3((L.W - 1) * 0.5f, (L.H - 1) * 0.5f, 0.75f), new Vector3(L.W + 40, L.H + 40, 1), root, false);
            back.name = "Backdrop";

            for (int x = 0; x < L.W; x++)
                for (int y = 0; y < L.H; y++)
                {
                    var c = new Vector2Int(x, y);
                    var p = new Vector3(x, y, 0);
                    switch (L.Grid[x, y])
                    {
                        case '#':
                            Prim(PrimitiveType.Cube, mWall, p, Vector3.one * 0.98f, root, false);
                            break;
                        case 'C':
                            Prim(PrimitiveType.Cube, mCaster, p, Vector3.one * 0.98f, root);
                            Prim(PrimitiveType.Cube, mCore, p + new Vector3(0, 0, -0.47f), new Vector3(0.36f, 0.36f, 0.08f), root, false).transform.localRotation = Quaternion.Euler(0, 0, 45);
                            break;
                        case 'X':
                            var h = Prim(PrimitiveType.Cube, mHazard, p, Vector3.one * 0.5f, root);
                            h.transform.localRotation = Quaternion.Euler(45, 45, 0);
                            hazards.Add(h.transform);
                            break;
                        case 'G':
                            goalT = Prim(PrimitiveType.Cube, mGoal, p, Vector3.one * 0.42f, root, false).transform;
                            var gl = new GameObject("GoalLight").AddComponent<Light>();
                            gl.transform.SetParent(root, false);
                            gl.transform.localPosition = p + new Vector3(0, 0, -0.8f);
                            gl.type = LightType.Point; gl.range = 3.5f; gl.intensity = 2.2f;
                            gl.color = new Color(0.3f, 1f, 0.85f);
                            goalLight = gl;
                            break;
                        case '*':
                            sparkObjs.Add(Prim(PrimitiveType.Sphere, mSpark, p, Vector3.one * 0.36f, root, false));
                            break;
                    }
                }

            // player
            var pg = Prim(PrimitiveType.Sphere, mPlayer, Vector3.zero, Vector3.one * 0.7f, root);
            pg.name = "Player";
            playerT = pg.transform;
            for (int e = -1; e <= 1; e += 2)
            {
                var eye = Prim(PrimitiveType.Sphere, mCaster, new Vector3(0.17f * e, 0.12f, -0.42f), new Vector3(0.12f, 0.17f, 0.08f), playerT, false);
                eye.transform.localScale = new Vector3(0.17f, 0.24f, 0.11f);
            }
            playerLight = new GameObject("PlayerLight").AddComponent<Light>();
            playerLight.transform.SetParent(playerT, false);
            playerLight.transform.localPosition = new Vector3(0, 0, -1.2f);
            playerLight.type = LightType.Point; playerLight.range = 3f; playerLight.intensity = 0.9f;
            playerLight.color = new Color(1f, 0.85f, 0.65f);

            // the lamp: always at the right edge of the screen
            lampT = Prim(PrimitiveType.Sphere, mLamp, Vector3.zero, Vector3.one * 0.9f, root, false).transform;
            lampT.name = "Lamp";

            ResetToStart();
            hintAlpha = 1f;
        }

        void ResetToStart()
        {
            history.Clear();
            var b = Sim.Begin(L);
            st = b.State;
            roll = 0;
            SnapVisuals();
        }

        void SnapVisuals()
        {
            playerT.localPosition = new Vector3(st.Pos.x, st.Pos.y, 0);
            playerT.gameObject.SetActive(true);
            SetShadows(Sim.Shadows(L, st.K, st.Pos), false, true);
            RefreshSparks(st.Mask);
        }

        void RefreshSparks(int mask)
        {
            for (int i = 0; i < sparkObjs.Count; i++) sparkObjs[i].SetActive((mask & (1 << i)) == 0);
        }

        void SetShadows(HashSet<Vector2Int> set, bool sound, bool instant = false)
        {
            bool grew = false;
            foreach (var c in set)
            {
                if (!shadowBlocks.TryGetValue(c, out var t))
                {
                    t = Prim(PrimitiveType.Cube, mShadow, new Vector3(c.x, c.y, 0), Vector3.one * (instant ? 0.98f : 0.15f), root, false).transform;
                    t.GetComponent<Renderer>().receiveShadows = false;
                    shadowBlocks[c] = t;
                    if (!curShadows.Contains(c)) grew = true;
                }
                shadowTarget[c] = 1;
            }
            foreach (var kv in shadowBlocks) if (!set.Contains(kv.Key)) shadowTarget[kv.Key] = 0;
            curShadows.Clear();
            curShadows.UnionWith(set);
            if (grew && sound && Time.time - lastShadeSound > 0.25f) { synth.Play("shade", 0.8f); lastShadeSound = Time.time; }
        }

        // ------------------------------------------------------------------ loop
        void Update()
        {
            float dt = Time.deltaTime;
            HandleInput(dt);

            if (mode == Mode.Title) roll += dt * 9f;

            // camera
            float r = roll * Mathf.Deg2Rad;
            var up = new Vector3(-Mathf.Sin(r), Mathf.Cos(r), 0);
            var right = new Vector3(Mathf.Cos(r), Mathf.Sin(r), 0);
            var center = new Vector3((L.W - 1) * 0.5f, (L.H - 1) * 0.5f, 0);
            float span = Mathf.Max(L.W, L.H) + 2.2f;
            float half = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float dist = Mathf.Max(span / (0.70f * 2 * half), span / (0.92f * 2 * half * Mathf.Max(0.1f, cam.aspect)));
            float visH = 2 * dist * half;
            var shakeOff = shake > 0 ? (Vector3)(Random.insideUnitCircle * shake * 0.25f) : Vector3.zero;
            shake = Mathf.Max(0, shake - dt * 2.5f);
            cam.transform.position = center + new Vector3(0, 0, -dist) + shakeOff - up * (mode == Mode.Title || trailer ? 0 : visH * 48f / 720f);
            cam.transform.rotation = Quaternion.LookRotation(Vector3.forward, up);

            // the lamp hangs to the right of the screen and lights toward screen-left
            float visibleHalfW = dist * half * cam.aspect;
            lampT.position = center + right * Mathf.Min(visibleHalfW - 0.9f, span * 0.5f + 0.4f) + new Vector3(0, 0, -1.5f);
            lampT.localScale = Vector3.one * (0.8f + Mathf.Sin(Time.time * 2.3f) * 0.05f);
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(-right.x, -right.y, 0.9f).normalized, up);

            // shadow blocks
            var dead = new List<Vector2Int>();
            foreach (var kv in shadowBlocks)
            {
                float tgt = shadowTarget[kv.Key];
                var s = kv.Value.localScale.x;
                s = Mathf.MoveTowards(s, tgt * 0.98f, dt * 7f);
                kv.Value.localScale = Vector3.one * s;
                if (tgt == 0 && s <= 0.001f) dead.Add(kv.Key);
            }
            foreach (var c in dead) { Destroy(shadowBlocks[c].gameObject); shadowBlocks.Remove(c); shadowTarget.Remove(c); }

            // ornaments
            foreach (var h in hazards) if (h) h.Rotate(new Vector3(20, 50, 10) * dt, Space.Self);
            if (goalT)
            {
                goalT.Rotate(new Vector3(35, 60, 20) * dt, Space.Self);
                goalT.localScale = Vector3.one * (0.42f + Mathf.Sin(Time.time * 3f) * 0.05f);
                if (goalLight) goalLight.intensity = 2f + Mathf.Sin(Time.time * 3f) * 0.6f;
            }
            for (int i = 0; i < sparkObjs.Count; i++)
            {
                var c = L.Sparks[i];
                sparkObjs[i].transform.localPosition = new Vector3(c.x, c.y, 0) + up * Mathf.Sin(Time.time * 3 + i) * 0.08f;
            }
            playerSquash = Vector3.Lerp(playerSquash, Vector3.one, dt * 10f);
            if (playerT)
            {
                playerT.rotation = Quaternion.LookRotation(Vector3.forward, up);
                playerT.localScale = new Vector3(0.7f * playerSquash.x, 0.7f * playerSquash.y, 0.7f);
            }

            // debris
            var g = -Sim.Up[st.K];
            for (int i = bits.Count - 1; i >= 0; i--)
            {
                var b = bits[i];
                b.Life -= dt;
                if (b.Life <= 0 || b.T == null) { if (b.T) Destroy(b.T.gameObject); bits.RemoveAt(i); continue; }
                b.V += new Vector3(g.x, g.y, 0) * 9f * dt;
                b.T.position += b.V * dt;
                b.T.Rotate(Vector3.one * 200 * dt);
                b.T.localScale = b.S0 * (b.Life / b.Max);
            }

            flash = Mathf.Max(0, flash - dt * 2.5f);
            if (mode == Mode.Play) hintAlpha = Mathf.MoveTowards(hintAlpha, history.Count > 2 ? 0.45f : 1f, dt);
        }

        void HandleInput(float dt)
        {
            bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.KeypadEnter);
            if (Input.GetKeyDown(KeyCode.M)) ToggleMusic();
            switch (mode)
            {
                case Mode.Title:
                    if (enter) StartLevel(li);
                    if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) PreviewLevel(li - 1);
                    if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) PreviewLevel(li + 1);
                    return;
                case Mode.Won:
                    if (enter) NextLevel();
                    if (Input.GetKeyDown(KeyCode.R)) Retry();
                    if (Input.GetKeyDown(KeyCode.Escape)) GoTitle();
                    return;
                case Mode.End:
                    if (enter || Input.GetKeyDown(KeyCode.Escape)) GoTitle();
                    return;
            }

            if (Input.GetKeyDown(KeyCode.Escape)) { GoTitle(); return; }
            if (Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.H)) showHelp = !showHelp;

            char key = '\0';
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) key = 'L';
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) key = 'R';
            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.PageUp)) key = 'Q';
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.PageDown)) key = 'E';
            if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.U)) key = 'U';
            if (Input.GetKeyDown(KeyCode.R)) key = 'X';

            bool holdL = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A);
            bool holdR = Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D);
            if (key != '\0') repeatTimer = 0.28f;
            else if (holdL || holdR)
            {
                repeatTimer -= dt;
                if (repeatTimer <= 0 && !busy) { key = holdL ? 'L' : 'R'; repeatTimer = 0.05f; }
            }

            if (key != '\0') Command(key);
        }

        void Command(char key)
        {
            if (mode != Mode.Play) return;
            if (busy) { if (key != 'X') buffered = key; return; }
            hintAlpha = Mathf.Min(hintAlpha, 1f);
            switch (key)
            {
                case 'U': Undo(); break;
                case 'X': synth.Play("click"); ResetToStartAnimated(); break;
                default: Act(key); break;
            }
        }

        void Act(char mv)
        {
            var r = Sim.Step(L, st, mv);
            if (r == null)
            {
                synth.Play("blocked", 0.8f);
                var dir = Sim.Right(st.K) * (mv == 'R' ? 1 : -1);
                StartCoroutine(Nudge(new Vector3(dir.x, dir.y, 0)));
                return;
            }
            StartCoroutine(Run(r, mv));
        }

        IEnumerator Nudge(Vector3 d)
        {
            busy = true;
            var p0 = playerT.localPosition;
            for (float t = 0; t < 1; t += Time.deltaTime / 0.12f)
            {
                playerT.localPosition = p0 + d * Mathf.Sin(t * Mathf.PI) * 0.12f;
                yield return null;
            }
            playerT.localPosition = p0;
            EndBusy();
        }

        static float Ease(float t) => t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;

        IEnumerator RollTo(float to, float dur)
        {
            float from = roll;
            for (float t = 0; t < 1; t += Time.deltaTime / dur)
            {
                roll = Mathf.Lerp(from, to, Ease(t));
                yield return null;
            }
            roll = to;
        }

        IEnumerator Run(StepResult r, char mv)
        {
            busy = true;
            var prev = st;
            int k = r.State.K;

            if (r.Rotated)
            {
                synth.Play("turn", 0.9f, mv == 'E' ? 1f : 0.92f);
                float from = roll, to = roll + (mv == 'E' ? 90 : -90);
                bool swapped = false;
                for (float t = 0; t < 1; t += Time.deltaTime / 0.36f)
                {
                    roll = Mathf.Lerp(from, to, Ease(t));
                    if (!swapped && t > 0.45f) { SetShadows(Sim.Shadows(L, k, prev.Pos), true); swapped = true; }
                    yield return null;
                }
                roll = to;
                if (!swapped) SetShadows(Sim.Shadows(L, k, prev.Pos), true);
            }

            st = new State(prev.Pos, k, prev.Mask); // so debris/gravity use the new orientation
            int fell = 0;
            int mask = prev.Mask;
            for (int i = 0; i < r.Path.Count; i++)
            {
                var cell = r.Path[i];
                bool walk = i == 0 && !r.Rotated;
                float dur = walk ? 0.1f : Mathf.Max(0.03f, 0.08f - fell * 0.01f);
                if (walk) synth.Play("step", 0.9f, Random.Range(0.95f, 1.08f)); else fell++;
                var a = playerT.localPosition; var b = new Vector3(cell.x, cell.y, 0);
                for (float t = 0; t < 1; t += Time.deltaTime / dur)
                {
                    playerT.localPosition = Vector3.Lerp(a, b, walk ? Mathf.SmoothStep(0, 1, t) : t * t * 0.4f + t * 0.6f);
                    if (walk) playerT.localPosition += (Vector3)(Vector2)Sim.Up[k] * Mathf.Sin(t * Mathf.PI) * 0.12f;
                    yield return null;
                }
                playerT.localPosition = b;
                SetShadows(Sim.Shadows(L, k, cell), true);
                if (L.At(cell) == '*')
                {
                    int si = L.Sparks.IndexOf(cell);
                    if (si >= 0 && (mask & (1 << si)) == 0)
                    {
                        mask |= 1 << si;
                        RefreshSparks(mask);
                        synth.Play("spark");
                        Burst(b, mSpark, 10, 3f);
                    }
                }
            }

            if (r.Status == Status.Ok)
            {
                if (fell > 0)
                {
                    synth.Play("land", Mathf.Clamp01(0.4f + fell * 0.12f));
                    playerSquash = new Vector3(1.25f, 0.75f, 1); // in local (camera-aligned) space
                    if (fell > 3) shake = 0.25f;
                }
                history.Add(prev);
                st = r.State;
                EndBusy();
                yield break;
            }

            if (r.Status == Status.Win)
            {
                history.Add(prev);
                st = r.State;
                synth.Play("win");
                Burst(playerT.localPosition, mGoal, 26, 5f);
                playerT.gameObject.SetActive(false);
                flash = 0.6f; flashColor = new Color(0.5f, 1f, 0.9f);
                SaveWin();
                yield return new WaitForSeconds(0.9f);
                mode = li == levels.Count - 1 ? Mode.End : Mode.Won;
                busy = false; buffered = '\0';
                yield break;
            }

            // death
            if (r.FellOut)
            {
                var gdir = -(Vector3)(Vector2)Sim.Up[k];
                var a = playerT.localPosition;
                for (float t = 0; t < 1; t += Time.deltaTime / 0.45f)
                {
                    playerT.localPosition = a + gdir * (t * t * 8f);
                    yield return null;
                }
            }
            synth.Play("death");
            Burst(playerT.localPosition, mPlayer, 18, 6f);
            Burst(playerT.localPosition, mHazard, 8, 4f);
            playerT.gameObject.SetActive(false);
            flash = 0.7f; flashColor = new Color(1f, 0.25f, 0.45f);
            shake = 0.6f;
            yield return new WaitForSeconds(0.55f);
            // rewind the fatal move
            st = prev;
            if (r.Rotated) yield return RollTo(roll + Mathf.DeltaAngle(roll, prev.K * 90f), 0.25f);
            SnapVisuals();
            playerSquash = new Vector3(0.6f, 1.4f, 1);
            EndBusy();
        }

        void EndBusy()
        {
            busy = false;
            if (buffered != '\0') { var b = buffered; buffered = '\0'; Command(b); }
        }

        void Undo()
        {
            if (history.Count == 0) { synth.Play("blocked", 0.6f); return; }
            st = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            synth.Play("click", 1f, 0.8f);
            StartCoroutine(UndoAnim());
        }

        IEnumerator UndoAnim()
        {
            busy = true;
            float target = roll + Mathf.DeltaAngle(roll, st.K * 90f);
            if (Mathf.Abs(target - roll) > 1f) { synth.Play("turn", 0.5f, 1.2f); yield return RollTo(target, 0.22f); }
            SnapVisuals();
            playerSquash = new Vector3(0.8f, 1.2f, 1);
            EndBusy();
        }

        void ResetToStartAnimated()
        {
            StartCoroutine(RestartAnim());
        }

        IEnumerator RestartAnim()
        {
            busy = true;
            for (float t = 0; t < 1; t += Time.deltaTime / 0.18f) { fade = t; yield return null; }
            ResetToStart();
            for (float t = 1; t > 0; t -= Time.deltaTime / 0.25f) { fade = t; yield return null; }
            fade = 0;
            EndBusy();
        }

        void Burst(Vector3 at, Material m, int n, float speed)
        {
            for (int i = 0; i < n; i++)
            {
                var g = Prim(PrimitiveType.Cube, m, at, Vector3.one * Random.Range(0.08f, 0.2f), root, false);
                var v = Random.insideUnitSphere * speed;
                v.z = -Mathf.Abs(v.z) * 0.5f;
                bits.Add(new Bit { T = g.transform, V = v, Life = Random.Range(0.5f, 1f), Max = 1f, S0 = g.transform.localScale });
            }
        }

        // ------------------------------------------------------------------ flow
        void SaveWin()
        {
            int moves = history.Count;
            string kb = "us_best_" + li, ks = "us_spark_" + li;
            int best = PlayerPrefs.GetInt(kb, 0);
            if (best == 0 || moves < best) PlayerPrefs.SetInt(kb, moves);
            int full = (1 << L.Sparks.Count) - 1;
            if (L.Sparks.Count > 0 && (st.Mask & full) == full) PlayerPrefs.SetInt(ks, 1);
            unlocked = Mathf.Max(unlocked, Mathf.Min(li + 1, levels.Count - 1));
            PlayerPrefs.SetInt("us_unlocked", unlocked);
            if (li == levels.Count - 1) PlayerPrefs.SetInt("us_beaten", 1);
            PlayerPrefs.Save();
        }

        void Retry()
        {
            Halt();
            mode = Mode.Play;
            LoadLevel(li);
        }

        void StartLevel(int i)
        {
            Halt();
            synth.Play("click");
            mode = Mode.Play;
            StartCoroutine(FadeLoad(i));
        }

        IEnumerator FadeLoad(int i)
        {
            for (float t = fade; t < 1; t += Time.deltaTime / 0.25f) { fade = t; yield return null; }
            LoadLevel(i);
            mode = Mode.Play;
            for (float t = 1; t > 0; t -= Time.deltaTime / 0.35f) { fade = t; yield return null; }
            fade = 0;
        }

        void PreviewLevel(int i)
        {
            if (i < 0 || i > unlocked || i >= levels.Count) return;
            Halt();
            synth.Play("click");
            float r = roll;
            LoadLevel(i);
            roll = r;
        }

        void NextLevel()
        {
            if (li + 1 < levels.Count) StartLevel(li + 1);
            else { mode = Mode.End; }
        }

        void GoTitle()
        {
            Halt();
            synth.Play("click");
            mode = Mode.Title;
            LoadLevel(li);
        }

        void ToggleMusic()
        {
            synth.SetMusic(!synth.MusicOn);
            PlayerPrefs.SetInt("us_music", synth.MusicOn ? 1 : 0);
        }

        // ------------------------------------------------------------------ UI
        GUIStyle sTitle, sSub, sBody, sSmall, sBtn, sBig, sCenter, sHint;
        Texture2D white;
        static readonly Color Ink = new Color(0.9f, 0.86f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.58f, 0.78f);
        static readonly Color Accent = new Color(0.45f, 1f, 0.85f);
        static readonly Color Gold = new Color(1f, 0.85f, 0.35f);

        void Styles()
        {
            if (sTitle != null) return;
            white = Texture2D.whiteTexture;
            sTitle = new GUIStyle(GUI.skin.label) { fontSize = 84, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            sTitle.normal.textColor = Ink;
            sSub = new GUIStyle(sTitle) { fontSize = 24, fontStyle = FontStyle.Italic };
            sSub.normal.textColor = Dim;
            sBig = new GUIStyle(sTitle) { fontSize = 52 };
            sBody = new GUIStyle(GUI.skin.label) { fontSize = 22, wordWrap = true };
            sBody.normal.textColor = Ink;
            sSmall = new GUIStyle(sBody) { fontSize = 17 };
            sSmall.normal.textColor = Dim;
            sCenter = new GUIStyle(sBody) { alignment = TextAnchor.MiddleCenter };
            sHint = new GUIStyle(sCenter) { fontSize = 21, fontStyle = FontStyle.Italic };
            sBtn = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            sBtn.normal.textColor = Ink; sBtn.hover.textColor = Accent; sBtn.active.textColor = Gold;
            sBtn.normal.background = Solid(new Color(0.14f, 0.12f, 0.24f, 0.85f));
            sBtn.hover.background = Solid(new Color(0.2f, 0.17f, 0.34f, 0.95f));
            sBtn.active.background = Solid(new Color(0.28f, 0.22f, 0.45f, 1f));
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t;
        }

        void Box(Rect r, Color c)
        {
            var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old;
        }

        void OnGUI()
        {
            Styles();
            float s = Screen.height / 720f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = Screen.width / s, H = 720f;

            if (!trailer)
                switch (mode)
                {
                    case Mode.Title: DrawTitle(W, H); break;
                    case Mode.Play: DrawHud(W, H); break;
                    case Mode.Won: DrawHud(W, H); DrawWon(W, H); break;
                    case Mode.End: DrawEnd(W, H); break;
                }

            if (flash > 0) Box(new Rect(0, 0, W, H), new Color(flashColor.r, flashColor.g, flashColor.b, flash * 0.35f));
            if (fade > 0) Box(new Rect(0, 0, W, H), new Color(0.02f, 0.015f, 0.04f, fade));
            if (trailer) DrawTrailer(W, H);
        }

        void DrawTitle(float W, float H)
        {
            Box(new Rect(0, 0, W, H), new Color(0.02f, 0.015f, 0.05f, 0.55f));
            GUI.Label(new Rect(0, 70, W, 110), "UMBRA SHIFT", sTitle);
            GUI.Label(new Rect(0, 170, W, 40), "turn the world  -  shift the shade", sSub);

            int n = levels.Count;
            int cols = 6;
            float bw = 120, bh = 66, gap = 14;
            float gx = (W - (cols * bw + (cols - 1) * gap)) / 2;
            float gy = 250;
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(gx + (i % cols) * (bw + gap), gy + (i / cols) * (bh + gap), bw, bh);
                bool open = i <= unlocked;
                int best = PlayerPrefs.GetInt("us_best_" + i, 0);
                bool spark = PlayerPrefs.GetInt("us_spark_" + i, 0) == 1;
                string label = open ? (i + 1).ToString() : "-";
                GUI.enabled = open;
                if (i == li) Box(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), Accent * new Color(1, 1, 1, 0.6f));
                if (GUI.Button(r, label, sBtn)) StartLevel(i);
                GUI.enabled = true;
                if (best > 0)
                {
                    var st2 = new GUIStyle(sSmall) { alignment = TextAnchor.LowerCenter, fontSize = 14 };
                    st2.normal.textColor = best <= levels[i].Par ? Gold : Accent;
                    GUI.Label(new Rect(r.x, r.y, r.width, r.height - 4), (best <= levels[i].Par ? "PAR" : "done") + (spark ? "  *" : ""), st2);
                }
            }

            float cy = gy + 2 * (bh + gap) + 30;
            if (GUI.Button(new Rect(W / 2 - 150, cy, 300, 60), "PLAY  " + (li + 1) + "  -  " + levels[li].Title.ToUpper(), sBtn))
                StartLevel(li);
            GUI.Label(new Rect(0, cy + 80, W, 30), "A/D or arrows: walk     Q/E: turn the world     Z: undo     R: restart     M: music     Esc: menu", new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter });
            GUI.Label(new Rect(0, H - 50, W, 30), "Made for GitHub Game Off 2026  -  theme: SHIFT     (left/right on this screen picks a level)", new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter, fontSize = 14 });
        }

        void DrawHud(float W, float H)
        {
            Box(new Rect(0, 0, W, 64), new Color(0.02f, 0.015f, 0.05f, 0.6f));
            var t = new GUIStyle(sBody) { fontSize = 26, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(24, 14, 600, 40), $"{li + 1}/{levels.Count}   {L.Title}", t);
            int moves = history.Count;
            var m = new GUIStyle(sBody) { alignment = TextAnchor.MiddleRight, fontSize = 22 };
            m.normal.textColor = moves <= L.Par ? Ink : Dim;
            string sparks = "";
            if (L.Sparks.Count > 0)
            {
                int got = 0; for (int i = 0; i < L.Sparks.Count; i++) if ((st.Mask & (1 << i)) != 0) got++;
                sparks = $"     spark {got}/{L.Sparks.Count}";
            }
            GUI.Label(new Rect(W - 524, 12, 500, 40), $"moves {moves}   par {L.Par}{sparks}", m);

            // hint
            var hs = new GUIStyle(sHint);
            hs.normal.textColor = new Color(Ink.r, Ink.g, Ink.b, hintAlpha);
            GUI.Label(new Rect(W * 0.1f, H - 150, W * 0.8f, 60), L.Hint, hs);

            // touch / mouse controls
            float bw = 84, bh = 60, gap = 10, y = H - 80;
            string[] labels = { "<", "Q", "E", ">" };
            char[] keys = { 'L', 'Q', 'E', 'R' };
            float total = 4 * bw + 3 * gap;
            float x0 = W / 2 - total / 2;
            for (int i = 0; i < 4; i++)
                if (GUI.Button(new Rect(x0 + i * (bw + gap), y, bw, bh), labels[i], sBtn)) Command(keys[i]);
            if (GUI.Button(new Rect(24, y, 110, bh), "undo", sBtn)) Command('U');
            if (GUI.Button(new Rect(144, y, 110, bh), "restart", sBtn)) Command('X');
            if (GUI.Button(new Rect(W - 134, y, 110, bh), "menu", sBtn)) GoTitle();
            if (GUI.Button(new Rect(W - 254, y, 110, bh), synth.MusicOn ? "music on" : "music off", sBtn)) ToggleMusic();

            if (showHelp || (li == 0 && history.Count == 0))
            {
                var r = new Rect(24, 80, 330, 170);
                Box(r, new Color(0.05f, 0.04f, 0.1f, 0.75f));
                GUI.Label(new Rect(r.x + 14, r.y + 10, r.width - 28, r.height - 20),
                    "A / D  walk\nQ / E  turn the world\nZ  undo     R  restart\n\nGravity pulls to the bottom of the screen. The lamp shines from the right.", sSmall);
            }
        }

        void DrawWon(float W, float H)
        {
            var r = new Rect(W / 2 - 260, H / 2 - 150, 520, 280);
            Box(new Rect(0, 0, W, H), new Color(0.02f, 0.015f, 0.05f, 0.45f));
            Box(r, new Color(0.07f, 0.06f, 0.14f, 0.93f));
            GUI.Label(new Rect(r.x, r.y + 20, r.width, 60), "shifted.", sBig);
            int moves = history.Count;
            string grade = moves <= L.Par ? "par!" : $"par is {L.Par}";
            var c = new GUIStyle(sCenter); c.normal.textColor = moves <= L.Par ? Gold : Ink;
            GUI.Label(new Rect(r.x, r.y + 95, r.width, 34), $"{moves} moves  -  {grade}", c);
            if (L.Sparks.Count > 0)
            {
                bool all = (st.Mask & ((1 << L.Sparks.Count) - 1)) == (1 << L.Sparks.Count) - 1;
                var sc = new GUIStyle(sCenter) { fontSize = 19 }; sc.normal.textColor = all ? Gold : Dim;
                GUI.Label(new Rect(r.x, r.y + 130, r.width, 30), all ? "* spark collected *" : "a spark was left behind...", sc);
            }
            if (GUI.Button(new Rect(r.x + 30, r.y + 190, 140, 60), "retry", sBtn)) Retry();
            if (GUI.Button(new Rect(r.x + 190, r.y + 190, 300, 60), "next  >", sBtn)) NextLevel();
        }

        void DrawEnd(float W, float H)
        {
            Box(new Rect(0, 0, W, H), new Color(0.02f, 0.015f, 0.05f, 0.75f));
            GUI.Label(new Rect(0, 150, W, 100), "every shadow shifted", sBig);
            int sparks = 0, pars = 0, withSparks = 0;
            for (int i = 0; i < levels.Count; i++)
            {
                if (PlayerPrefs.GetInt("us_spark_" + i, 0) == 1) sparks++;
                if (levels[i].Sparks.Count > 0) withSparks++;
                int b = PlayerPrefs.GetInt("us_best_" + i, 0);
                if (b > 0 && b <= levels[i].Par) pars++;
            }
            GUI.Label(new Rect(0, 270, W, 40), $"pars matched: {pars}/{levels.Count}     sparks: {sparks}/{withSparks}", sCenter);
            GUI.Label(new Rect(0, 320, W, 40), "Thanks for playing UMBRA SHIFT.", new GUIStyle(sHint));
            if (GUI.Button(new Rect(W / 2 - 150, 420, 300, 60), "back to menu", sBtn)) GoTitle();
        }
    }
}

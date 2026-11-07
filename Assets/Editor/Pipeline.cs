using System.IO;
using UmbraShift;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Command-line pipeline. Examples:
///   Unity.exe -batchmode -quit -projectPath . -executeMethod Pipeline.Setup
///   Unity.exe -batchmode -quit -projectPath . -executeMethod Pipeline.Validate
///   Unity.exe -batchmode -quit -projectPath . -executeMethod Pipeline.BuildWebGL
///   Unity.exe -batchmode -quit -projectPath . -executeMethod Pipeline.BuildWindows
/// </summary>
public static class Pipeline
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string MatDir = "Assets/Resources/Materials";

    [MenuItem("Umbra Shift/Setup Project")]
    public static void Setup()
    {
        Directory.CreateDirectory(MatDir);
        Directory.CreateDirectory("Assets/Scenes");

        Opaque("Wall", new Color(0.42f, 0.4f, 0.56f), 0.2f, Color.black);
        Opaque("Caster", new Color(0.03f, 0.025f, 0.05f), 0.92f, Color.black);
        Opaque("Core", new Color(0.6f, 0.3f, 1f), 0.5f, new Color(0.9f, 0.35f, 1.6f));
        Opaque("Hazard", new Color(0.9f, 0.15f, 0.35f), 0.7f, new Color(0.9f, 0.1f, 0.3f));
        Opaque("Goal", new Color(0.4f, 1f, 0.85f), 0.6f, new Color(0.3f, 1.4f, 1.1f));
        Opaque("Spark", new Color(1f, 0.85f, 0.35f), 0.6f, new Color(1.6f, 1.2f, 0.4f));
        Opaque("Player", new Color(1f, 0.95f, 0.88f), 0.45f, new Color(0.3f, 0.26f, 0.2f));
        Opaque("Back", new Color(0.075f, 0.065f, 0.12f), 0.0f, Color.black);
        Opaque("Lamp", new Color(1f, 0.8f, 0.4f), 0f, new Color(1.1f, 0.75f, 0.3f));
        Transparent("Shadow", new Color(0.16f, 0.05f, 0.3f, 0.7f), new Color(0.2f, 0.05f, 0.45f));
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.035f, 0.03f, 0.07f);
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = new Vector3(0, 0, -20);

        var lightGo = new GameObject("Lamp Light");
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Directional;
        l.shadows = LightShadows.Soft;

        new GameObject("Game").AddComponent<Game>();

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.16f, 0.15f, 0.26f);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        PlayerSettings.companyName = "Umbra Shift";
        PlayerSettings.productName = "Umbra Shift";
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.SplashScreen.showUnityLogo = false;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.WebGL.template = "APPLICATION:Minimal";
        AssetDatabase.SaveAssets();
        Debug.Log("[Pipeline] Setup complete");
    }

    static Material Get(string name, string shader)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find(shader);
        return m;
    }

    static void Opaque(string name, Color c, float smooth, Color emission)
    {
        var m = Get(name, "Standard");
        m.SetColor("_Color", c);
        m.SetFloat("_Glossiness", smooth);
        m.SetFloat("_Metallic", 0f);
        if (emission.maxColorComponent > 0)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else m.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(m);
    }

    static void Transparent(string name, Color c, Color emission)
    {
        var m = Get(name, "Standard");
        m.SetColor("_Color", c);
        m.SetFloat("_Mode", 3);
        m.SetFloat("_Glossiness", 0.8f);
        m.SetInt("_SrcBlend", (int)BlendMode.One);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHABLEND_ON");
        m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        m.renderQueue = 3000;
        EditorUtility.SetDirty(m);
    }

    [MenuItem("Umbra Shift/Validate Levels")]
    public static void Validate()
    {
        var text = File.ReadAllText("Assets/Resources/levels.txt");
        var levels = LevelData.ParseAll(text);
        bool ok = true;
        for (int i = 0; i < levels.Count; i++)
        {
            var L = levels[i];
            var sol = Sim.Solve(L, false);
            var all = L.Sparks.Count > 0 ? Sim.Solve(L, true) : sol;
            bool good = sol != null && sol.Length == L.Par && all != null;
            ok &= good;
            Debug.Log($"[Validate] {(good ? "OK  " : "FAIL")} {i + 1,2} {L.Title,-14} par={L.Par} solved={sol?.Length} {sol}  sparks={all?.Length}");
        }
        Debug.Log(ok ? "[Validate] ALL LEVELS OK" : "[Validate] PROBLEMS FOUND");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(2);
    }

    public static void BuildWebGL() => Build(BuildTarget.WebGL, "Builds/WebGL");
    public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/UmbraShift.exe");

    static void Build(BuildTarget target, string path)
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = path,
            target = target,
            options = BuildOptions.None
        });
        Debug.Log($"[Pipeline] Build {target}: {report.summary.result}, {report.summary.totalSize / 1024} KB, errors={report.summary.totalErrors}");
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded && Application.isBatchMode)
            EditorApplication.Exit(1);
    }
}

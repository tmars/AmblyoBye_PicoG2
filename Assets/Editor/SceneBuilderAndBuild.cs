using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;
using System.IO;

// Auto-build trigger: если файл /tmp/unity-auto-build-trigger существует,
// запускает CreateSceneAndBuild при загрузке редактора (работает и в GUI, и в batch)
[InitializeOnLoad]
public class AutoBuildOnLoad
{
    private const string TRIGGER_FILE = "/tmp/unity-auto-build-trigger";
    private const string RESULT_FILE  = "/tmp/unity-auto-build-result";

    static AutoBuildOnLoad()
    {
        if (File.Exists(TRIGGER_FILE))
        {
            Debug.Log("[AutoBuild] Trigger file detected. Scheduling build...");
            EditorApplication.delayCall += RunAutoBuild;
        }
    }

    private static void RunAutoBuild()
    {
        if (!File.Exists(TRIGGER_FILE)) return;
        File.Delete(TRIGGER_FILE);

        try
        {
            Debug.Log("[AutoBuild] Starting CreateSceneAndBuild...");
            SceneBuilderAndBuild.CreateSceneAndBuild();
            File.WriteAllText(RESULT_FILE, "SUCCESS");
            Debug.Log("[AutoBuild] Build completed successfully!");
        }
        catch (System.Exception e)
        {
            File.WriteAllText(RESULT_FILE, "FAILED: " + e.Message);
            Debug.LogError("[AutoBuild] Build failed: " + e);
        }
    }
}

public class SceneBuilderAndBuild
{
    private const string SCENE_PATH = "Assets/Scenes/DichopticMovieScene.unity";
    private const string SHADER_PATH = "Assets/Resources/Shaders/DichopticMovieUnlit.shader";
    private const string PVR_SDK_PREFAB = "Assets/PicoMobileSDK/Pvr_UnitySDK/Prefabs/Pvr_UnitySDK.prefab";
    private const string PVR_CONTROLLER_PREFAB = "Assets/PicoMobileSDK/Pvr_Controller/Prefabs/ControllerManager.prefab";

    [MenuItem("Build/1. Create Scene")]
    public static void CreateScene()
    {
        Directory.CreateDirectory("Assets/Scenes");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- Pvr_UnitySDK (VR Camera Rig) ----
        GameObject pvrSDK = null;
        var pvrPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PVR_SDK_PREFAB);
        if (pvrPrefab != null)
        {
            pvrSDK = (GameObject)PrefabUtility.InstantiatePrefab(pvrPrefab);
            pvrSDK.name = "Pvr_UnitySDK";
            pvrSDK.transform.position = new Vector3(0, 1.6f, 0);

            // Set all cameras to solid black background
            foreach (var cam in pvrSDK.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }
        }
        else
        {
            Debug.LogWarning("Pvr_UnitySDK prefab not found at " + PVR_SDK_PREFAB + ". Creating fallback camera.");
            pvrSDK = new GameObject("MainCamera");
            var cam = pvrSDK.AddComponent<Camera>();
            cam.tag = "MainCamera";
        }

        // ---- Controller Manager (child of Pvr_UnitySDK) ----
        var ctrlPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PVR_CONTROLLER_PREFAB);
        if (ctrlPrefab != null)
        {
            var ctrlObj = (GameObject)PrefabUtility.InstantiatePrefab(ctrlPrefab);
            ctrlObj.name = "ControllerManager";
            ctrlObj.transform.SetParent(pvrSDK.transform);
        }
        else
        {
            Debug.LogWarning("ControllerManager prefab not found at " + PVR_CONTROLLER_PREFAB);
        }

        // ---- No light needed — video is self-lit, black background ----
        RenderSettings.ambientLight = Color.black;
        RenderSettings.skybox = null;

        // ---- Dichoptic Material ----
        Shader dichopticShader = Shader.Find("Custom/DichopticMovieUnlit");
        if (dichopticShader == null)
        {
            var shaderAsset = AssetDatabase.LoadAssetAtPath<Shader>(SHADER_PATH);
            dichopticShader = shaderAsset != null ? shaderAsset : Shader.Find("Unlit/Texture");
        }
        Material dichopticMat = new Material(dichopticShader);
        dichopticMat.name = "DichopticFilterMaterial";
        Directory.CreateDirectory("Assets/Resources/Materials");
        AssetDatabase.CreateAsset(dichopticMat, "Assets/Resources/Materials/DichopticFilterMaterial.mat");

        // ---- MoviePlayer object (Quad + VideoPlayer + AudioSource) ----
        var moviePlayer = GameObject.CreatePrimitive(PrimitiveType.Quad);
        moviePlayer.name = "MoviePlayer";
        moviePlayer.transform.position = new Vector3(0, 1.6f, 2f);
        moviePlayer.transform.localScale = new Vector3(3.2f, 1.8f, 1f);

        var meshRenderer = moviePlayer.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = dichopticMat;

        var videoPlayer = moviePlayer.AddComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false;
        videoPlayer.renderMode = VideoRenderMode.MaterialOverride;
        videoPlayer.targetMaterialRenderer = meshRenderer;
        videoPlayer.targetMaterialProperty = "_MainTex";
        videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        var audioSource = moviePlayer.AddComponent<AudioSource>();
        videoPlayer.SetTargetAudioSource(0, audioSource);

        // ---- EventSystem (use existing from Pvr_UnitySDK prefab, add Gaze + Touchpad) ----
        // Pvr_UnitySDK prefab already contains an EventSystem — find it, don't create a duplicate
        var existingES = Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
        GameObject eventSystemObj;
        if (existingES != null)
        {
            eventSystemObj = existingES.gameObject;
            Debug.Log("Using existing EventSystem from Pvr_UnitySDK prefab");
            // Remove any StandaloneInputModule that might conflict
            var standalone = eventSystemObj.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            if (standalone != null) Object.DestroyImmediate(standalone);
        }
        else
        {
            eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
        }
        var gazeInput = eventSystemObj.AddComponent<Pvr_GazeInputModule>();
        gazeInput.mode = Pvr_GazeInputModule.Mode.Click;
        gazeInput.GazeTimeInSeconds = 99f;
        eventSystemObj.AddComponent<PicoTouchpadClick>();

        // ---- Settings UI Canvas (on top of movie screen) ----
        var canvasObj = new GameObject("SettingsCanvas");
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        var canvasRT = canvasObj.GetComponent<RectTransform>();
        canvasRT.position = new Vector3(0, 1.6f, 1.95f);
        canvasRT.sizeDelta = new Vector2(800, 700);
        canvasRT.localScale = new Vector3(0.002f, 0.002f, 0.002f);

        // VR UI setup (finds camera, sets up eye index for dichoptic rendering)
        var vrUISetup = canvasObj.AddComponent<VRUISetup>();
        vrUISetup.targetCanvas = canvas;

        // Save reticle material as asset so shader is included in build
        Directory.CreateDirectory("Assets/Resources/Materials");
        Shader reticleShader = Shader.Find("UI/Default");
        if (reticleShader != null)
        {
            var reticleMat = new Material(reticleShader);
            reticleMat.color = Color.white;
            AssetDatabase.CreateAsset(reticleMat, "Assets/Resources/Materials/ReticleMaterial.mat");
        }

        // Background panel
        var panel = CreateUIElement<Image>("Panel", canvasObj.transform);
        panel.color = new Color(0.02f, 0.02f, 0.02f, 0.98f);
        var panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;

        // ---- Version text ----
        var versionText = CreateTMPText("VersionText", panel.transform, "v0.1", 16,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -10), new Vector2(200, 30));

        // ---- Total played time text ----
        var playedTimeText = CreateTMPText("TotalPlayedTimeText", panel.transform, "Watched: 0h 00 min", 18,
            new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(300, 30));

        // ---- Movie Dropdown (hidden, used internally for delete/backward compat) ----
        var dropdownObj = CreateTMPDropdown("MovieDropdown", panel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 200), new Vector2(400, 40));
        dropdownObj.SetActive(false);

        // ---- Select Video Button (opens picker panel) ----
        var selectVideoBtn = CreateButton("SelectVideoButton", "Select Video", panel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 200), new Vector2(300, 45));
        selectVideoBtn.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.55f, 1f);

        // ---- Eye Bias Slider ----
        var eyeBiasSlider = CreateSliderWithLabel("EyeBiasSlider", "Eye Bias", panel.transform,
            new Vector2(0, 130), 0f, 1f, 0.5f, 0.05f, "0.00");

        // ---- Blob Scale Slider ----
        var blobScaleSlider = CreateSliderWithLabel("BlobScaleSlider", "Blob Scale", panel.transform,
            new Vector2(0, 90), 0.1f, 10f, 1f, 0.5f, "0.0");

        // ---- Blob Gray Color Slider ----
        var blobGraySlider = CreateSliderWithLabel("BlobGraySlider", "Blob Gray", panel.transform,
            new Vector2(0, 50), 0f, 255f, 70f, 15f, "0");

        // ---- Blob Timer Slider ----
        var blobTimerSlider = CreateSliderWithLabel("BlobTimerSlider", "Blob Timer", panel.transform,
            new Vector2(0, 10), 1f, 30f, 5f, 1f, "0.0");

        // ---- Reset Settings Button ----
        var resetBtn = CreateButton("ResetSettingsButton", "Reset Settings", panel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(200, 40));

        // ---- Stats Graph placeholder (disabled by default, StatsGraph script will handle) ----
        var statsObj = new GameObject("StatsPanel");
        statsObj.transform.SetParent(panel.transform, false);
        statsObj.SetActive(false);

        // ---- Confirmation Dialog ----
        var confirmDialog = CreateConfirmationDialog(panel.transform);

        // ---- SceneManager object ----
        var sceneManagerObj = new GameObject("SceneManager");
        var sceneManager = sceneManagerObj.AddComponent<DichopticMovieSceneManager>();

        sceneManager.moviePlayerObject = moviePlayer;
        sceneManager.videoPlayer = videoPlayer;
        sceneManager.dichopticFilterMaterial = dichopticMat;
        sceneManager.movieListDropdown = dropdownObj.GetComponent<TMP_Dropdown>();
        sceneManager.eyeBiasSlider = eyeBiasSlider;
        sceneManager.blobScaleSlider = blobScaleSlider;
        sceneManager.blobGrayColorSlider = blobGraySlider;
        sceneManager.blobTimerSlider = blobTimerSlider;
        sceneManager.settingsUI = canvasObj;
        sceneManager.versionTextBox = versionText;
        sceneManager.totalPlayedTimeTextBox = playedTimeText;
        sceneManager.confirmationDialog = confirmDialog.GetComponent<ConfirmationDialog>();

        // Video time + pause button row
        var pauseBtn = CreateButton("PausePlayBtn", "Pause / Play", panel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-120, -80), new Vector2(160, 32));
        pauseBtn.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.5f, 1f);
        var pauseBtnComp = pauseBtn.GetComponent<Button>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            pauseBtnComp.onClick,
            new UnityEngine.Events.UnityAction(sceneManager.TogglePause));

        var videoTimeTextObj = CreateTMPText("VideoTimeText", panel.transform, "--:-- / --:--", 20,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(80, -80), new Vector2(300, 30));
        videoTimeTextObj.alignment = TextAlignmentOptions.Center;
        sceneManager.videoTimeText = videoTimeTextObj;

        // Select Video button → opens picker
        var selectVideoBtnComp = selectVideoBtn.GetComponent<Button>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            selectVideoBtnComp.onClick,
            new UnityEngine.Events.UnityAction(sceneManager.ShowVideoPicker));

        // Reset settings button
        var resetBtnComp = resetBtn.GetComponent<Button>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            resetBtnComp.onClick,
            new UnityEngine.Events.UnityAction(sceneManager.ResetSettingsButtonHandler));

        // Slider onValueChanged — wrapped in try-catch so failures don't abort scene creation
        try { WireSliderOnChanged(eyeBiasSlider, sceneManager, "ChangeEyeBiasValue"); }
        catch (System.Exception e) { Debug.LogWarning("WireSlider EyeBias: " + e.Message); }
        try { WireSliderOnChanged(blobScaleSlider, sceneManager, "ChangeBlobScale"); }
        catch (System.Exception e) { Debug.LogWarning("WireSlider BlobScale: " + e.Message); }
        try { WireSliderOnChanged(blobGraySlider, sceneManager, "ChangeBlobGreyValue"); }
        catch (System.Exception e) { Debug.LogWarning("WireSlider BlobGray: " + e.Message); }
        try { WireSliderOnChanged(blobTimerSlider, sceneManager, "ChangeBlobTimerValue"); }
        catch (System.Exception e) { Debug.LogWarning("WireSlider BlobTimer: " + e.Message); }

        // ---- Seek Rewind Row ----
        float seekY = -115f;
        string[] rewindLabels = { "-10m", "-5m", "-1m", "-10s", "-5s" };
        string[] rewindMethods = { "SeekBack10m", "SeekBack5m", "SeekBack1m", "SeekBack10s", "SeekBack5s" };
        for (int i = 0; i < rewindLabels.Length; i++)
        {
            float x = -220 + i * 90;
            var seekBtn = CreateButton("Seek" + rewindLabels[i], rewindLabels[i], panel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, seekY), new Vector2(80, 32));
            seekBtn.GetComponent<Image>().color = new Color(0.4f, 0.25f, 0.25f, 1f);
            var seekBtnComp = seekBtn.GetComponent<Button>();
            var seekMethod = typeof(DichopticMovieSceneManager).GetMethod(rewindMethods[i]);
            if (seekMethod != null)
            {
                var action = (UnityEngine.Events.UnityAction)System.Delegate.CreateDelegate(
                    typeof(UnityEngine.Events.UnityAction), sceneManager, seekMethod);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(seekBtnComp.onClick, action);
            }
        }

        // ---- Seek Forward Row ----
        float seekY2 = -155f;
        string[] fwdLabels = { "+5s", "+10s", "+1m", "+5m", "+10m" };
        string[] fwdMethods = { "SeekFwd5s", "SeekFwd10s", "SeekFwd1m", "SeekFwd5m", "SeekFwd10m" };
        for (int i = 0; i < fwdLabels.Length; i++)
        {
            float x = -220 + i * 90;
            var seekBtn = CreateButton("Seek" + fwdLabels[i], fwdLabels[i], panel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, seekY2), new Vector2(80, 32));
            seekBtn.GetComponent<Image>().color = new Color(0.25f, 0.4f, 0.25f, 1f);
            var seekBtnComp = seekBtn.GetComponent<Button>();
            var seekMethod = typeof(DichopticMovieSceneManager).GetMethod(fwdMethods[i]);
            if (seekMethod != null)
            {
                var action = (UnityEngine.Events.UnityAction)System.Delegate.CreateDelegate(
                    typeof(UnityEngine.Events.UnityAction), sceneManager, seekMethod);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(seekBtnComp.onClick, action);
            }
        }

        // ---- Screen Distance Controls ----
        float distY = -200f;
        var distLabel = CreateTMPText("DistLabel", panel.transform, "Screen", 16,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-120, distY), new Vector2(100, 35));
        distLabel.alignment = TextAlignmentOptions.MidlineRight;

        var distCloserBtn = CreateButton("ScreenCloser", "Closer", panel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-40, distY), new Vector2(90, 35));
        distCloserBtn.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.5f, 1f);
        var distCloserComp = distCloserBtn.GetComponent<Button>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            distCloserComp.onClick,
            new UnityEngine.Events.UnityAction(sceneManager.ScreenCloser));

        var distText = CreateTMPText("DistanceText", panel.transform, "2.00m", 18,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(50, distY), new Vector2(100, 35));
        distText.alignment = TextAlignmentOptions.Center;
        sceneManager.distanceText = distText;

        var distFartherBtn = CreateButton("ScreenFarther", "Farther", panel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(140, distY), new Vector2(90, 35));
        distFartherBtn.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.5f, 1f);
        var distFartherComp = distFartherBtn.GetComponent<Button>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            distFartherComp.onClick,
            new UnityEngine.Events.UnityAction(sceneManager.ScreenFarther));

        // ---- Screen Tilt Controls ----
        float tiltY = -240f;
        var tiltLabel = CreateTMPText("TiltLabel", panel.transform, "Tilt", 16,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-120, tiltY), new Vector2(100, 35));
        tiltLabel.alignment = TextAlignmentOptions.MidlineRight;

        var tiltDownBtn = CreateButton("ScreenTiltDown", "Down", panel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-40, tiltY), new Vector2(90, 35));
        tiltDownBtn.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.5f, 1f);
        var tiltDownComp = tiltDownBtn.GetComponent<Button>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            tiltDownComp.onClick,
            new UnityEngine.Events.UnityAction(sceneManager.ScreenTiltDown));

        var tiltTextObj = CreateTMPText("TiltText", panel.transform, "0°", 18,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(50, tiltY), new Vector2(100, 35));
        tiltTextObj.alignment = TextAlignmentOptions.Center;
        sceneManager.tiltText = tiltTextObj;

        var tiltUpBtn = CreateButton("ScreenTiltUp", "Up", panel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(140, tiltY), new Vector2(90, 35));
        tiltUpBtn.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.5f, 1f);
        var tiltUpComp = tiltUpBtn.GetComponent<Button>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            tiltUpComp.onClick,
            new UnityEngine.Events.UnityAction(sceneManager.ScreenTiltUp));

        // ---- Send Stats Button ----
        float sendStatsY = -285f;
        var sendStatsBtn = CreateButton("SendStatsButton", "Send Stats to TG", panel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, sendStatsY), new Vector2(220, 35));
        sendStatsBtn.GetComponent<Image>().color = new Color(0.2f, 0.4f, 0.55f, 1f);
        var sendStatsBtnComp = sendStatsBtn.GetComponent<Button>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            sendStatsBtnComp.onClick,
            new UnityEngine.Events.UnityAction(sceneManager.SendStatsToTelegram));

        // ---- Video Picker Panel (создаётся ПОСЛЕДНИМ — рендерится поверх всех кнопок) ----
        CreateVideoPickerPanel(panel.transform, sceneManager);

        // ---- Save scene ----
        EditorSceneManager.SaveScene(scene, SCENE_PATH);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Update build settings
        EditorBuildSettings.scenes = new EditorBuildSettingsScene[]
        {
            new EditorBuildSettingsScene(SCENE_PATH, true)
        };

        Debug.Log("SCENE CREATED: " + SCENE_PATH);
    }

    [MenuItem("Build/0. Fix Android Tools (run first!)")]
    public static void FixAndroidTools()
    {
        string systemSdk = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) + "/Library/Android/sdk";
        string systemNdk = "/Users/marseltalipov/Documents/tamerlan/android-ndk-r21d";

        // Set via EditorPrefs (works in Unity 2021.3 batchmode and GUI)
        EditorPrefs.SetBool("JdkUseEmbedded", true);

        if (System.IO.Directory.Exists(systemSdk))
        {
            EditorPrefs.SetBool("SdkUseEmbedded", false);
            EditorPrefs.SetString("AndroidSdkRoot", systemSdk);
            Debug.Log("Using Android SDK: " + systemSdk);
        }

        if (System.IO.Directory.Exists(systemNdk))
        {
            EditorPrefs.SetBool("NdkUseEmbedded", false);
            EditorPrefs.SetString("AndroidNdkRootR21d", systemNdk);
            EditorPrefs.SetString("AndroidNdkRoot", systemNdk);
            Debug.Log("Using Android NDK: " + systemNdk);
        }

        // Also set via Unity Preferences API directly
        EditorPrefs.SetString("AndroidNdkRootR21", systemNdk);

        Debug.Log("FixAndroidTools complete.");
        Debug.Log("NDK EditorPrefs check: " + EditorPrefs.GetString("AndroidNdkRootR21d", "NOT SET"));
    }

    [MenuItem("Build/2. Build Android APK")]
    public static void BuildAndroid()
    {
        // Always fix tools paths before building
        FixAndroidTools();

        // Pico G2 Player Settings
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.amblyobye.amblyobye");
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)33;

        bool hasNdk = Directory.Exists("/Users/marseltalipov/Documents/tamerlan/android-ndk-r21d");
        if (hasNdk)
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            Debug.Log("Using IL2CPP + ARM64");
        }
        else
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.Mono2x);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7;
            Debug.Log("NDK not found. Falling back to Mono + ARMv7");
        }

        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
            new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.MTRendering = false;
        QualitySettings.vSyncCount = 0;

        // Ensure build directory
        Directory.CreateDirectory("Builds");

        string[] scenes = new string[] { SCENE_PATH };
        string outputPath = "Builds/AmblyoBye_PicoG2.apk";

        BuildPlayerOptions buildOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(buildOptions);

        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log("BUILD SUCCEEDED: " + System.IO.Path.GetFullPath(outputPath));
        }
        else
        {
            string errorMsg = "BUILD FAILED: " + report.summary.result;
            Debug.LogError(errorMsg);
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error || msg.type == LogType.Warning)
                        Debug.LogError(msg.content);
                }
            }
            throw new System.Exception(errorMsg);
        }
    }

    [MenuItem("Build/3. Create Scene + Build APK")]
    public static void CreateSceneAndBuild()
    {
        CreateScene();
        BuildAndroid();
    }

    // ==========================
    // UI Helper Methods
    // ==========================

    static T CreateUIElement<T>(string name, Transform parent) where T : Component
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<T>();
    }

    static TextMeshProUGUI CreateTMPText(string name, Transform parent, string text, float fontSize,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = Color.white;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        return tmp;
    }

    static GameObject CreateTMPDropdown(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;

        // Background image
        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);

        // Label
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(go.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = "";
        label.fontSize = 16;
        label.color = Color.white;
        var labelRT = labelGo.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = new Vector2(10, 0);
        labelRT.offsetMax = new Vector2(-30, 0);

        // Template (required for TMP_Dropdown)
        var template = new GameObject("Template");
        template.transform.SetParent(go.transform, false);
        var templateRT = template.AddComponent<RectTransform>();
        templateRT.anchorMin = new Vector2(0, 0);
        templateRT.anchorMax = new Vector2(1, 0);
        templateRT.pivot = new Vector2(0.5f, 1f);
        templateRT.anchoredPosition = Vector2.zero;
        templateRT.sizeDelta = new Vector2(0, 150);
        var scrollRect = template.AddComponent<ScrollRect>();
        template.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);

        // Viewport
        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(template.transform, false);
        var vpRT = viewport.AddComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = Vector2.zero;
        vpRT.offsetMax = Vector2.zero;
        viewport.AddComponent<Image>().color = Color.white;
        var mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        scrollRect.viewport = vpRT;

        // Content
        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRT = content.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.sizeDelta = new Vector2(0, 28);
        scrollRect.content = contentRT;

        // Item
        var item = new GameObject("Item");
        item.transform.SetParent(content.transform, false);
        var itemRT = item.AddComponent<RectTransform>();
        itemRT.anchorMin = new Vector2(0, 0.5f);
        itemRT.anchorMax = new Vector2(1, 0.5f);
        itemRT.sizeDelta = new Vector2(0, 28);
        var itemToggle = item.AddComponent<Toggle>();

        // Item background
        var itemBg = new GameObject("Item Background");
        itemBg.transform.SetParent(item.transform, false);
        var itemBgRT = itemBg.AddComponent<RectTransform>();
        itemBgRT.anchorMin = Vector2.zero;
        itemBgRT.anchorMax = Vector2.one;
        itemBgRT.offsetMin = Vector2.zero;
        itemBgRT.offsetMax = Vector2.zero;
        itemBg.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);

        // Item label
        var itemLabel = new GameObject("Item Label");
        itemLabel.transform.SetParent(item.transform, false);
        var itemLabelTMP = itemLabel.AddComponent<TextMeshProUGUI>();
        itemLabelTMP.text = "";
        itemLabelTMP.fontSize = 14;
        itemLabelTMP.color = Color.white;
        var itemLabelRT = itemLabel.GetComponent<RectTransform>();
        itemLabelRT.anchorMin = Vector2.zero;
        itemLabelRT.anchorMax = Vector2.one;
        itemLabelRT.offsetMin = new Vector2(10, 0);
        itemLabelRT.offsetMax = new Vector2(-10, 0);

        template.SetActive(false);

        // Add TMP_Dropdown
        var dropdown = go.AddComponent<TMP_Dropdown>();
        dropdown.captionText = label;
        dropdown.template = templateRT;
        dropdown.itemText = itemLabelTMP;

        return go;
    }

    static GameObject CreateButton(string name, string text, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.3f, 0.3f, 0.6f, 1f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 16;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        var textRT = textGo.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        return go;
    }

    static GameObject CreateSliderWithLabel(string name, string label, Transform parent,
        Vector2 offset, float min, float max, float defaultVal, float step = 0.05f, string format = "0.00")
    {
        var container = new GameObject(name);
        container.transform.SetParent(parent, false);
        var containerRT = container.AddComponent<RectTransform>();
        containerRT.anchorMin = new Vector2(0.5f, 0.5f);
        containerRT.anchorMax = new Vector2(0.5f, 0.5f);
        containerRT.anchoredPosition = offset;
        containerRT.sizeDelta = new Vector2(600, 40);

        // Hidden Slider component for value storage + onValueChanged compatibility
        var containerSlider = container.AddComponent<Slider>();
        containerSlider.minValue = min;
        containerSlider.maxValue = max;
        containerSlider.value = defaultVal;

        // Label (left)
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(container.transform, false);
        var labelTMP = labelGo.AddComponent<TextMeshProUGUI>();
        labelTMP.text = label;
        labelTMP.fontSize = 16;
        labelTMP.color = Color.white;
        labelTMP.alignment = TextAlignmentOptions.MidlineLeft;
        var labelRT = labelGo.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0, 0);
        labelRT.anchorMax = new Vector2(0.35f, 1);
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        // [-] button
        var minusBtn = CreateButton("MinusBtn", "  -  ", container.transform,
            new Vector2(0.35f, 0), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
        var minusBtnRT = minusBtn.GetComponent<RectTransform>();
        minusBtnRT.anchorMin = new Vector2(0.35f, 0);
        minusBtnRT.anchorMax = new Vector2(0.5f, 1);
        minusBtnRT.anchoredPosition = Vector2.zero;
        minusBtnRT.offsetMin = new Vector2(2, 2);
        minusBtnRT.offsetMax = new Vector2(-2, -2);
        minusBtnRT.sizeDelta = Vector2.zero;
        minusBtn.GetComponent<Image>().color = new Color(0.5f, 0.2f, 0.2f, 1f);

        // Value text (center)
        var valueGo = new GameObject("ValueText");
        valueGo.transform.SetParent(container.transform, false);
        var valueTMP = valueGo.AddComponent<TextMeshProUGUI>();
        valueTMP.text = defaultVal.ToString(format);
        valueTMP.fontSize = 16;
        valueTMP.color = Color.white;
        valueTMP.alignment = TextAlignmentOptions.Center;
        var valueRT = valueGo.GetComponent<RectTransform>();
        valueRT.anchorMin = new Vector2(0.5f, 0);
        valueRT.anchorMax = new Vector2(0.7f, 1);
        valueRT.offsetMin = Vector2.zero;
        valueRT.offsetMax = Vector2.zero;

        // [+] button
        var plusBtn = CreateButton("PlusBtn", "  +  ", container.transform,
            new Vector2(0.7f, 0), new Vector2(0.85f, 1), Vector2.zero, Vector2.zero);
        var plusBtnRT = plusBtn.GetComponent<RectTransform>();
        plusBtnRT.anchorMin = new Vector2(0.7f, 0);
        plusBtnRT.anchorMax = new Vector2(0.85f, 1);
        plusBtnRT.anchoredPosition = Vector2.zero;
        plusBtnRT.offsetMin = new Vector2(2, 2);
        plusBtnRT.offsetMax = new Vector2(-2, -2);
        plusBtnRT.sizeDelta = Vector2.zero;
        plusBtn.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.2f, 1f);

        // SliderPlusMinus controller
        var spm = container.AddComponent<SliderPlusMinus>();
        spm.slider = containerSlider;
        spm.step = step;
        spm.valueText = valueTMP;
        spm.format = format;

        // Wire buttons
        var minusBtnComp = minusBtn.GetComponent<Button>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            minusBtnComp.onClick,
            new UnityEngine.Events.UnityAction(spm.Decrement));

        var plusBtnComp = plusBtn.GetComponent<Button>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            plusBtnComp.onClick,
            new UnityEngine.Events.UnityAction(spm.Increment));

        return container;
    }

    static GameObject CreateConfirmationDialog(Transform parent)
    {
        var dialog = new GameObject("ConfirmationDialog");
        dialog.transform.SetParent(parent, false);
        var dialogRT = dialog.AddComponent<RectTransform>();
        dialogRT.anchorMin = new Vector2(0.2f, 0.3f);
        dialogRT.anchorMax = new Vector2(0.8f, 0.7f);
        dialogRT.offsetMin = Vector2.zero;
        dialogRT.offsetMax = Vector2.zero;

        var bg = dialog.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

        // Message
        var msgGo = new GameObject("MessageText");
        msgGo.transform.SetParent(dialog.transform, false);
        var msgTMP = msgGo.AddComponent<TextMeshProUGUI>();
        msgTMP.text = "Are you sure?";
        msgTMP.fontSize = 18;
        msgTMP.color = Color.white;
        msgTMP.alignment = TextAlignmentOptions.Center;
        var msgRT = msgGo.GetComponent<RectTransform>();
        msgRT.anchorMin = new Vector2(0.1f, 0.5f);
        msgRT.anchorMax = new Vector2(0.9f, 0.9f);
        msgRT.offsetMin = Vector2.zero;
        msgRT.offsetMax = Vector2.zero;

        // Yes button
        var yesBtn = CreateButton("YesButton", "Yes", dialog.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-60, -40), new Vector2(100, 35));
        yesBtn.GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.2f, 1f);

        // No button
        var noBtn = CreateButton("NoButton", "No", dialog.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(60, -40), new Vector2(100, 35));
        noBtn.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f);

        // Add ConfirmationDialog component
        var confirmComp = dialog.AddComponent<ConfirmationDialog>();

        // Wire references via SerializedObject
        var so = new SerializedObject(confirmComp);
        so.FindProperty("messageText").objectReferenceValue = msgTMP;
        so.FindProperty("yesButton").objectReferenceValue = yesBtn.GetComponent<Button>();
        so.FindProperty("noButton").objectReferenceValue = noBtn.GetComponent<Button>();
        so.ApplyModifiedProperties();

        dialog.SetActive(false);

        return dialog;
    }

    static void WireSliderOnChanged(GameObject sliderObj, DichopticMovieSceneManager target, string methodName)
    {
        var slider = sliderObj.GetComponent<Slider>();
        if (slider == null) return;

        var method = typeof(DichopticMovieSceneManager).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
            null,
            System.Type.EmptyTypes,
            null
        );

        if (method != null)
        {
            // CreateDelegate bound to MonoBehaviour (UnityEngine.Object) — required by Unity's persistent event system
            var action = (UnityEngine.Events.UnityAction)System.Delegate.CreateDelegate(
                typeof(UnityEngine.Events.UnityAction), target, method);
            UnityEditor.Events.UnityEventTools.AddVoidPersistentListener(slider.onValueChanged, action);
        }
    }

    static void CreateVideoPickerPanel(Transform parent, DichopticMovieSceneManager sceneManager)
    {
        // Full-size overlay panel (covers entire settings canvas)
        var pickerImg = CreateUIElement<Image>("VideoPickerPanel", parent);
        pickerImg.color = new Color(0.05f, 0.07f, 0.12f, 0.98f);
        var pickerRT = pickerImg.GetComponent<RectTransform>();
        pickerRT.anchorMin = Vector2.zero;
        pickerRT.anchorMax = Vector2.one;
        pickerRT.offsetMin = Vector2.zero;
        pickerRT.offsetMax = Vector2.zero;
        var pickerPanel = pickerImg.gameObject;

        // Title
        var title = CreateTMPText("PickerTitle", pickerPanel.transform, "Выбор видео", 30,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 240), new Vector2(600, 48));
        title.alignment = TextAlignmentOptions.Center;
        title.fontStyle = FontStyles.Bold;

        // Close button (top-right)
        var closeBtn = CreateButton("PickerCloseBtn", "✕  Закрыть", pickerPanel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(310, 240), new Vector2(140, 42));
        closeBtn.GetComponent<Image>().color = new Color(0.5f, 0.15f, 0.15f, 1f);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            closeBtn.GetComponent<Button>().onClick,
            new UnityEngine.Events.UnityAction(sceneManager.HideVideoPicker));

        // 5 video slot buttons
        float[] slotY = { 155f, 90f, 25f, -40f, -105f };
        string[] slotMethods = { "VideoPickerSelect0", "VideoPickerSelect1", "VideoPickerSelect2", "VideoPickerSelect3", "VideoPickerSelect4" };
        var slots = new GameObject[5];
        for (int i = 0; i < 5; i++)
        {
            var slotBtn = CreateButton("PickerSlot" + i, "—", pickerPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, slotY[i]), new Vector2(700, 56));
            slotBtn.GetComponent<Image>().color = new Color(0.18f, 0.22f, 0.32f, 1f);
            var slotTmp = slotBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (slotTmp != null)
            {
                slotTmp.fontSize = 20;
                slotTmp.alignment = TextAlignmentOptions.MidlineLeft;
                slotTmp.margin = new Vector4(20, 0, 0, 0);
                slotTmp.overflowMode = TextOverflowModes.Ellipsis;
            }
            var slotMethod = typeof(DichopticMovieSceneManager).GetMethod(slotMethods[i]);
            if (slotMethod != null)
            {
                var action = (UnityEngine.Events.UnityAction)System.Delegate.CreateDelegate(
                    typeof(UnityEngine.Events.UnityAction), sceneManager, slotMethod);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(slotBtn.GetComponent<Button>().onClick, action);
            }
            slots[i] = slotBtn;
        }

        // Pagination row
        float pageY = -185f;
        var prevBtn = CreateButton("PickerPrevPage", "◀", pickerPanel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-160, pageY), new Vector2(90, 46));
        prevBtn.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.45f, 1f);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            prevBtn.GetComponent<Button>().onClick,
            new UnityEngine.Events.UnityAction(sceneManager.VideoPickerPrevPage));

        var pageLabel = CreateTMPText("PickerPageLabel", pickerPanel.transform, "1 / 1", 22,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, pageY), new Vector2(200, 46));
        pageLabel.alignment = TextAlignmentOptions.Center;

        var nextBtn = CreateButton("PickerNextPage", "▶", pickerPanel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(160, pageY), new Vector2(90, 46));
        nextBtn.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.45f, 1f);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            nextBtn.GetComponent<Button>().onClick,
            new UnityEngine.Events.UnityAction(sceneManager.VideoPickerNextPage));

        // Wire to sceneManager
        sceneManager.videoPickerPanel = pickerPanel;
        sceneManager.videoPickerPageLabel = pageLabel;
        sceneManager.videoPickerSlots = slots;

        pickerPanel.SetActive(false);
    }
}

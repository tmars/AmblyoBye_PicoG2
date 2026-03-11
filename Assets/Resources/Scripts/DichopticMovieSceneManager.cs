using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using System.Collections.Generic;
using System.IO;
using System;

public class DichopticMovieSceneManager : MonoBehaviour
{
    private float DISTANCE_TO_SCREEN_IN_M = 2.0f;
    private float screenTiltAngle = 0f; // degrees, positive = up, negative = down
    private string EMPTY_MOVIE_NAME = "";

    public static DichopticMovieSceneManager Instance;

    [SerializeField]
    public GameObject moviePlayerObject;

    [SerializeField]
    public VideoPlayer videoPlayer;

    [SerializeField]
    public Material dichopticFilterMaterial;

    [SerializeField]
    public TMP_Dropdown movieListDropdown;

    [SerializeField]
    public GameObject eyeBiasSlider;

    [SerializeField]
    public GameObject blobScaleSlider;

    [SerializeField]
    public GameObject blobGrayColorSlider;

    [SerializeField]
    public GameObject blobTimerSlider;

    [SerializeField]
    public GameObject settingsUI;

    [SerializeField]
    public TextMeshProUGUI versionTextBox;

    [SerializeField]
    public ConfirmationDialog confirmationDialog;

    [SerializeField]
    public TextMeshProUGUI totalPlayedTimeTextBox;

    [SerializeField]
    public TextMeshProUGUI videoTimeText;

    [SerializeField]
    public TextMeshProUGUI distanceText;

    [SerializeField]
    public TextMeshProUGUI tiltText;

    [SerializeField]
    public TextMeshProUGUI ipdText;

    [SerializeField]
    public TextMeshProUGUI debugKeyText;

    // --- Video Picker ---
    [SerializeField] public GameObject videoPickerPanel;
    [SerializeField] public TextMeshProUGUI videoPickerPageLabel;
    [SerializeField] public GameObject[] videoPickerSlots;

    private List<string> pickerVideoList = new List<string>();
    private int pickerPage = 0;
    private const int PICKER_PAGE_SIZE = 5;

    private float currentIPD = 62.5f; // in mm

    private float sessionSecondsWatched = 0f;
    private float blobTimerDuration = 10;
    private bool wasMenuButtonPressed = false;
    private bool isCameraInit = false;
    private DichopticMovieSettingsManager settingsManager = null;

    private DailyUsageTracker usageTracker = null;
    private string currentVideoPath = "";
    private float savePositionTimer = 0f;
    private const float SAVE_INTERVAL = 5f; // save every 5 seconds

    // Stats & notifications
    private StatsDatabase statsDb = null;
    private TelegramNotifier telegram = null;
    private string currentSessionId;
    private float telegramTickTimer = 0f;
    private const float TELEGRAM_TICK_INTERVAL = 300f; // 5 minutes

    // Proximity sensor pause/resume
    private bool wasPlayingBeforeHeadsetOff = false;
    private bool pausedByHeadsetRemoval = false;

    void Awake()
    {
        Instance = this;
        usageTracker = new DailyUsageTracker();
        statsDb = new StatsDatabase();
        telegram = new TelegramNotifier();
        currentSessionId = Guid.NewGuid().ToString();
    }

    void Start()
    {
        versionTextBox.text = "v" + Application.version;
        UpdateTimeWatchedText(0);
        RestoreInitialSettingsFromPersistance();
        PopulateMovieDropdown();
        PopulateVideoPicker();
        StartCoroutine(RunBlobChangeTimer());

        // Crash detection
        if (statsDb != null && !statsDb.WasPreviousShutdownClean())
        {
            statsDb.LogEvent(currentSessionId, "crash_detected", "", 0, 0);
            telegram?.SendMessage("Previous session ended unexpectedly");
        }
        statsDb?.SetCleanShutdown(false);

        AutoPlayFirstVideo();
    }

    private const string LAST_VIDEO_FILE = "LastVideo.txt";

    private void AutoPlayFirstVideo()
    {
        // Try to restore last played video
        var result = StorageHandler.ReadFile(TypeSafeDir.Settings, LAST_VIDEO_FILE);
        if (result.Item1 && !string.IsNullOrEmpty(result.Item2))
        {
            string lastFile = result.Item2.Trim();
            foreach (string path in pickerVideoList)
            {
                if (System.IO.Path.GetFileName(path) == lastFile)
                {
                    LoadMovieByPath(path);
                    return;
                }
            }
        }

        // Fallback: first video
        if (pickerVideoList.Count > 0)
            LoadMovieByPath(pickerVideoList[0]);
    }

    void Update()
    {
        UpdateCamera();

        // Pico G2: TOUCHPAD (controller) or Escape (headset body button) toggles settings / clicks
        bool isTouchpadPressed = UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape);
#if !UNITY_EDITOR && UNITY_ANDROID
        try { isTouchpadPressed = isTouchpadPressed || Pvr_UnitySDKAPI.Controller.UPvr_GetKey(0, Pvr_UnitySDKAPI.Pvr_KeyCode.TOUCHPAD); } catch {}
#else
        isTouchpadPressed = isTouchpadPressed || UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Space);
#endif
        if (isTouchpadPressed && !wasMenuButtonPressed)
        {
            if (!settingsUI.activeSelf)
            {
                // Menu hidden → show it
                settingsUI.SetActive(true);
                VRUISetup.Instance?.SetReticleVisible(true);
            }
            else
            {
                // Menu visible → only hide if NOT gazing at a UI element
                // (if gazing at UI, PicoTouchpadClick handles the click)
                bool gazingAtUI = false;
                try { gazingAtUI = Pvr_GazeInputModule.gazeGameObject != null; }
                catch { }
                if (!gazingAtUI)
                {
                    settingsUI.SetActive(false);
                    VRUISetup.Instance?.SetReticleVisible(false);
                }
            }
        }
        wasMenuButtonPressed = isTouchpadPressed;

        if (videoPlayer.isPlaying)
        {
            usageTracker?.Tick(Time.deltaTime);
            sessionSecondsWatched += Time.deltaTime;
            UpdateTimeWatchedText((int)sessionSecondsWatched);

            // Periodically save video position
            savePositionTimer += Time.deltaTime;
            if (savePositionTimer >= SAVE_INTERVAL)
            {
                savePositionTimer = 0f;
                SaveVideoPosition();
            }

            // Periodic Telegram status update (edit session message)
            telegramTickTimer += Time.deltaTime;
            if (telegramTickTimer >= TELEGRAM_TICK_INTERVAL)
            {
                telegramTickTimer = 0f;
                int mins = (int)(sessionSecondsWatched / 60);
                telegram?.AppendSessionStatus("⏱ " + mins + " min");
            }
        }
        UpdateVideoTimeText();
        DetectKeyPress();
    }

    private int debugKeyCounter = 0;

    private void DetectKeyPress()
    {
        if (debugKeyText == null) return;

        // Method 1: Unity KeyCodes
        if (Input.anyKeyDown)
        {
            foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
            {
                if (Input.GetKeyDown(kc))
                {
                    debugKeyCounter++;
                    debugKeyText.text = debugKeyCounter + " Unity: " + kc + " (" + (int)kc + ")";
                    Debug.Log("[KEY] Unity KeyCode: " + kc + " code=" + (int)kc);
                }
            }
        }

        // Method 2: Touch on headset
        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
            {
                debugKeyCounter++;
                debugKeyText.text = debugKeyCounter + " Touch: " + t.position;
                Debug.Log("[KEY] Touch: " + t.position);
            }
        }

        // Method 3: Mouse button (some HMD buttons map as mouse)
        for (int i = 0; i < 3; i++)
        {
            if (Input.GetMouseButtonDown(i))
            {
                debugKeyCounter++;
                debugKeyText.text = debugKeyCounter + " Mouse: btn" + i;
                Debug.Log("[KEY] Mouse button: " + i);
            }
        }

        // Method 4: Pico controller keys (both hands)
#if !UNITY_EDITOR && UNITY_ANDROID
        try
        {
            var allKeys = (Pvr_UnitySDKAPI.Pvr_KeyCode[])System.Enum.GetValues(typeof(Pvr_UnitySDKAPI.Pvr_KeyCode));
            for (int hand = 0; hand <= 1; hand++)
            {
                foreach (var pk in allKeys)
                {
                    if (Pvr_UnitySDKAPI.Controller.UPvr_GetKeyDown(hand, pk))
                    {
                        debugKeyCounter++;
                        debugKeyText.text = debugKeyCounter + " Pico[" + hand + "]: " + pk;
                        Debug.Log("[KEY] Pico hand=" + hand + " key=" + pk);
                    }
                }
            }
        }
        catch {}

        // Method 5: Android native key events via JNI
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                // Check common Android keycodes via InputDevice
                // This is just for detection - poll via KeyEvent isn't ideal,
                // but Input.anyKeyDown above should catch most
            }
        }
        catch {}
#endif
    }

    // Method 6: OnGUI catches events that Update() might miss
    private void OnGUI()
    {
        if (debugKeyText == null) return;
        Event e = Event.current;
        if (e != null && e.isKey && e.type == EventType.KeyDown)
        {
            debugKeyCounter++;
            debugKeyText.text = debugKeyCounter + " GUI: " + e.keyCode + " (" + (int)e.keyCode + ")";
            Debug.Log("[KEY] OnGUI KeyCode: " + e.keyCode + " code=" + (int)e.keyCode);
        }
    }

    private void UpdateVideoTimeText()
    {
        if (videoTimeText == null) return;
        if (videoPlayer != null && videoPlayer.isPrepared)
        {
            TimeSpan cur = TimeSpan.FromSeconds(videoPlayer.time);
            TimeSpan total = TimeSpan.FromSeconds(videoPlayer.length);
            videoTimeText.text = string.Format("{0:D2}:{1:D2} / {2:D2}:{3:D2}",
                cur.Minutes + cur.Hours * 60, cur.Seconds,
                total.Minutes + total.Hours * 60, total.Seconds);
        }
        else
        {
            videoTimeText.text = "--:-- / --:--";
        }
    }

    private void UpdateTimeWatchedText(int seconds)
    {
        TimeSpan t = TimeSpan.FromSeconds(seconds);
        totalPlayedTimeTextBox.text = $"Watched: {t.Hours}h {t.Minutes:D2} min";
    }

    private void RestoreInitialSettingsFromPersistance()
    {
        settingsManager = new DichopticMovieSettingsManager(eyeBiasSlider.GetComponent<Slider>().value,
                                                    blobScaleSlider.GetComponent<Slider>().value,
                                                    blobGrayColorSlider.GetComponent<Slider>().value,
                                                    blobTimerSlider.GetComponent<Slider>().value,
                                                    DISTANCE_TO_SCREEN_IN_M,
                                                    currentIPD);
        settingsManager.TryRestore();
        RestoreSettingsPanelFromManager(settingsManager);

        // Restore screen distance
        float savedDist = settingsManager.GetScreenDistance();
        if (savedDist != DISTANCE_TO_SCREEN_IN_M)
        {
            DISTANCE_TO_SCREEN_IN_M = savedDist;
            MoveScreenToDistance();
        }

        // Restore IPD
        float savedIPD = settingsManager.GetIPD();
        if (savedIPD != currentIPD)
        {
            currentIPD = savedIPD;
            ApplyIPD();
        }
    }

    public void ResetSettingsButtonHandler()
    {
        confirmationDialog.Show("Reset settings to their default values?", yes =>
        {
            if (yes)
            {
                ResetSettings();
            }
        });
    }

    public void DeleteSelectedMovieButtonHandler()
    {
        string movieToDelete = movieListDropdown.captionText.text;
        string deletionConfirmationMessage = $"Are you sure you want to delete {movieToDelete} ?";
        confirmationDialog.Show(deletionConfirmationMessage, yes =>
        {
            if (yes)
            {
                DeleteSelectedMovie(movieToDelete);
            }
        });
    }

    private void ResetSettings()
    {
        settingsManager = new DichopticMovieSettingsManager(0.5F, 1.0F, 70.0F, 5.0F, 2.0F, 62.5F);
        settingsManager.StoreSettings();
        RestoreSettingsPanelFromManager(settingsManager);
        DISTANCE_TO_SCREEN_IN_M = 2.0f;
        MoveScreenToDistance();
        currentIPD = 62.5f;
        ApplyIPD();
    }

    private void RestoreSettingsPanelFromManager(DichopticMovieSettingsManager settingsManager)
    {
        HandleChangeEyeBiasValue(settingsManager.GetEyeBiasValue());
        HandleChangeBlobScale(settingsManager.GetBlobScaleValue());
        HandleChangeBlobGreyValue(settingsManager.GetBlobGreyColorValue());
        HandleChangeBlobTimerValue(settingsManager.GetBlobTimerValue());
    }

    public void RefreshVideoList()
    {
        PopulateMovieDropdown();
        PopulateVideoPicker();
    }

    private void PopulateMovieDropdown()
    {
        StorageHandler.InitDirectoryTree();
        List<string> availableMoviePaths = StorageHandler.GetFilePathsFromDir(TypeSafeDir.Movies);
        List<string> allowedExtensions = new List<string> { ".asf", ".avi", ".dv", ".m4v", ".mp4", ".mov", ".mpg", ".mpeg", ".ogv", ".vp8", ".webm", ".wmv" };

        // Filter by extension, then sort by file creation time (newest first)
        var filtered = new List<string>();
        foreach (var fp in availableMoviePaths)
            if (allowedExtensions.Contains(Path.GetExtension(fp).ToLowerInvariant()))
                filtered.Add(fp);
        filtered.Sort((a, b) => File.GetCreationTime(b).CompareTo(File.GetCreationTime(a)));

        movieListDropdown.ClearOptions();
        movieListDropdown.AddOptions(new List<string> { EMPTY_MOVIE_NAME });
        foreach (string fp in filtered)
            movieListDropdown.AddOptions(new List<string> { Path.GetFileName(fp) });
    }

    public void ChangeEyeBiasValue()
    {
        float eyeBiasValue = eyeBiasSlider.GetComponent<Slider>().value;
        settingsManager?.SetEyeBiasValue(eyeBiasValue);
        HandleChangeEyeBiasValue(eyeBiasValue);
    }

    public void ChangeBlobScale()
    {
        float blobScaleValue = blobScaleSlider.GetComponent<Slider>().value;
        settingsManager?.SetBlobScaleValue(blobScaleValue);
        HandleChangeBlobScale(blobScaleValue);
    }

    public void ChangeBlobGreyValue()
    {
        float greyColorValue = blobGrayColorSlider.GetComponent<Slider>().value;
        settingsManager?.SetBlobGreyColorValue(greyColorValue);
        HandleChangeBlobGreyValue(greyColorValue);
    }

    public void ChangeBlobTimerValue()
    {
        float blobTimerValue = blobTimerSlider.GetComponent<Slider>().value;
        settingsManager?.SetBlobTimerValue(blobTimerValue);
        HandleChangeBlobTimerValue(blobTimerValue);
    }

    private Material GetRendererMaterial()
    {
        var renderer = moviePlayerObject.GetComponent<MeshRenderer>();
        return renderer != null ? renderer.material : null;
    }

    private void SetMaterialFloat(string property, float value)
    {
        dichopticFilterMaterial.SetFloat(property, value);
        var mat = GetRendererMaterial();
        if (mat != null) mat.SetFloat(property, value);
    }

    private void SetMaterialColor(string property, Color value)
    {
        dichopticFilterMaterial.SetColor(property, value);
        var mat = GetRendererMaterial();
        if (mat != null) mat.SetColor(property, value);
    }

    private void SetMaterialVector(string property, Vector4 value)
    {
        dichopticFilterMaterial.SetVector(property, value);
        var mat = GetRendererMaterial();
        if (mat != null) mat.SetVector(property, value);
    }

    private void UpdateSliderUI(GameObject sliderObj, float value)
    {
        sliderObj.GetComponent<Slider>().value = value;
        var spm = sliderObj.GetComponent<SliderPlusMinus>();
        if (spm != null) spm.UpdateText();
    }

    private void HandleChangeEyeBiasValue(float eyeBiasValue)
    {
        UpdateSliderUI(eyeBiasSlider, eyeBiasValue);
        SetMaterialFloat("_BlobClipping", eyeBiasValue);
    }

    private void HandleChangeBlobScale(float blobScaleValue)
    {
        UpdateSliderUI(blobScaleSlider, blobScaleValue);
        SetMaterialFloat("_BlobScale", blobScaleValue);
    }

    private void HandleChangeBlobGreyValue(float greyColorValue)
    {
        UpdateSliderUI(blobGrayColorSlider, greyColorValue);
        SetMaterialColor("_BlobColor", new Color(greyColorValue / 255, greyColorValue / 255, greyColorValue / 255, 0));
    }

    private void HandleChangeBlobTimerValue(float blobTimerValue)
    {
        UpdateSliderUI(blobTimerSlider, blobTimerValue);
        blobTimerDuration = blobTimerValue;
    }

    // ---- Pause/Play ----
    public void TogglePause()
    {
        if (videoPlayer == null) return;
        pausedByHeadsetRemoval = false; // manual toggle overrides headset state
        if (videoPlayer.isPlaying)
            videoPlayer.Pause();
        else
            videoPlayer.Play();
    }

    // ---- Send stats to Telegram ----
    public void SendStatsToTelegram()
    {
        if (statsDb == null || telegram == null || !telegram.IsConfigured) return;
        string dbPath = statsDb.GetDbFilePath();
        StartCoroutine(telegram.SendFile(dbPath, "AmblyoBye stats database"));
    }

    // ---- Screen distance controls ----
    public void ScreenCloser()
    {
        DISTANCE_TO_SCREEN_IN_M = Mathf.Max(0.5f, DISTANCE_TO_SCREEN_IN_M - 0.25f);
        settingsManager?.SetScreenDistance(DISTANCE_TO_SCREEN_IN_M);
        MoveScreenToDistance();
    }

    public void ScreenFarther()
    {
        DISTANCE_TO_SCREEN_IN_M = Mathf.Min(5f, DISTANCE_TO_SCREEN_IN_M + 0.25f);
        settingsManager?.SetScreenDistance(DISTANCE_TO_SCREEN_IN_M);
        MoveScreenToDistance();
    }

    // ---- Screen tilt controls (arc up/down) ----
    public void ScreenTiltUp()
    {
        screenTiltAngle = Mathf.Min(90f, screenTiltAngle + 5f);
        MoveScreenToDistance();
    }

    public void ScreenTiltDown()
    {
        screenTiltAngle = Mathf.Max(-90f, screenTiltAngle - 5f);
        MoveScreenToDistance();
    }

    private void MoveScreenToDistance()
    {
        // Use Pvr_UnitySDK root as stable reference (doesn't move with head tracking)
        var pvrSDK = GameObject.Find("Pvr_UnitySDK");
        Transform refTransform = pvrSDK != null ? pvrSDK.transform : null;

        if (refTransform == null && Camera.main != null)
            refTransform = Camera.main.transform;

        if (refTransform != null && moviePlayerObject != null)
        {
            Vector3 origin = refTransform.position;
            Vector3 forward = refTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward = forward.normalized;

            // Apply tilt: move along an arc (same radius, different vertical angle)
            float tiltRad = screenTiltAngle * Mathf.Deg2Rad;
            float horizontalDist = DISTANCE_TO_SCREEN_IN_M * Mathf.Cos(tiltRad);
            float verticalOffset = DISTANCE_TO_SCREEN_IN_M * Mathf.Sin(tiltRad);

            Vector3 newPos = origin + forward * horizontalDist;
            newPos.y = origin.y + verticalOffset;
            moviePlayerObject.transform.position = newPos;

            // Rotation and settings canvas positioning handled by UpdateCamera every frame

            isCameraInit = true;
        }
        if (distanceText != null)
            distanceText.text = DISTANCE_TO_SCREEN_IN_M.ToString("0.00") + "m";
        if (tiltText != null)
            tiltText.text = screenTiltAngle.ToString("0") + "°";
    }

    // ---- IPD controls ----
    public void IPDIncrease()
    {
        currentIPD = Mathf.Min(70f, currentIPD + 1f);
        settingsManager?.SetIPD(currentIPD);
        ApplyIPD();
    }

    public void IPDDecrease()
    {
        currentIPD = Mathf.Max(45f, currentIPD - 1f);
        settingsManager?.SetIPD(currentIPD);
        ApplyIPD();
    }

    private void ApplyIPD()
    {
        float ipdMeters = currentIPD / 1000f;

        // Try SDK API first
#if !UNITY_EDITOR && UNITY_ANDROID
        try { Pvr_UnitySDKAPI.System.UPvr_SetIPD(ipdMeters); } catch {}
#endif

        // Also directly set eye offsets on Pvr_UnitySDKManager (guaranteed to work)
        if (Pvr_UnitySDKManager.SDK != null)
        {
            Pvr_UnitySDKManager.SDK.leftEyeOffset = new Vector3(-ipdMeters / 2f, 0, 0);
            Pvr_UnitySDKManager.SDK.rightEyeOffset = new Vector3(ipdMeters / 2f, 0, 0);

            // Update each eye camera position
            var eyeManager = Pvr_UnitySDKEyeManager.Instance;
            if (eyeManager != null && eyeManager.Eyes != null)
            {
                for (int i = 0; i < eyeManager.Eyes.Length; i++)
                {
                    eyeManager.Eyes[i].RefreshCameraPosition(ipdMeters);
                }
            }
        }

        if (ipdText != null)
            ipdText.text = currentIPD.ToString("0") + " mm";
        Debug.Log("[DichopticScene] IPD set to: " + currentIPD + "mm (" + ipdMeters + "m)");
    }

    // ---- Seek controls ----
    private void SeekVideo(double seconds)
    {
        if (videoPlayer != null && videoPlayer.isPrepared)
        {
            double target = videoPlayer.time + seconds;
            videoPlayer.time = System.Math.Max(0, System.Math.Min(target, videoPlayer.length));
        }
    }

    public void SeekBack5s()  { SeekVideo(-5); }
    public void SeekBack10s() { SeekVideo(-10); }
    public void SeekBack1m()  { SeekVideo(-60); }
    public void SeekBack5m()  { SeekVideo(-300); }
    public void SeekBack10m() { SeekVideo(-600); }
    public void SeekFwd5s()   { SeekVideo(5); }
    public void SeekFwd10s()  { SeekVideo(10); }
    public void SeekFwd1m()   { SeekVideo(60); }
    public void SeekFwd5m()   { SeekVideo(300); }
    public void SeekFwd10m()  { SeekVideo(600); }

    public void LoadMovieByPath(string filepath)
    {
        // Save position of previous video before switching
        SaveVideoPosition();

        currentVideoPath = filepath;
        videoPlayer.source = VideoSource.Url;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
        videoPlayer.controlledAudioTrackCount = 1;
        videoPlayer.GetComponent<AudioSource>().volume = 1.0f;
        videoPlayer.url = filepath;

        // Restore saved position after video is prepared
        double savedPos = GetSavedPosition(filepath);
        if (savedPos > 1.0)
        {
            VideoPlayer.EventHandler handler = null;
            handler = (vp) =>
            {
                vp.prepareCompleted -= handler;
                if (savedPos < vp.length - 5)
                    vp.time = savedPos;
            };
            videoPlayer.prepareCompleted += handler;
        }

        videoPlayer.Play();
        settingsUI.SetActive(false);
        VRUISetup.Instance?.SetReticleVisible(false);

        // Save last played video
        string videoName = System.IO.Path.GetFileName(filepath);
        StorageHandler.WriteFile(TypeSafeDir.Settings, LAST_VIDEO_FILE, videoName);

        // Send weekly summary on first play of the day
        if (statsDb != null && statsDb.IsFirstSessionToday() && telegram != null)
        {
            string summary = statsDb.GetWeeklySummary();
            if (!string.IsNullOrEmpty(summary))
                telegram.SendMessage(summary);
        }

        // Log session start & notify
        statsDb?.LogEvent(currentSessionId, "start", videoName, sessionSecondsWatched, 0);
        telegram?.StartSessionMessage(videoName);
        telegramTickTimer = 0f;
    }

    public void LoadMovieButtonHandle()
    {
        string selectedFilename = movieListDropdown.captionText.text;
        if (selectedFilename != EMPTY_MOVIE_NAME)
        {
            List<string> availableMovies = StorageHandler.GetFilePathsFromDir(TypeSafeDir.Movies);
            foreach (string filepath in availableMovies)
            {
                if (filepath.Contains(selectedFilename))
                {
                    LoadMovieByPath(filepath);
                    break;
                }
            }
        }
    }

    // --- Video Picker ---
    public void ShowVideoPicker()
    {
        PopulateVideoPicker();
        if (videoPickerPanel != null) videoPickerPanel.SetActive(true);
    }

    public void HideVideoPicker()
    {
        if (videoPickerPanel != null) videoPickerPanel.SetActive(false);
    }

    public void VideoPickerNextPage()
    {
        int maxPage = pickerVideoList.Count == 0 ? 0 : (pickerVideoList.Count - 1) / PICKER_PAGE_SIZE;
        pickerPage = Mathf.Min(pickerPage + 1, maxPage);
        RefreshPickerPage();
    }

    public void VideoPickerPrevPage()
    {
        pickerPage = Mathf.Max(pickerPage - 1, 0);
        RefreshPickerPage();
    }

    public void VideoPickerSelect0() { VideoPickerSelectSlot(0); }
    public void VideoPickerSelect1() { VideoPickerSelectSlot(1); }
    public void VideoPickerSelect2() { VideoPickerSelectSlot(2); }
    public void VideoPickerSelect3() { VideoPickerSelectSlot(3); }
    public void VideoPickerSelect4() { VideoPickerSelectSlot(4); }

    private void VideoPickerSelectSlot(int slotIndex)
    {
        int idx = pickerPage * PICKER_PAGE_SIZE + slotIndex;
        if (idx < pickerVideoList.Count)
        {
            LoadMovieByPath(pickerVideoList[idx]);
            HideVideoPicker();
        }
    }

    private void PopulateVideoPicker()
    {
        StorageHandler.InitDirectoryTree();
        var allFiles = StorageHandler.GetFilePathsFromDir(TypeSafeDir.Movies);
        var allowed = new System.Collections.Generic.HashSet<string>
            { ".asf", ".avi", ".dv", ".m4v", ".mp4", ".mov", ".mpg", ".mpeg", ".ogv", ".vp8", ".webm", ".wmv" };
        pickerVideoList = new List<string>();
        foreach (var f in allFiles)
            if (allowed.Contains(Path.GetExtension(f).ToLowerInvariant()))
                pickerVideoList.Add(f);
        // Sort by file creation time (newest first)
        pickerVideoList.Sort((a, b) => File.GetCreationTime(b).CompareTo(File.GetCreationTime(a)));
        pickerPage = 0;
        RefreshPickerPage();
    }

    private void RefreshPickerPage()
    {
        if (videoPickerSlots == null) return;
        int total = pickerVideoList.Count;
        int maxPage = total == 0 ? 0 : (total - 1) / PICKER_PAGE_SIZE;
        if (videoPickerPageLabel != null)
            videoPickerPageLabel.text = total == 0 ? "Нет видео" : (pickerPage + 1) + " / " + (maxPage + 1);
        for (int i = 0; i < videoPickerSlots.Length; i++)
        {
            if (videoPickerSlots[i] == null) continue;
            int idx = pickerPage * PICKER_PAGE_SIZE + i;
            if (idx < total)
            {
                videoPickerSlots[i].SetActive(true);
                var txt = videoPickerSlots[i].GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null) txt.text = Path.GetFileNameWithoutExtension(pickerVideoList[idx]);
            }
            else
            {
                videoPickerSlots[i].SetActive(false);
            }
        }
    }

    private void DeleteSelectedMovie(string movieSelectedFilename)
    {
        if (movieSelectedFilename != EMPTY_MOVIE_NAME)
        {
            StorageHandler.DeleteFileFromDir(TypeSafeDir.Movies, movieSelectedFilename);
            PopulateMovieDropdown();
        }
    }

    IEnumerator RunBlobChangeTimer()
    {
        System.Random random = new System.Random();
        while (true)
        {
            yield return new WaitForSeconds(blobTimerDuration);

            float f1 = (float)(32748 * 2.0 * (random.NextDouble() - 0.5));
            float f2 = (float)(32748 * 2.0 * (random.NextDouble() - 0.5));
            SetMaterialVector("_BlobOffset", new Vector2(f1, f2));
        }
    }

    // ---- Video position persistence ----
    private const string VIDEO_POSITIONS_FILE = "VideoPositions.json";

    private void SaveVideoPosition()
    {
        if (string.IsNullOrEmpty(currentVideoPath)) return;
        if (videoPlayer == null || !videoPlayer.isPrepared) return;
        if (videoPlayer.time < 1.0) return;

        string key = System.IO.Path.GetFileName(currentVideoPath);
        var positions = LoadPositionsDict();
        positions[key] = videoPlayer.time.ToString("F1");

        string json = "{";
        bool first = true;
        foreach (var kvp in positions)
        {
            if (!first) json += ",";
            json += "\"" + EscapeJson(kvp.Key) + "\":\"" + kvp.Value + "\"";
            first = false;
        }
        json += "}";

        StorageHandler.WriteFile(TypeSafeDir.Settings, VIDEO_POSITIONS_FILE, json);
    }

    private double GetSavedPosition(string filepath)
    {
        string key = System.IO.Path.GetFileName(filepath);
        var positions = LoadPositionsDict();
        if (positions.ContainsKey(key))
        {
            double val;
            if (double.TryParse(positions[key], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out val))
                return val;
        }
        return 0;
    }

    private Dictionary<string, string> LoadPositionsDict()
    {
        var dict = new Dictionary<string, string>();
        var result = StorageHandler.ReadFile(TypeSafeDir.Settings, VIDEO_POSITIONS_FILE);
        if (result.Item1 && !string.IsNullOrEmpty(result.Item2))
        {
            // Simple JSON parse: {"key":"value","key2":"value2"}
            string json = result.Item2.Trim();
            if (json.StartsWith("{") && json.EndsWith("}"))
            {
                json = json.Substring(1, json.Length - 2);
                string[] pairs = json.Split(',');
                foreach (string pair in pairs)
                {
                    int colon = pair.IndexOf(':');
                    if (colon > 0)
                    {
                        string k = pair.Substring(0, colon).Trim().Trim('"');
                        string v = pair.Substring(colon + 1).Trim().Trim('"');
                        dict[k] = v;
                    }
                }
            }
        }
        return dict;
    }

    private string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    void OnApplicationPause(bool pause)
    {
        if (pause)
        {
            SaveVideoPosition();
            statsDb?.SetCleanShutdown(true); // safety: Android may kill after pause

            if (videoPlayer != null && videoPlayer.isPlaying)
            {
                wasPlayingBeforeHeadsetOff = true;
                pausedByHeadsetRemoval = true;
                videoPlayer.Pause();

                int mins = (int)(sessionSecondsWatched / 60);
                string videoName = Path.GetFileName(currentVideoPath);
                statsDb?.LogEvent(currentSessionId, "pause_headset", videoName, sessionSecondsWatched,
                    videoPlayer.isPrepared ? videoPlayer.time : 0);
                telegram?.AppendSessionStatus("😴 Paused at " + mins + " min");
            }
        }
        else
        {
            statsDb?.SetCleanShutdown(false); // back to in-progress

            if (wasPlayingBeforeHeadsetOff && videoPlayer != null)
            {
                videoPlayer.Play();
                wasPlayingBeforeHeadsetOff = false;
                pausedByHeadsetRemoval = false;

                string videoName = Path.GetFileName(currentVideoPath);
                statsDb?.LogEvent(currentSessionId, "resume_headset", videoName, sessionSecondsWatched,
                    videoPlayer.isPrepared ? videoPlayer.time : 0);
                telegram?.AppendSessionStatus("▶️ Resumed");
            }
        }
    }

    void OnApplicationQuit()
    {
        SaveVideoPosition();

        int mins = (int)(sessionSecondsWatched / 60);
        int todayTotal = statsDb != null ? statsDb.GetTodayTotalSeconds() / 60 + mins : mins;
        string videoName = Path.GetFileName(currentVideoPath);

        statsDb?.LogEvent(currentSessionId, "stop", videoName, sessionSecondsWatched,
            videoPlayer != null && videoPlayer.isPrepared ? videoPlayer.time : 0);
        statsDb?.SetCleanShutdown(true);
        telegram?.EndSession("🔴 Ended: " + mins + " min. Today: " + todayTotal + " min");
        statsDb?.Close();
    }

    private void UpdateCamera()
    {
        if (!isCameraInit)
        {
            if (!Camera.main)
            {
                return;
            }

            Vector3 forwardFlat = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            if (forwardFlat.sqrMagnitude < 0.001f)
            {
                forwardFlat = Camera.main.transform.forward;
            }

            float tiltRad = screenTiltAngle * Mathf.Deg2Rad;
            float hDist = DISTANCE_TO_SCREEN_IN_M * Mathf.Cos(tiltRad);
            float vOffset = DISTANCE_TO_SCREEN_IN_M * Mathf.Sin(tiltRad);

            Vector3 p2 = Camera.main.transform.position + forwardFlat * hDist;
            p2.y = Camera.main.transform.position.y + vOffset;
            moviePlayerObject.transform.position = p2;

            isCameraInit = true;
        }

        if (Camera.main)
        {
            // Base rotation: face camera horizontally (ignore vertical)
            Vector3 flatLookDir = moviePlayerObject.transform.position - Camera.main.transform.position;
            flatLookDir.y = 0f;
            if (flatLookDir.sqrMagnitude > 0.001f)
            {
                Quaternion baseRot = Quaternion.LookRotation(flatLookDir, Vector3.up);
                // Apply tilt: rotate around screen's local X axis (pitch backward)
                Quaternion tiltRot = Quaternion.AngleAxis(-screenTiltAngle, Vector3.right);
                moviePlayerObject.transform.rotation = baseRot * tiltRot;
            }

            // Settings canvas follows screen every frame
            if (settingsUI != null)
            {
                Vector3 toViewer = -moviePlayerObject.transform.forward;
                settingsUI.transform.position = moviePlayerObject.transform.position + toViewer * 0.01f;
                settingsUI.transform.rotation = moviePlayerObject.transform.rotation;
            }
        }
    }
}

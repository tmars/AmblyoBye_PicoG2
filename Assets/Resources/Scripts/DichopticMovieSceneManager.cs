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

    private float sessionSecondsWatched = 0f;
    private float blobTimerDuration = 10;
    private bool wasMenuButtonPressed = false;
    private bool isCameraInit = false;
    private DichopticMovieSettingsManager settingsManager = null;

    private DailyUsageTracker usageTracker = null;

    void Awake()
    {
        Instance = this;
        usageTracker = new DailyUsageTracker();
    }

    void Start()
    {
        versionTextBox.text = "v" + Application.version;
        UpdateTimeWatchedText(0);
        RestoreInitialSettingsFromPersistance();
        PopulateMovieDropdown();
        StartCoroutine(RunBlobChangeTimer());
        AutoPlayFirstVideo();
    }

    private void AutoPlayFirstVideo()
    {
        // Auto-play first available video if any
        if (movieListDropdown.options.Count > 1)
        {
            movieListDropdown.value = 1; // Select first real video (index 0 is empty)
            movieListDropdown.RefreshShownValue();
            LoadMovieButtonHandle();
        }
    }

    void Update()
    {
        UpdateCamera();

        // Pico G2 3DoF controller: TOUCHPAD toggles settings UI or clicks UI elements
#if !UNITY_EDITOR && UNITY_ANDROID
        bool isTouchpadPressed = Pvr_UnitySDKAPI.Controller.UPvr_GetKey(0, Pvr_UnitySDKAPI.Pvr_KeyCode.TOUCHPAD);
#else
        bool isTouchpadPressed = UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Space);
#endif
        if (isTouchpadPressed && !wasMenuButtonPressed)
        {
            if (!settingsUI.activeSelf)
            {
                // Menu hidden → show it
                settingsUI.SetActive(true);
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
                }
            }
        }
        wasMenuButtonPressed = isTouchpadPressed;

        if (videoPlayer.isPlaying)
        {
            usageTracker?.Tick(Time.deltaTime);
            sessionSecondsWatched += Time.deltaTime;
            UpdateTimeWatchedText((int)sessionSecondsWatched);
        }
        UpdateVideoTimeText();
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
                                                    DISTANCE_TO_SCREEN_IN_M);
        settingsManager.TryRestore();
        RestoreSettingsPanelFromManager(settingsManager);

        // Restore screen distance
        float savedDist = settingsManager.GetScreenDistance();
        if (savedDist != DISTANCE_TO_SCREEN_IN_M)
        {
            DISTANCE_TO_SCREEN_IN_M = savedDist;
            MoveScreenToDistance();
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
        settingsManager = new DichopticMovieSettingsManager(0.5F, 1.0F, 70.0F, 5.0F, 2.0F);
        settingsManager.StoreSettings();
        RestoreSettingsPanelFromManager(settingsManager);
        DISTANCE_TO_SCREEN_IN_M = 2.0f;
        MoveScreenToDistance();
    }

    private void RestoreSettingsPanelFromManager(DichopticMovieSettingsManager settingsManager)
    {
        HandleChangeEyeBiasValue(settingsManager.GetEyeBiasValue());
        HandleChangeBlobScale(settingsManager.GetBlobScaleValue());
        HandleChangeBlobGreyValue(settingsManager.GetBlobGreyColorValue());
        HandleChangeBlobTimerValue(settingsManager.GetBlobTimerValue());
    }

    private void PopulateMovieDropdown()
    {
        StorageHandler.InitDirectoryTree();
        List<string> availableMovies = StorageHandler.GetFileNamesFromDir(TypeSafeDir.Movies);
        availableMovies.Sort(StringComparer.OrdinalIgnoreCase);
        List<string> allowedExtensions = new List<string> { ".asf", ".avi", ".dv", ".m4v", ".mp4", ".mov", ".mpg", ".mpeg", ".ogv", ".vp8", ".webm", ".wmv" };

        movieListDropdown.ClearOptions();
        movieListDropdown.AddOptions(new List<string> { EMPTY_MOVIE_NAME });
        foreach (string movieFileName in availableMovies)
        {
            if (allowedExtensions.Contains(Path.GetExtension(movieFileName)))
            {
                movieListDropdown.AddOptions(new List<string> { movieFileName });
            }
        }
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
        if (videoPlayer.isPlaying)
            videoPlayer.Pause();
        else
            videoPlayer.Play();
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

            Vector3 newPos = origin + forward * DISTANCE_TO_SCREEN_IN_M;
            newPos.y = origin.y;
            moviePlayerObject.transform.position = newPos;

            // Move settings canvas slightly in front of screen
            if (settingsUI != null)
            {
                Vector3 canvasPos = origin + forward * (DISTANCE_TO_SCREEN_IN_M - 0.05f);
                canvasPos.y = origin.y;
                settingsUI.transform.position = canvasPos;
            }

            isCameraInit = true;
            Debug.Log("[DichopticScene] Screen moved to distance: " + DISTANCE_TO_SCREEN_IN_M + "m");
        }
        if (distanceText != null)
            distanceText.text = DISTANCE_TO_SCREEN_IN_M.ToString("0.00") + "m";
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
                    videoPlayer.source = VideoSource.Url;
                    videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
                    videoPlayer.controlledAudioTrackCount = 1;
                    videoPlayer.GetComponent<AudioSource>().volume = 1.0f;
                    videoPlayer.url = filepath;
                    videoPlayer.Play();
                    settingsUI.SetActive(false);
                    break;
                }
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

            Vector3 p2 = Camera.main.transform.position + forwardFlat * DISTANCE_TO_SCREEN_IN_M;
            p2.y = Camera.main.transform.position.y;
            moviePlayerObject.transform.position = p2;

            isCameraInit = true;
        }

        if (Camera.main)
        {
            Vector3 lookDir = moviePlayerObject.transform.position - Camera.main.transform.position;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                moviePlayerObject.transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }
        }
    }
}

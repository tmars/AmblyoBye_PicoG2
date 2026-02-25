using System;

public class DichopticMovieSettingsManager
{
    [Serializable]
    public struct DichopticMovieSettingsStruct
    {
        public float EyeBiasValue;
        public float BlobScaleValue;
        public float BlobGreyColorValue;
        public float BlobTimerValue;
        public float ScreenDistance;
        public float IPD;
    }

    private const string SETTINGS_FILENAME = "DichopticMovieSettings.cfg";

    private DichopticMovieSettingsStruct _dichopticMovieSettings;

    public DichopticMovieSettingsManager(float eyeBiasValue, float blobScaleValue, float blobGreyColorValue, float blobTimerValue, float screenDistance = 2.0f, float ipd = 62.5f)
    {
        _dichopticMovieSettings = new DichopticMovieSettingsStruct
        {
            EyeBiasValue = eyeBiasValue,
            BlobScaleValue = blobScaleValue,
            BlobGreyColorValue = blobGreyColorValue,
            BlobTimerValue = blobTimerValue,
            ScreenDistance = screenDistance,
            IPD = ipd,
        };
    }

    public void TryRestore()
    {
        if (!RestoreSettings())
        {
            StoreSettings();
        }
    }

    public void StoreSettings()
    {
        SettingsHandler.StoreSettings(SETTINGS_FILENAME, _dichopticMovieSettings);
    }

    public bool RestoreSettings()
    {
        bool isSuccess = false;
        DichopticMovieSettingsStruct tempStruct = new DichopticMovieSettingsStruct();
        if (SettingsHandler.RestoreSettings(SETTINGS_FILENAME, ref tempStruct))
        {
            _dichopticMovieSettings = tempStruct;
            isSuccess = true;
        }
        return isSuccess;
    }


    public void SetEyeBiasValue(float value)
    {
        _dichopticMovieSettings.EyeBiasValue = value;
        StoreSettings();
    }

    public void SetBlobScaleValue(float value)
    {
        _dichopticMovieSettings.BlobScaleValue = value;
        StoreSettings();
    }

    public void SetBlobGreyColorValue(float value)
    {
        _dichopticMovieSettings.BlobGreyColorValue = value;
        StoreSettings();
    }

    public void SetBlobTimerValue(float value)
    {
        _dichopticMovieSettings.BlobTimerValue = value;
        StoreSettings();
    }

    public float GetEyeBiasValue()
    {
        return _dichopticMovieSettings.EyeBiasValue;
    }

    public float GetBlobScaleValue()
    {
        return _dichopticMovieSettings.BlobScaleValue;
    }

    public float GetBlobGreyColorValue()
    {
        return _dichopticMovieSettings.BlobGreyColorValue;
    }

    public float GetBlobTimerValue()
    {
        return _dichopticMovieSettings.BlobTimerValue;
    }

    public void SetScreenDistance(float value)
    {
        _dichopticMovieSettings.ScreenDistance = value;
        StoreSettings();
    }

    public float GetScreenDistance()
    {
        return _dichopticMovieSettings.ScreenDistance > 0.1f ? _dichopticMovieSettings.ScreenDistance : 2.0f;
    }

    public void SetIPD(float value)
    {
        _dichopticMovieSettings.IPD = value;
        StoreSettings();
    }

    public float GetIPD()
    {
        return _dichopticMovieSettings.IPD > 1f ? _dichopticMovieSettings.IPD : 62.5f;
    }
}

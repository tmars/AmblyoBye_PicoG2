using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Runtime setup: finds VR camera, sets it on Canvas, creates crosshair reticle,
/// and configures per-eye dichoptic rendering.
/// </summary>
public class VRUISetup : MonoBehaviour
{
    public Canvas targetCanvas;

    private GameObject reticle;

    public static VRUISetup Instance { get; private set; }

    public void SetReticleVisible(bool visible)
    {
        if (reticle != null) reticle.SetActive(visible);
    }

    void Start()
    {
        Instance = this;
        SetupCanvasCamera();
        SetupEyeIndex();
        CreateReticle();
        SetReticleVisible(false); // скрыт пока меню закрыто
    }

    void SetupEyeIndex()
    {
        var leftEye = GameObject.Find("LeftEye");
        if (leftEye != null)
        {
            var setter = leftEye.AddComponent<PicoEyeIndexSetter>();
            setter.eyeIndex = 0f;
            Debug.Log("[VRUISetup] LeftEye eye index = 0");
        }

        var rightEye = GameObject.Find("RightEye");
        if (rightEye != null)
        {
            var setter = rightEye.AddComponent<PicoEyeIndexSetter>();
            setter.eyeIndex = 1f;
            Debug.Log("[VRUISetup] RightEye eye index = 1");
        }
    }

    void SetupCanvasCamera()
    {
        if (targetCanvas == null)
            targetCanvas = GetComponent<Canvas>();
        if (targetCanvas == null)
            return;

        Camera vrCam = null;

        // Use Head camera (enabled, MainCamera tag) — NOT BothEye (disabled)
        var head = GameObject.Find("Head");
        if (head != null)
            vrCam = head.GetComponent<Camera>();

        if (vrCam == null)
            vrCam = Camera.main;

        if (vrCam != null)
        {
            targetCanvas.worldCamera = vrCam;
            Debug.Log("[VRUISetup] Canvas camera set to: " + vrCam.gameObject.name);
        }
    }

    void CreateReticle()
    {
        Camera cam = targetCanvas != null ? targetCanvas.worldCamera : Camera.main;
        if (cam == null) return;

        reticle = GameObject.CreatePrimitive(PrimitiveType.Quad);
        reticle.name = "GazeReticle";
        reticle.transform.SetParent(cam.transform);
        reticle.transform.localPosition = new Vector3(0, 0, 1.5f);
        reticle.transform.localRotation = Quaternion.identity;
        reticle.transform.localScale = new Vector3(0.005f, 0.005f, 0.005f);

        // Remove collider so it doesn't block raycasts
        var col = reticle.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // Load pre-saved material from Resources (shader is guaranteed to be in build)
        var renderer = reticle.GetComponent<Renderer>();
        var mat = Resources.Load<Material>("Materials/ReticleMaterial");
        if (mat != null)
        {
            mat = new Material(mat); // Clone so we don't modify the asset
            mat.renderQueue = 4000;
            renderer.material = mat;
        }
        else
        {
            // Last resort fallback - Sprites/Default is always included
            Shader fallback = Shader.Find("Sprites/Default");
            if (fallback != null)
            {
                var fallbackMat = new Material(fallback);
                fallbackMat.color = Color.white;
                fallbackMat.renderQueue = 4000;
                renderer.material = fallbackMat;
            }
            Debug.LogWarning("[VRUISetup] ReticleMaterial not found in Resources, using fallback");
        }
    }
}

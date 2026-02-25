using UnityEngine;

/// <summary>
/// Attach to each eye camera (LeftEye, RightEye).
/// Sets global shader float _PicoEyeIndex before each eye renders
/// so the dichoptic shader knows which eye is being drawn.
/// </summary>
public class PicoEyeIndexSetter : MonoBehaviour
{
    [Tooltip("0 = Left eye, 1 = Right eye")]
    public float eyeIndex = 0f;

    void OnPreRender()
    {
        Shader.SetGlobalFloat("_PicoEyeIndex", eyeIndex);
    }
}

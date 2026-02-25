using UnityEngine;

/// <summary>
/// Head-based laser pointer for Pico G2 3DoF.
/// Laser shoots from head camera forward direction.
/// Pvr_UIPointer on Head handles raycasting, Pvr_InputModule handles touchpad clicks.
/// </summary>
public class LaserPointerSetup : MonoBehaviour
{
    private LineRenderer laserLine;
    private Transform headTransform;

    void Start()
    {
        StartCoroutine(DelayedSetup());
    }

    System.Collections.IEnumerator DelayedSetup()
    {
        // Wait a frame for Pico SDK to initialize
        yield return null;
        yield return null;

        // Find the Head transform from Pvr_UnitySDK
        var head = GameObject.Find("Head");
        if (head != null)
        {
            headTransform = head.transform;

            // Add Pvr_UIPointer for Pvr_InputModule raycasting
            var uiPointer = head.GetComponent<Pvr_UIPointer>();
            if (uiPointer == null)
            {
                uiPointer = head.AddComponent<Pvr_UIPointer>();
                uiPointer.clickMethod = Pvr_UIPointer.ClickMethods.ClickOnButtonUp;
            }

            CreateLaserLine(head);
            Debug.Log("[LaserSetup] Head laser pointer active");
        }
        else
        {
            Debug.LogWarning("[LaserSetup] Head not found!");
        }
    }

    void CreateLaserLine(GameObject parent)
    {
        laserLine = parent.AddComponent<LineRenderer>();
        laserLine.startWidth = 0.003f;
        laserLine.endWidth = 0.001f;
        laserLine.positionCount = 2;
        laserLine.useWorldSpace = true;

        Shader shader = Shader.Find("UI/Default");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.color = new Color(0.5f, 0.8f, 1f, 0.8f);
            laserLine.material = mat;
        }

        laserLine.startColor = new Color(0.5f, 0.8f, 1f, 0.8f);
        laserLine.endColor = new Color(0.5f, 0.8f, 1f, 0.2f);
    }

    void Update()
    {
        if (laserLine != null && headTransform != null)
        {
            Vector3 start = headTransform.position;
            Vector3 direction = headTransform.forward;
            Vector3 end = start + direction * 10f;

            RaycastHit hit;
            if (Physics.Raycast(start, direction, out hit, 10f))
            {
                end = hit.point;
            }

            laserLine.SetPosition(0, start);
            laserLine.SetPosition(1, end);
        }
    }
}

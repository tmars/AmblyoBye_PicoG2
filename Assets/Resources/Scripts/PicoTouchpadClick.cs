using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Bridges Pico G2 touchpad to Unity EventSystem.
/// Handles both button clicks and slider adjustments via gaze.
/// </summary>
public class PicoTouchpadClick : MonoBehaviour
{
    void Update()
    {
        // Headset body button (Escape) OR controller touchpad
        bool touchpadPressed = Input.GetKeyDown(KeyCode.Escape);

#if !UNITY_EDITOR && UNITY_ANDROID
        try
        {
            touchpadPressed = touchpadPressed || Pvr_UnitySDKAPI.Controller.UPvr_GetKeyDown(0, Pvr_UnitySDKAPI.Pvr_KeyCode.TOUCHPAD);
            if (!touchpadPressed)
                touchpadPressed = Pvr_UnitySDKAPI.Controller.UPvr_GetKeyDown(1, Pvr_UnitySDKAPI.Pvr_KeyCode.TOUCHPAD);
        }
        catch { }
#else
        touchpadPressed = touchpadPressed || Input.GetKeyDown(KeyCode.Space);
#endif

        if (touchpadPressed)
        {
            var currentObj = Pvr_GazeInputModule.gazeGameObject;
            if (currentObj != null)
            {
                var ped = new PointerEventData(EventSystem.current);
                ped.position = new Vector2(Screen.width / 2f, Screen.height / 2f);
                ped.pressPosition = ped.position;
                ped.button = PointerEventData.InputButton.Left;

                // Copy raycast info from GazeInputModule for correct screen-to-world conversion
                var gazeModule = FindObjectOfType<Pvr_GazeInputModule>();
                if (gazeModule != null)
                {
                    ped.pointerCurrentRaycast = gazeModule.CurrentRaycast;
                    ped.pointerPressRaycast = gazeModule.CurrentRaycast;
                }

                // Check if it's a slider — send pointer down/up sequence
                var slider = currentObj.GetComponentInParent<Slider>();
                if (slider != null)
                {
                    ExecuteEvents.ExecuteHierarchy(currentObj, ped, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.ExecuteHierarchy(currentObj, ped, ExecuteEvents.pointerUpHandler);
                }

                // Always send click (for buttons)
                ExecuteEvents.ExecuteHierarchy(currentObj, ped, ExecuteEvents.pointerClickHandler);
            }
        }
    }
}

using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// +/- button control for a Slider value.
/// Attached to the container, wired to Increment/Decrement by buttons.
/// </summary>
public class SliderPlusMinus : MonoBehaviour
{
    public Slider slider;
    public float step = 0.1f;
    public TextMeshProUGUI valueText;
    public string format = "0.00";

    void Start()
    {
        UpdateText();
    }

    public void Increment()
    {
        if (slider == null) return;
        slider.value = Mathf.Min(slider.value + step, slider.maxValue);
        UpdateText();
    }

    public void Decrement()
    {
        if (slider == null) return;
        slider.value = Mathf.Max(slider.value - step, slider.minValue);
        UpdateText();
    }

    public void UpdateText()
    {
        if (valueText != null && slider != null)
            valueText.text = slider.value.ToString(format);
    }
}

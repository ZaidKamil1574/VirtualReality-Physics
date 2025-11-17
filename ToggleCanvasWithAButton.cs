using UnityEngine;
using UnityEngine.XR;

public class ToggleCanvasWithAButton : MonoBehaviour
{
    [Header("Canvas to Toggle")]
    public GameObject targetCanvas;

    private InputDevice rightHand;
    private bool prevAButtonState = false;

    void Start()
    {
        // Initialize right hand input device
        rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    void Update()
    {
        // Reacquire device if disconnected
        if (!rightHand.isValid)
            rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        // Check A button press
        if (rightHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool aPressed))
        {
            // Detect button press (not hold)
            if (aPressed && !prevAButtonState)
            {
                if (targetCanvas != null)
                {
                    bool newState = !targetCanvas.activeSelf;
                    targetCanvas.SetActive(newState);
                    Debug.Log($"🎛 Canvas {(newState ? "Enabled" : "Disabled")} via A Button");
                }
            }

            prevAButtonState = aPressed;
        }
    }
}
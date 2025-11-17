using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

public class TogglePanelWithYButton : MonoBehaviour
{
    [Header("UI Panel to Toggle (child of a Canvas)")]
    public GameObject targetPanel;

    private InputDevice leftHand;
    private bool prevYState;

    void OnEnable()
    {
        InputDevices.deviceConnected += OnDeviceConnected;
        InputDevices.deviceDisconnected += OnDeviceDisconnected;
        AcquireLeftHand();
    }

    void OnDisable()
    {
        InputDevices.deviceConnected -= OnDeviceConnected;
        InputDevices.deviceDisconnected -= OnDeviceDisconnected;
    }

    void Update()
    {
        if (!leftHand.isValid)
        {
            AcquireLeftHand();
            return;
        }

        if (leftHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bool yPressed))
        {
            if (yPressed && !prevYState)
                Toggle();

            prevYState = yPressed;
        }
    }

    private void Toggle()
    {
        if (targetPanel == null)
        {
            Debug.LogWarning("[Y-Toggle] targetPanel not assigned.");
            return;
        }

        // If the parent Canvas is disabled, you won't see the Panel—toggle that instead.
        if (!targetPanel.transform.root.gameObject.activeInHierarchy)
        {
            targetPanel.transform.root.gameObject.SetActive(true);
        }

        bool newState = !targetPanel.activeSelf;
        targetPanel.SetActive(newState);
        Debug.Log($"[Y-Toggle] Panel {(newState ? "ENABLED" : "DISABLED")} via Y.");
    }

    private void OnDeviceConnected(InputDevice device)
    {
        if ((device.characteristics & (InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left)) != 0)
            leftHand = device;
    }

    private void OnDeviceDisconnected(InputDevice device)
    {
        if (device == leftHand) leftHand = default;
    }

    private void AcquireLeftHand()
    {
        var list = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left, list);
        leftHand = list.Count > 0 ? list[0] : default;
    }
}

using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class VRKeyboardManager : MonoBehaviour
{
    private TouchScreenKeyboard keyboard;
    private TMP_InputField currentField;

    [Header("Auto-wire")]
    [SerializeField] List<TMP_InputField> fieldsToWire = new(); // drag fields here OR
    [SerializeField] bool autoFindInChildren = true;            // let it scan children at runtime

    void Awake()
    {
        // Find all TMP_InputFields under this object if autoFind is on
        if (autoFindInChildren)
            fieldsToWire.AddRange(GetComponentsInChildren<TMP_InputField>(true));

        // Hook events so selecting a field opens the keyboard
        foreach (var f in fieldsToWire)
        {
            var field = f; // capture
            field.contentType = TMP_InputField.ContentType.DecimalNumber;
            field.lineType = TMP_InputField.LineType.SingleLine;

            field.onSelect.AddListener(_ => OpenForField(field));
            field.onDeselect.AddListener(_ => CloseIfField(field));
        }
    }

    private void OpenForField(TMP_InputField field)
    {
        currentField = field;
        keyboard = TouchScreenKeyboard.Open(field.text, TouchScreenKeyboardType.DecimalPad);
    }

    void Update()
    {
        if (keyboard == null || currentField == null) return;

        var status = keyboard.status;
        if (status == TouchScreenKeyboard.Status.Done || status == TouchScreenKeyboard.Status.Canceled)
        {
            keyboard = null;
            currentField = null;
            return;
        }

        currentField.text = keyboard.text;
    }

    private void CloseIfField(TMP_InputField field)
    {
        if (currentField == field)
        {
            keyboard = null;
            currentField = null;
        }
    }
}

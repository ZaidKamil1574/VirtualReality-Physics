using UnityEngine;

public class NotepadManager : MonoBehaviour
{
    public GameObject notepadCanvas;

    private bool isVisible = false;

    public void ToggleNotepad()
    {
        isVisible = !isVisible;
        notepadCanvas.SetActive(isVisible);
    }
}

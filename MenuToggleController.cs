using UnityEngine;

public class MenuToggleController : MonoBehaviour
{
    public GameObject menuToToggle;  // Assign your Blue Menu (BoxCanvas)

    private bool isMenuVisible = true;

    public void ToggleMenu()
    {
        isMenuVisible = !isMenuVisible;
        if (menuToToggle != null)
        {
            menuToToggle.SetActive(isMenuVisible);
        }
    }
}

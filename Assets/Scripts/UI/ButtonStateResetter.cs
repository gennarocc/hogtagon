using UnityEngine;
using UnityEngine.UI;

public class ButtonStateResetter : MonoBehaviour
{
    private Button[] buttons;

    private void Awake()
    {
        Debug.Log($"ButtonStateResetter Awake on {gameObject.name}");
        // Get all buttons in this panel
        buttons = GetComponentsInChildren<Button>(true);
        if (buttons != null && buttons.Length > 0)
        {
            Debug.Log($"Found {buttons.Length} buttons in {gameObject.name}");
        }
        else
        {
            Debug.LogWarning($"No buttons found in {gameObject.name}");
        }
    }

    public void ResetAllButtonStates()
    {
        // Check if buttons array is initialized
        if (buttons == null)
        {
            Debug.LogWarning($"Buttons array is null in {gameObject.name}, attempting to find buttons");
            buttons = GetComponentsInChildren<Button>(true);
        }

        // If still null or empty, return
        if (buttons == null || buttons.Length == 0)
        {
            Debug.LogWarning($"No buttons found in {gameObject.name}, cannot reset states");
            return;
        }

        foreach (Button button in buttons)
        {
            if (button != null)
            {
                // Only reset animation if the button has an animator
                if (button.animator != null)
                {
                    button.animator.Rebind();
                    button.animator.Update(0f);
                }

                // Deselect the button
                button.OnDeselect(null);
            }
        }
    }
} 
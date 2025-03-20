using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ButtonStateResetter : MonoBehaviour
{
    [SerializeField] private Button[] buttonsToReset;

    private void OnEnable()
    {
        // Can optionally collect buttons automatically
        if (buttonsToReset.Length == 0)
        {
            buttonsToReset = GetComponentsInChildren<Button>(true);
        }
    }

    public void ResetAllButtonStates()
    {
        foreach (Button button in buttonsToReset)
        {
            if (button == null) continue;
            
            // Make sure button is interactive
            button.interactable = true;
            
            // Force transition to normal state
            button.OnPointerExit(new PointerEventData(EventSystem.current));
            
            // Reset selection state in EventSystem
            if (EventSystem.current.currentSelectedGameObject == button.gameObject)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
            
            // Force the animator to go to normal state if using animator
            Animator animator = button.GetComponent<Animator>();
            if (animator != null)
            {
                animator.Play("Normal");
            }
            
            // Reset any MenuButtonHighlight component
            MenuButtonHighlight highlight = button.GetComponent<MenuButtonHighlight>();
            if (highlight != null)
            {
                highlight.ForceUnhighlightButton();
            }
        }
        
        // Clear any selection in the event system
        EventSystem.current.SetSelectedGameObject(null);
    }
}
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
        }
    }
}
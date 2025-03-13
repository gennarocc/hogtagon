using UnityEngine;

public abstract class BasePanel : MonoBehaviour
{
    protected InputManager inputManager;
    protected ButtonStateResetter buttonStateResetter;

    protected virtual void Awake()
    {
        inputManager = InputManager.Instance;
        buttonStateResetter = GetComponent<ButtonStateResetter>();
    }

    public virtual void Show()
    {
        gameObject.SetActive(true);
        OnPanelShown();
    }

    public virtual void Hide()
    {
        if (buttonStateResetter != null)
            buttonStateResetter.ResetAllButtonStates();
        
        OnPanelHidden();
        gameObject.SetActive(false);
    }

    protected virtual void OnPanelShown() { }
    protected virtual void OnPanelHidden() { }

    protected virtual void Update() { }

    public virtual void ResetAllButtonStates()
    {
        if (buttonStateResetter != null)
            buttonStateResetter.ResetAllButtonStates();
    }
} 
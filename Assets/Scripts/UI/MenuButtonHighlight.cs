using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;

[RequireComponent(typeof(Button))]
public class MenuButtonHighlight : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [Header("Highlight Settings")]
    [SerializeField] private Color normalColor = new Color(0.0f, 0.8f, 0.0f, 0.7f);
    [SerializeField] private Color highlightedColor = new Color(0.0f, 1.0f, 0.0f, 1.0f);
    [SerializeField] private float outlineWidth = 2f;
    [SerializeField] private float animationSpeed = 5f;
    [SerializeField] private bool showHighlightArrow = true;  // New field to toggle highlight arrow

    // Create a separate GameObjects for the outline borders
    private GameObject topBorder;
    private GameObject rightBorder;
    private GameObject bottomBorder;
    private GameObject leftBorder;
    private GameObject highlightArrow;  // New reference for the highlight arrow

    private Button button;
    private TextMeshProUGUI buttonText;
    private Image buttonImage;
    private bool isHighlighted = false;
    private static MenuButtonHighlight currentlyHighlighted = null;
    private static Color sharedNormalColor = new Color(0.0f, 0.8f, 0.0f, 0.7f); // Ensure all buttons use this exact same color

    private void Awake()
    {
        button = GetComponent<Button>();
        buttonText = GetComponentInChildren<TextMeshProUGUI>();
        buttonImage = GetComponent<Image>();

        // Override the inspector-set color with the shared static color to ensure consistency
        normalColor = sharedNormalColor;

        // Make button background completely transparent
        if (buttonImage != null)
        {
            // Change button to use transparent background
            Color buttonColor = buttonImage.color;
            buttonColor.a = 0.0f; // Fully transparent
            buttonImage.color = buttonColor;
        }

        // Create four border GameObjects for the outline
        CreateBorders();

        // Create highlight arrow if enabled
        if (showHighlightArrow)
        {
            CreateHighlightArrow();
        }

        // Set borders and arrow to invisible by default
        SetBordersVisible(false);
        if (highlightArrow != null)
        {
            highlightArrow.SetActive(false);
        }

        // Set initial text color
        if (buttonText != null)
        {
            // Force exact same color for all buttons
            buttonText.color = sharedNormalColor;
            buttonText.fontStyle = FontStyles.Normal;

            // Force material to be correct
            if (buttonText.fontSharedMaterial != null)
            {
                // Ensure we're not using different font materials
                Material fontMat = buttonText.fontSharedMaterial;
                buttonText.fontMaterial = fontMat;
            }
        }

        // Update the button colors to use transparency
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(1, 1, 1, 0.0f); // Fully transparent
        colors.highlightedColor = new Color(1, 1, 1, 0.0f); // Fully transparent
        colors.selectedColor = new Color(1, 1, 1, 0.0f); // Fully transparent 
        colors.pressedColor = new Color(1, 1, 1, 0.0f); // Fully transparent
        button.colors = colors;
    }

    private void Start()
    {
        // Ensure all buttons start in non-highlighted state
        StartCoroutine(ResetAllButtonsDelayed());
    }

    private IEnumerator ResetAllButtonsDelayed()
    {
        // Wait for two frames to ensure all UI elements are initialized
        yield return null;
        yield return null;

        // Reset all buttons
        MenuButtonHighlight[] allButtons = FindObjectsByType<MenuButtonHighlight>(FindObjectsSortMode.None);
        foreach (MenuButtonHighlight btn in allButtons)
        {
            btn.ForceUnhighlightButton();
        }

        // Clear any selection (this is important to prevent Unity from auto-selecting)
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        // Wait another frame then normalize all colors again
        yield return null;
        NormalizeAllButtonColors();
    }

    // Static method to ensure all buttons have exactly the same color
    public static void NormalizeAllButtonColors()
    {
        MenuButtonHighlight[] allButtons = FindObjectsByType<MenuButtonHighlight>(FindObjectsSortMode.None);
        foreach (MenuButtonHighlight btn in allButtons)
        {
            if (!btn.isHighlighted && btn.buttonText != null)
            {
                // Force exact same shared color
                btn.buttonText.color = sharedNormalColor;
            }
        }
    }

    private void OnEnable()
    {
        // Reset to non-highlighted state when enabled
        ForceUnhighlightButton();
    }

    private void OnDisable()
    {
        // Make sure we don't stay as the currently highlighted button if disabled
        if (currentlyHighlighted == this)
        {
            currentlyHighlighted = null;
        }
    }

    private void CreateBorders()
    {
        // Get the RectTransform of the button
        RectTransform rt = GetComponent<RectTransform>();

        // TOP BORDER
        topBorder = new GameObject("TopBorder");
        topBorder.transform.SetParent(transform, false);
        RectTransform topRT = topBorder.AddComponent<RectTransform>();
        topRT.anchorMin = new Vector2(0, 1);
        topRT.anchorMax = new Vector2(1, 1);
        topRT.pivot = new Vector2(0.5f, 1f);
        topRT.sizeDelta = new Vector2(0, outlineWidth);
        topRT.anchoredPosition = new Vector2(0, 0);
        Image topImage = topBorder.AddComponent<Image>();
        topImage.color = new Color(0, 0, 0, 0); // Start transparent

        // RIGHT BORDER
        rightBorder = new GameObject("RightBorder");
        rightBorder.transform.SetParent(transform, false);
        RectTransform rightRT = rightBorder.AddComponent<RectTransform>();
        rightRT.anchorMin = new Vector2(1, 0);
        rightRT.anchorMax = new Vector2(1, 1);
        rightRT.pivot = new Vector2(1f, 0.5f);
        rightRT.sizeDelta = new Vector2(outlineWidth, 0);
        rightRT.anchoredPosition = new Vector2(0, 0);
        Image rightImage = rightBorder.AddComponent<Image>();
        rightImage.color = new Color(0, 0, 0, 0); // Start transparent

        // BOTTOM BORDER
        bottomBorder = new GameObject("BottomBorder");
        bottomBorder.transform.SetParent(transform, false);
        RectTransform bottomRT = bottomBorder.AddComponent<RectTransform>();
        bottomRT.anchorMin = new Vector2(0, 0);
        bottomRT.anchorMax = new Vector2(1, 0);
        bottomRT.pivot = new Vector2(0.5f, 0f);
        bottomRT.sizeDelta = new Vector2(0, outlineWidth);
        bottomRT.anchoredPosition = new Vector2(0, 0);
        Image bottomImage = bottomBorder.AddComponent<Image>();
        bottomImage.color = new Color(0, 0, 0, 0); // Start transparent

        // LEFT BORDER
        leftBorder = new GameObject("LeftBorder");
        leftBorder.transform.SetParent(transform, false);
        RectTransform leftRT = leftBorder.AddComponent<RectTransform>();
        leftRT.anchorMin = new Vector2(0, 0);
        leftRT.anchorMax = new Vector2(0, 1);
        leftRT.pivot = new Vector2(0f, 0.5f);
        leftRT.sizeDelta = new Vector2(outlineWidth, 0);
        leftRT.anchoredPosition = new Vector2(0, 0);
        Image leftImage = leftBorder.AddComponent<Image>();
        leftImage.color = new Color(0, 0, 0, 0); // Start transparent
    }

    private void CreateHighlightArrow()
    {
        // Create arrow GameObject
        highlightArrow = new GameObject("HighlightArrow");
        highlightArrow.transform.SetParent(transform, false);

        // Set up RectTransform
        RectTransform arrowRT = highlightArrow.AddComponent<RectTransform>();
        arrowRT.anchorMin = new Vector2(0, 0.5f);
        arrowRT.anchorMax = new Vector2(0, 0.5f);
        arrowRT.pivot = new Vector2(1f, 0.5f);
        arrowRT.sizeDelta = new Vector2(20, 20); // Size of the arrow
        arrowRT.anchoredPosition = new Vector2(-10, 0); // Position to the left of the text

        // Add TextMeshProUGUI component for the arrow
        TextMeshProUGUI arrowText = highlightArrow.AddComponent<TextMeshProUGUI>();
        arrowText.text = ">";  // Use ">" as the arrow
        arrowText.fontSize = 24;
        arrowText.color = normalColor;
        arrowText.alignment = TextAlignmentOptions.Center;
    }

    private void SetBordersVisible(bool visible)
    {
        Color color = visible ? highlightedColor : new Color(0, 0, 0, 0);

        if (topBorder != null) topBorder.GetComponent<Image>().color = color;
        if (rightBorder != null) rightBorder.GetComponent<Image>().color = color;
        if (bottomBorder != null) bottomBorder.GetComponent<Image>().color = color;
        if (leftBorder != null) leftBorder.GetComponent<Image>().color = color;

        // Update arrow visibility if it exists
        if (highlightArrow != null && showHighlightArrow)
        {
            highlightArrow.SetActive(visible);
            if (visible)
            {
                highlightArrow.GetComponent<TextMeshProUGUI>().color = highlightedColor;
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Only highlight if the button is interactable
        if (button != null && button.interactable)
        {
            HighlightButton();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // Only unhighlight if not currently selected and if the button is interactable
        if (button != null && button.interactable && EventSystem.current.currentSelectedGameObject != gameObject)
        {
            UnhighlightButton();
        }
    }

    public void OnSelect(BaseEventData eventData)
    {
        // Only highlight if the button is interactable
        if (button != null && button.interactable)
        {
            HighlightButton();
        }
    }


    public void OnDeselect(BaseEventData eventData)
    {
        UnhighlightButton();
    }

    private void HighlightButton()
    {
        // If there's a currently highlighted button that isn't this one, unhighlight it
        if (currentlyHighlighted != null && currentlyHighlighted != this)
        {
            currentlyHighlighted.UnhighlightButton();
        }

        // Set this as the currently highlighted button
        currentlyHighlighted = this;
        isHighlighted = true;

        // Change text color and style
        if (buttonText != null)
        {
            buttonText.color = highlightedColor;
            buttonText.fontStyle = FontStyles.Bold;
        }

        // Show outline on hover/selection
        SetBordersVisible(true);

        // Now normalize all other buttons to ensure consistency
        NormalizeAllButtonColors();
    }

    public void UnhighlightButton()
    {
        // Only clear currentlyHighlighted if it's this button
        if (currentlyHighlighted == this)
        {
            currentlyHighlighted = null;
        }

        isHighlighted = false;

        // Reset text color and style
        if (buttonText != null)
        {
            // Use the shared color value to ensure absolute consistency
            buttonText.color = sharedNormalColor;
            buttonText.fontStyle = FontStyles.Normal;
        }

        // Hide outline when not highlighted
        SetBordersVisible(false);

        // Now normalize all buttons to ensure consistency
        NormalizeAllButtonColors();
    }

    // Use this method when we want to force unhighlight
    public void ForceUnhighlightButton()
    {
        // Avoid deselecting a button if it's the current UI selection
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        UnhighlightButton();
    }

    private void Update()
    {
        // Animate the outline if button is highlighted
        if (isHighlighted)
        {
            // Pulse effect on the outline
            float pulse = Mathf.Sin(Time.time * animationSpeed) * 0.5f + 0.5f;
            Color pulseColor = highlightedColor;
            pulseColor.a = Mathf.Lerp(0.7f, 1.0f, pulse);

            if (topBorder != null) topBorder.GetComponent<Image>().color = pulseColor;
            if (rightBorder != null) rightBorder.GetComponent<Image>().color = pulseColor;
            if (bottomBorder != null) bottomBorder.GetComponent<Image>().color = pulseColor;
            if (leftBorder != null) leftBorder.GetComponent<Image>().color = pulseColor;
        }

        // Double-check text color if not highlighted (to ensure consistent alpha)
        if (!isHighlighted && buttonText != null)
        {
            // Direct comparison of color values to force equality and avoid precision issues
            if (!ColorEquals(buttonText.color, sharedNormalColor, 0.01f))
            {
                buttonText.color = sharedNormalColor; // Ensure exact color match
            }
        }

        // Safety check - if this button appears highlighted but isn't currentlyHighlighted, fix it
        if (isHighlighted && currentlyHighlighted != this)
        {
            ForceUnhighlightButton();
        }
    }

    // Helper method for more reliable color comparison
    private bool ColorEquals(Color a, Color b, float tolerance = 0.0001f)
    {
        return Mathf.Abs(a.r - b.r) < tolerance &&
               Mathf.Abs(a.g - b.g) < tolerance &&
               Mathf.Abs(a.b - b.b) < tolerance &&
               Mathf.Abs(a.a - b.a) < tolerance;
    }
}
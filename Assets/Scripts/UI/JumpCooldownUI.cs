using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class JumpCooldownUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image cooldownFill; // Circular fill image for cooldown
    [SerializeField] private TextMeshProUGUI cooldownText; // Optional text display
    [SerializeField] private GameObject cooldownPanel; // Parent panel for cooldown UI
    
    [Header("Colors")]
    [SerializeField] private Color readyColor = Color.green;
    [SerializeField] private Color cooldownColor = Color.red;
    
    // Reference to player's HogController
    private HogController playerHogController;
    
    private void Start()
    {
        // Find the local player's HogController
        if (playerHogController == null)
        {
            Player localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject?.GetComponent<Player>();
            if (localPlayer != null)
            {
                playerHogController = localPlayer.GetComponentInChildren<HogController>();
            }
        }
        
        // Initialize UI
        UpdateCooldownUI(false, 0, 1);
    }
    
    private void Update()
    {
        if (playerHogController == null)
        {
            // Try to find the controller again if it's not set
            Player localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject?.GetComponent<Player>();
            if (localPlayer != null)
            {
                playerHogController = localPlayer.GetComponentInChildren<HogController>();
            }
            
            if (playerHogController == null) return;
        }
        
        // Get cooldown info from HogController
        bool onCooldown = playerHogController.JumpOnCooldown;
        float remaining = playerHogController.JumpCooldownRemaining;
        float total = playerHogController.JumpCooldownTotal;
        
        // Update UI
        UpdateCooldownUI(onCooldown, remaining, total);
    }
    
    private void UpdateCooldownUI(bool onCooldown, float remaining, float total)
    {
        // Show/hide the cooldown panel based on state
        if (cooldownPanel != null)
        {
            cooldownPanel.SetActive(true); // Always show, but could toggle visibility
        }
        
        // Update the fill amount
        if (cooldownFill != null)
        {
            if (onCooldown)
            {
                // Fill amount goes from 0 to 1 as cooldown completes
                cooldownFill.fillAmount = 1 - (remaining / total);
                cooldownFill.color = cooldownColor;
            }
            else
            {
                // When ready, show full
                cooldownFill.fillAmount = 1;
                cooldownFill.color = readyColor;
            }
        }
        
        // Update text if needed
        if (cooldownText != null)
        {
            if (onCooldown)
            {
                cooldownText.text = Mathf.Ceil(remaining).ToString();
                cooldownText.color = cooldownColor;
            }
            else
            {
                cooldownText.text = "JUMP!";
                cooldownText.color = readyColor;
            }
        }
    }
}
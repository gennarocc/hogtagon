using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class KillFeed : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private int maxMessages = 5;
    [SerializeField] private float messageDuration = 5f;
    [SerializeField] private GameObject killFeedItemPrefab;
    [SerializeField] private Transform killFeedContainer;

    private Queue<GameObject> activeMessages = new Queue<GameObject>();

    // Singleton pattern for easy access
    public static KillFeed Instance { get; private set; }

    private void Awake()
    {
        // Setup singleton instance
        Instance = this;
    }

    public void AddKillMessage(string killerName, string victimName)
    {
        if (killFeedItemPrefab == null || killFeedContainer == null)
        {
            Debug.LogError("KillFeed: Missing prefab or container reference");
            return;
        }

        GameObject messageObj = Instantiate(killFeedItemPrefab, killFeedContainer);
        TextMeshProUGUI messageText = messageObj.GetComponentInChildren<TextMeshProUGUI>();
        
        if (messageText != null)
        {
            // Format: "Killer killed Victim"
            messageText.text = $"{killerName} killed {victimName}";
        }

        // Add to queue and manage the max number of messages
        activeMessages.Enqueue(messageObj);
        if (activeMessages.Count > maxMessages)
        {
            GameObject oldestMessage = activeMessages.Dequeue();
            Destroy(oldestMessage);
        }

        // Automatically remove after duration
        StartCoroutine(RemoveMessageAfterDuration(messageObj));
    }

    public void AddSuicideMessage(string playerName)
    {
        if (killFeedItemPrefab == null || killFeedContainer == null)
        {
            Debug.LogError("KillFeed: Missing prefab or container reference");
            return;
        }

        GameObject messageObj = Instantiate(killFeedItemPrefab, killFeedContainer);
        TextMeshProUGUI messageText = messageObj.GetComponentInChildren<TextMeshProUGUI>();
        
        if (messageText != null)
        {
            // Format: "Player killed themselves"
            messageText.text = $"{playerName} killed themselves";
        }

        // Add to queue and manage the max number of messages
        activeMessages.Enqueue(messageObj);
        if (activeMessages.Count > maxMessages)
        {
            GameObject oldestMessage = activeMessages.Dequeue();
            Destroy(oldestMessage);
        }

        // Automatically remove after duration
        StartCoroutine(RemoveMessageAfterDuration(messageObj));
    }

    private IEnumerator RemoveMessageAfterDuration(GameObject message)
    {
        yield return new WaitForSeconds(messageDuration);
        
        if (activeMessages.Contains(message))
        {
            // Convert to list, remove the message, and recreate the queue
            List<GameObject> messagesList = activeMessages.ToList();
            messagesList.Remove(message);
            activeMessages = new Queue<GameObject>(messagesList);
            
            Destroy(message);
        }
    }
} 
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Hogtagon.Core.Infrastructure;

public class KillFeed : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private int maxMessages = 5;
    [SerializeField] private float messageDuration = 5f;
    [SerializeField] private GameObject killFeedItemPrefab;
    [SerializeField] private Transform killFeedContainer;

    [Header("Colors")]
    [SerializeField] private Color backgroundColor = new Color(0, 0, 0, 0.5f);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color killTextColor = new Color(1f, 0.2f, 0.2f);
    [SerializeField] private Color suicideTextColor = new Color(1f, 0.92f, 0.016f);
    [SerializeField] private Color victimTextColor = new Color(0.7f, 0.7f, 0.7f);

    private static readonly string[] killMessages = {
        "DEMOLISHED",
        "EVISCERATED",
        "CLOBBERED",
        "OBLITERATED",
        "ANNIHILATED",
        "PULVERIZED",
        "DECIMATED"
    };

    private static readonly string[] suicideMessages = {
        "took themselves out!",
        "couldn't handle the pressure!",
        "chose the easy way out!",
        "discovered gravity!",
        "made a fatal mistake!",
        "failed spectacularly!"
    };

    // Instance management
    private static KillFeed instance;
    
    // Message tracking
    private Queue<GameObject> activeMessages = new();
    private GameObject lastKillMessage;
    private HashSet<ulong> processedMessageIds = new();
    private ulong currentMessageId;

    // State
    private bool isPaused;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Multiple KillFeed instances detected. Destroying duplicate.");
            Destroy(gameObject);
            return;
        }
        instance = this;
        ServiceLocator.RegisterService<KillFeed>(this);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Debug.Log($"KillFeed OnNetworkSpawn - IsServer: {IsServer}, IsClient: {IsClient}");
        ClearAllMessages();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (instance == this)
        {
            instance = null;
            ServiceLocator.UnregisterService<KillFeed>();
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
            ServiceLocator.UnregisterService<KillFeed>();
        }
    }

    // Public methods for GameManager to control kill feed state
    public void Pause()
    {
        ClearAllMessages();
    }

    public void PauseAndKeepLastMessage()
    {
        ClearAllMessagesExceptLast();
    }

    public void ResetForNewRound()
    {
        currentMessageId = 0;
        ClearAllMessages();
    }

    private void ClearAllMessages()
    {
        Debug.Log($"KillFeed: Clearing all messages - ActiveCount: {activeMessages.Count}");
        
        foreach (var message in activeMessages)
        {
            if (message != null)
            {
                Destroy(message);
            }
        }
        activeMessages.Clear();
        lastKillMessage = null;
        processedMessageIds.Clear();
    }

    private void ClearAllMessagesExceptLast()
    {
        if (activeMessages.Count <= 0) return;
        
        lastKillMessage = activeMessages.Last();
        while (activeMessages.Count > 1)
        {
            var message = activeMessages.Dequeue();
            if (message != null && message != lastKillMessage)
            {
                Destroy(message);
            }
        }
    }

    public void AddKillMessage(string killerName, string victimName)
    {
        // Only server should generate and broadcast messages
        if (!IsServer) return;

        string killMessage = killMessages[Random.Range(0, killMessages.Length)];
        string messageText = FormatKillMessage(killerName, killMessage, victimName);
        
        // Server generates the message and broadcasts to all clients (including itself)
        DisplayMessageClientRpc(messageText, currentMessageId++);
    }

    public void AddSuicideMessage(string playerName)
    {
        // Only server should generate and broadcast messages
        if (!IsServer) return;

        string suicideMessage = suicideMessages[Random.Range(0, suicideMessages.Length)];
        string messageText = FormatSuicideMessage(playerName, suicideMessage);
        
        // Server generates the message and broadcasts to all clients (including itself)
        DisplayMessageClientRpc(messageText, currentMessageId++);
    }

    private string FormatKillMessage(string killerName, string killMessage, string victimName)
    {
        return $"<color=#{ColorUtility.ToHtmlStringRGB(textColor)}>{killerName}</color> " +
               $"<color=#{ColorUtility.ToHtmlStringRGB(killTextColor)}>{killMessage}</color> " +
               $"<color=#{ColorUtility.ToHtmlStringRGB(victimTextColor)}>{victimName}</color>";
    }

    private string FormatSuicideMessage(string playerName, string suicideMessage)
    {
        return $"<color=#{ColorUtility.ToHtmlStringRGB(suicideTextColor)}>{playerName}</color> " +
               $"<color=#{ColorUtility.ToHtmlStringRGB(textColor)}>{suicideMessage}</color>";
    }

    [ClientRpc]
    private void DisplayMessageClientRpc(string messageText, ulong messageId)
    {
        // Add debug logging to track message processing
        Debug.Log($"KillFeed: Received message - ID: {messageId}, IsProcessed: {processedMessageIds.Contains(messageId)}, IsServer: {IsServer}, IsHost: {IsHost}");

        if (processedMessageIds.Contains(messageId))
        {
            Debug.Log($"KillFeed: Skipping duplicate message {messageId}");
            return;
        }

        CreateMessage(messageText, messageId);
    }

    private void CreateMessage(string messageText, ulong messageId)
    {
        if (!killFeedItemPrefab || !killFeedContainer)
        {
            Debug.LogWarning("KillFeed: Cannot add message - missing references");
            return;
        }

        var messageObj = Instantiate(killFeedItemPrefab, killFeedContainer);
        var textComponent = messageObj.GetComponentInChildren<TextMeshProUGUI>();
        if (textComponent == null)
        {
            Debug.LogError("KillFeed: TextMeshProUGUI component not found in prefab!");
            Destroy(messageObj);
            return;
        }

        ConfigureMessageObject(messageObj, textComponent, messageText);
        ManageMessageQueue(messageObj, messageId);
    }

    private void ConfigureMessageObject(GameObject messageObj, TextMeshProUGUI textComponent, string messageText)
    {
        textComponent.text = messageText;
        textComponent.fontSize = 24;
        textComponent.alignment = TextAlignmentOptions.Center;

        if (textComponent.transform.parent.TryGetComponent<Image>(out var backgroundImage))
        {
            backgroundImage.color = backgroundColor;
        }
    }

    private void ManageMessageQueue(GameObject messageObj, ulong messageId)
    {
        lastKillMessage = messageObj;
        activeMessages.Enqueue(messageObj);
        processedMessageIds.Add(messageId);

        if (activeMessages.Count > maxMessages)
        {
            var oldestMessage = activeMessages.Dequeue();
            if (oldestMessage != null && oldestMessage != lastKillMessage)
            {
                Destroy(oldestMessage);
            }
        }

        StartCoroutine(RemoveMessageAfterDuration(messageObj, messageId));
    }

    private IEnumerator RemoveMessageAfterDuration(GameObject messageObj, ulong messageId)
    {
        if (messageObj == null) yield break;

        yield return new WaitForSeconds(messageDuration);

        if (messageObj != null)
        {
            if (messageObj == lastKillMessage)
            {
                lastKillMessage = null;
            }
            Destroy(messageObj);
            processedMessageIds.Remove(messageId);
        }
    }
}
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
    
    // Public accessor for the instance
    public static KillFeed Instance => instance;
    
    // Message tracking
    private Queue<GameObject> activeMessages = new();
    private ulong currentMessageId;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Multiple KillFeed instances detected. Destroying duplicate.");
            Destroy(gameObject);
            return;
        }
        
        instance = this;
        Debug.Log("[KillFeed] Registering KillFeed with ServiceLocator");
        ServiceLocator.RegisterService<KillFeed>(this);
        Debug.Log("[KillFeed] Registration complete with instance: " + GetInstanceID());
        
        // Validate required components
        if (killFeedItemPrefab == null)
        {
            Debug.LogError("[KillFeed] killFeedItemPrefab is not assigned!");
        }
        
        if (killFeedContainer == null)
        {
            Debug.LogError("[KillFeed] killFeedContainer is not assigned!");
        }
        else
        {
            Debug.Log($"[KillFeed] killFeedContainer found: {killFeedContainer.name}");
        }
    }

    // Ensure this object persists through scene changes
    private void Start()
    {
        // Don't destroy on load to maintain the KillFeed across scenes
        if (gameObject.scene.buildIndex != -1) // Only if not in a "DontDestroyOnLoad" scene
        {
            DontDestroyOnLoad(gameObject);
            Debug.Log("[KillFeed] Set to DontDestroyOnLoad");
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Debug.Log($"[KillFeed] OnNetworkSpawn - IsServer: {IsServer}, IsClient: {IsClient}, IsOwner: {IsOwner}");
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

    public override void OnDestroy()
    {
        // Call base implementation
        base.OnDestroy();
        
        // Unregister service
        if (instance == this)
        {
            instance = null;
            ServiceLocator.UnregisterService<KillFeed>();
        }
    }

    public void ResetForNewRound()
    {
        currentMessageId = 0;
        ClearAllMessages();
    }

    public void PauseAndKeepLastMessage()
    {
        ClearAllMessagesExceptLast();
    }

    private void ClearAllMessages()
    {
        foreach (var message in activeMessages)
        {
            if (message != null)
            {
                Destroy(message);
            }
        }
        activeMessages.Clear();
    }

    private void ClearAllMessagesExceptLast()
    {
        if (activeMessages.Count <= 0) return;
        
        var lastKillMessage = activeMessages.Last();
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
        // Only server should generate messages
        if (!IsServer)
        {
            Debug.LogWarning("KillFeed: Non-server tried to add kill message");
            return;
        }

        Debug.Log($"[KillFeed] AddKillMessage called: {killerName} killed {victimName}");

        // Generate a random kill message
        string killMessage = killMessages[Random.Range(0, killMessages.Length)];
        
        // Format the message
        string formattedMessage = FormatKillMessage(killerName, killMessage, victimName);
        
        // Send to all clients
        Debug.Log($"[KillFeed] Sending kill message ClientRpc: {formattedMessage}");
        DisplayMessageClientRpc(formattedMessage, currentMessageId++);
    }

    public void AddSuicideMessage(string playerName)
    {
        // Only server should generate messages
        if (!IsServer)
        {
            Debug.LogWarning("KillFeed: Non-server tried to add suicide message");
            return;
        }

        Debug.Log($"[KillFeed] AddSuicideMessage called: {playerName}");

        // Generate a random suicide message
        string suicideMessage = suicideMessages[Random.Range(0, suicideMessages.Length)];
        
        // Format the message
        string formattedMessage = FormatSuicideMessage(playerName, suicideMessage);
        
        // Send to all clients
        Debug.Log($"[KillFeed] Sending suicide message ClientRpc: {formattedMessage}");
        DisplayMessageClientRpc(formattedMessage, currentMessageId++);
    }

    private string FormatKillMessage(string killerName, string killMessage, string victimName)
    {
        // Find client IDs for the players
        ulong? killerClientId = FindClientIdByName(killerName);
        ulong? victimClientId = FindClientIdByName(victimName);
        
        // Get colored names (if client IDs found)
        string coloredKillerName = killerClientId.HasValue 
            ? ConnectionManager.Instance.GetPlayerColoredName(killerClientId.Value)
            : killerName;
            
        string coloredVictimName = victimClientId.HasValue
            ? ConnectionManager.Instance.GetPlayerColoredName(victimClientId.Value)
            : victimName;
        
        // Use colored names with white kill message
        return $"{coloredKillerName} {killMessage} {coloredVictimName}";
    }

    private string FormatSuicideMessage(string playerName, string suicideMessage)
    {
        // Find client ID for the player
        ulong? playerClientId = FindClientIdByName(playerName);
        
        // Get colored name (if client ID found)
        string coloredPlayerName = playerClientId.HasValue
            ? ConnectionManager.Instance.GetPlayerColoredName(playerClientId.Value)
            : playerName;
            
        // Use colored name with white suicide message
        return $"{coloredPlayerName} {suicideMessage}";
    }
    
    // Helper method to find client ID by player name
    private ulong? FindClientIdByName(string playerName)
    {
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (ConnectionManager.Instance.GetClientUsername(clientId) == playerName)
            {
                return clientId;
            }
        }
        return null;
    }

    [ClientRpc]
    private void DisplayMessageClientRpc(string messageText, ulong messageId)
    {
        Debug.Log($"[KillFeed] DisplayMessageClientRpc received on client: {messageText}");
        CreateMessage(messageText);
    }

    private void CreateMessage(string messageText)
    {
        if (!killFeedItemPrefab || !killFeedContainer)
        {
            Debug.LogWarning("KillFeed: Cannot add message - missing references");
            return;
        }

        Debug.Log($"[KillFeed] Creating message UI: {messageText}");

        var messageObj = Instantiate(killFeedItemPrefab, killFeedContainer);
        var textComponent = messageObj.GetComponentInChildren<TextMeshProUGUI>();
        if (textComponent == null)
        {
            Debug.LogError("KillFeed: TextMeshProUGUI component not found in prefab!");
            Destroy(messageObj);
            return;
        }

        ConfigureMessageObject(messageObj, textComponent, messageText);
        ManageMessageQueue(messageObj);
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

    private void ManageMessageQueue(GameObject messageObj)
    {
        activeMessages.Enqueue(messageObj);

        if (activeMessages.Count > maxMessages)
        {
            var oldestMessage = activeMessages.Dequeue();
            if (oldestMessage != null)
            {
                Destroy(oldestMessage);
            }
        }

        StartCoroutine(RemoveMessageAfterDuration(messageObj));
    }

    private IEnumerator RemoveMessageAfterDuration(GameObject messageObj)
    {
        if (messageObj == null) yield break;

        yield return new WaitForSeconds(messageDuration);

        if (messageObj != null)
        {
            Destroy(messageObj);
            
            // Remove from active messages if it still exists in the queue
            var messagesArray = activeMessages.ToArray();
            activeMessages.Clear();
            
            foreach (var msg in messagesArray)
            {
                if (msg != null && msg != messageObj)
                {
                    activeMessages.Enqueue(msg);
                }
            }
        }
    }

    // Method to handle player killed events
    public void HandlePlayerKilled(ulong killerClientId, string killerName, ulong victimClientId, string victimName)
    {
        if (killerClientId == victimClientId || killerClientId == 0)
        {
            // Suicide or environmental death
            AddSuicideMessage(victimName);
        }
        else
        {
            // Normal kill
            AddKillMessage(killerName, victimName);
        }
    }
}
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

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

    // Singleton pattern for easy access
    private static KillFeed instance;
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
        
        // Validate required components
        if (killFeedItemPrefab == null)
        {
            Debug.LogError("[KillFeed] killFeedItemPrefab is not assigned!");
        }
        
        if (killFeedContainer == null)
        {
            Debug.LogError("[KillFeed] killFeedContainer is not assigned!");
        }
    }

    // Ensure this object persists through scene changes
    private void Start()
    {
        // Don't destroy on load to maintain the KillFeed across scenes
        if (gameObject.scene.buildIndex != -1) // Only if not in a "DontDestroyOnLoad" scene
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ClearAllMessages();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (instance == this)
        {
            instance = null;
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (instance == this)
        {
            instance = null;
        }
    }

    public void ResetForNewRound()
    {
        currentMessageId = 0;
        ClearAllMessages();
    }

    public void ClearAllMessages()
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

    public void ClearAllMessagesExceptLast()
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

        // Generate a random kill message (only server selects the message)
        string killMessage = killMessages[Random.Range(0, killMessages.Length)];
        
        // Find client IDs for the players on the server side
        ulong? killerClientId = FindClientIdByName(killerName);
        ulong? victimClientId = FindClientIdByName(victimName);
        
        // Server sends the client IDs and the selected message to all clients
        DisplayKillMessageClientRpc(
            killerName, 
            killMessage, 
            victimName, 
            killerClientId.HasValue ? killerClientId.Value : 0, 
            victimClientId.HasValue ? victimClientId.Value : 0,
            killerClientId.HasValue,
            victimClientId.HasValue,
            currentMessageId++
        );
    }

    public void AddSuicideMessage(string playerName)
    {
        // Only server should generate messages
        if (!IsServer)
        {
            Debug.LogWarning("KillFeed: Non-server tried to add suicide message");
            return;
        }

        // Generate a random suicide message (only server selects the message)
        string suicideMessage = suicideMessages[Random.Range(0, suicideMessages.Length)];
        
        // Find client ID for the player on the server side
        ulong? playerClientId = FindClientIdByName(playerName);
        
        // Server sends the client ID and the selected message to all clients
        DisplaySuicideMessageClientRpc(
            playerName, 
            suicideMessage, 
            playerClientId.HasValue ? playerClientId.Value : 0,
            playerClientId.HasValue,
            currentMessageId++
        );
    }

    [ClientRpc]
    private void DisplayKillMessageClientRpc(
        string killerName, 
        string killMessage, 
        string victimName, 
        ulong killerClientId, 
        ulong victimClientId,
        bool hasKillerClientId,
        bool hasVictimClientId,
        ulong messageId)
    {
        // Format message on client side using the exact same message from the server
        string formattedMessage = FormatKillMessageWithIds(
            killerName, 
            killMessage, 
            victimName, 
            hasKillerClientId ? killerClientId : null,
            hasVictimClientId ? victimClientId : null
        );
        
        CreateMessage(formattedMessage);
    }

    [ClientRpc]
    private void DisplaySuicideMessageClientRpc(
        string playerName, 
        string suicideMessage, 
        ulong playerClientId,
        bool hasPlayerClientId,
        ulong messageId)
    {
        // Format message on client side using the exact same message from the server
        string formattedMessage = FormatSuicideMessageWithId(
            playerName, 
            suicideMessage, 
            hasPlayerClientId ? playerClientId : null
        );
        
        CreateMessage(formattedMessage);
    }

    private string FormatKillMessageWithIds(
        string killerName, 
        string killMessage, 
        string victimName, 
        ulong? killerClientId, 
        ulong? victimClientId)
    {
        // Get colored names based on client IDs
        string coloredKillerName;
        string coloredVictimName;
        
        if (killerClientId.HasValue)
        {
            coloredKillerName = ConnectionManager.Instance.GetPlayerColoredName(killerClientId.Value);
        }
        else
        {
            coloredKillerName = killerName;
        }
            
        if (victimClientId.HasValue)
        {
            coloredVictimName = ConnectionManager.Instance.GetPlayerColoredName(victimClientId.Value);
        }
        else
        {
            coloredVictimName = victimName;
        }
        
        // Use colored names with kill message
        return $"{coloredKillerName} {killMessage} {coloredVictimName}";
    }

    private string FormatSuicideMessageWithId(
        string playerName, 
        string suicideMessage, 
        ulong? playerClientId)
    {
        // Get colored name based on client ID
        string coloredPlayerName;
        
        if (playerClientId.HasValue)
        {
            coloredPlayerName = ConnectionManager.Instance.GetPlayerColoredName(playerClientId.Value);
        }
        else
        {
            coloredPlayerName = playerName;
        }
            
        // Use colored name with suicide message
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

    private void CreateMessage(string messageText)
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
        
        // Force settings that ensure rich text works
        textComponent.richText = true;
        textComponent.parseCtrlCharacters = true;
        
        // Now configure with the message
        ConfigureMessageObject(messageObj, textComponent, messageText);
        
        // Make sure the text component is enabled
        textComponent.enabled = true;
        
        ManageMessageQueue(messageObj);
    }

    private void ConfigureMessageObject(GameObject messageObj, TextMeshProUGUI textComponent, string messageText)
    {
        // Set text with rich text support enabled
        textComponent.text = messageText;
        textComponent.fontSize = 24;
        textComponent.alignment = TextAlignmentOptions.Center;
        
        // Ensure rich text is enabled to support color tags
        textComponent.richText = true;

        if (messageObj.TryGetComponent<Image>(out var backgroundImage))
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
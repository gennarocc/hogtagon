using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;
using UnityEngine.UI;
using Unity.Netcode;

public class Scoreboard : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject playerEntryPrefab;
    [SerializeField] private Transform playerListContainer;
    [SerializeField] private TextMeshProUGUI headerText;
    [SerializeField] private TMP_FontAsset technoFont; 

    [Header("Style Settings")]
    [SerializeField] private Color headerColor = new Color(0f, 1f, 0f); 
    [SerializeField] private Color rowBackgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f); 
    [SerializeField] private Color alternateRowColor = new Color(0.15f, 0.15f, 0.15f, 0.9f); 
    [SerializeField] private float rowSpacing = 2f;
    
    
    [Header("Font Settings")]
    [SerializeField] private float headerFontSize = 36f;
    [SerializeField] private float rankFontSize = 24f;
    [SerializeField] private float nameFontSize = 24f;
    [SerializeField] private float scoreFontSize = 24f;

    private List<GameObject> playerEntries = new List<GameObject>();
    private ConnectionManager connectionManager;

    private void Awake()
    {
        connectionManager = ConnectionManager.Instance;
        
        if (headerText != null)
        {
            headerText.color = headerColor;
            if (technoFont != null)
                headerText.font = technoFont;
            headerText.fontSize = headerFontSize;
            
            // Set up outline using material properties
            headerText.fontMaterial.EnableKeyword("OUTLINE_ON");
            headerText.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
            headerText.fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0.5f, 0f, 1f));
        }
    }

    private void OnEnable()
    {
        UpdatePlayerList();
    }

    public void UpdatePlayerList()
    {
        if (connectionManager == null)
        {
            connectionManager = ConnectionManager.Instance;
            if (connectionManager == null) return;
        }

        ClearPlayerEntries();
        
        // Get player data and sort by score
        var playerData = GetAllPlayerData().OrderByDescending(p => p.score).ToList();
            
        // Create player entries
        for (int i = 0; i < playerData.Count; i++)
        {
            CreatePlayerEntry(i + 1, playerData[i], i % 2 == 1);
        }
        
        // Update header if available
        if (headerText != null)
        {
            headerText.text = $"SCOREBOARD - {connectionManager.GetPlayerCount()} PLAYERS";
        }
    }

    private List<PlayerData> GetAllPlayerData()
    {
        List<PlayerData> result = new List<PlayerData>();
        
        if (NetworkManager.Singleton != null)
        {
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                PlayerData playerData = new PlayerData();
                
                if (connectionManager.TryGetPlayerData(clientId, out global::PlayerData networkPlayerData))
                {
                    playerData.playerId = clientId.ToString();
                    playerData.playerName = networkPlayerData.username;
                    playerData.score = networkPlayerData.score;
                    playerData.isActive = networkPlayerData.state != PlayerState.Dead;
                    playerData.colorIndex = networkPlayerData.colorIndex;
                }
                
                result.Add(playerData);
            }
        }
        
        return result;
    }

    private void ClearPlayerEntries()
    {
        foreach (var entry in playerEntries)
        {
            Destroy(entry);
        }
        playerEntries.Clear();
    }

    private void CreatePlayerEntry(int rank, PlayerData playerData, bool isAlternateRow)
    {
        if (playerEntryPrefab == null || playerListContainer == null) return;

        GameObject entryObject = Instantiate(playerEntryPrefab, playerListContainer);
        playerEntries.Add(entryObject);

        // Set position with spacing
        RectTransform rectTransform = entryObject.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = new Vector2(0, -rowSpacing * (rank - 1));
        }

        // Set background color with alternating rows
        Image background = entryObject.GetComponent<Image>();
        if (background != null)
        {
            background.color = isAlternateRow ? alternateRowColor : rowBackgroundColor;
        }

        // Set rank text with cyberpunk styling
        TextMeshProUGUI rankText = entryObject.transform.Find("RankText")?.GetComponent<TextMeshProUGUI>();
        if (rankText != null)
        {
            if (technoFont != null)
                rankText.font = technoFont;
            rankText.fontSize = rankFontSize;
            rankText.text = rank.ToString();
            
            // Color coding for top ranks
            if (rank == 1)
                rankText.color = new Color(0f, 1f, 0f); // Bright green
            else if (rank == 2)
                rankText.color = new Color(0f, 0.8f, 0f); // Slightly dimmer green
            else if (rank == 3)
                rankText.color = new Color(0f, 0.6f, 0f); // Even dimmer green
            
            // Set up outline using material properties
            rankText.fontMaterial.EnableKeyword("OUTLINE_ON");
            rankText.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
            rankText.fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0.3f, 0f));
        }

        // Set player name with color
        TextMeshProUGUI nameText = entryObject.transform.Find("NameText")?.GetComponent<TextMeshProUGUI>();
        if (nameText != null)
        {
            if (technoFont != null)
                nameText.font = technoFont;
            nameText.fontSize = nameFontSize;
            
            // Convert playerId string back to ulong for the ConnectionManager method
            if (ulong.TryParse(playerData.playerId, out ulong clientId))
            {
                string coloredName = connectionManager.GetPlayerColoredName(clientId);
                nameText.text = coloredName;
            }
            else
            {
                nameText.text = playerData.playerName;
            }
            
            // Set up outline using material properties
            nameText.fontMaterial.EnableKeyword("OUTLINE_ON");
            nameText.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
            nameText.fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
        }

        // Set score text 
        TextMeshProUGUI scoreText = entryObject.transform.Find("ScoreText")?.GetComponent<TextMeshProUGUI>();
        if (scoreText != null)
        {
            if (technoFont != null)
                scoreText.font = technoFont;
            scoreText.fontSize = scoreFontSize;
            scoreText.text = playerData.score.ToString();
            scoreText.color = new Color(0f, 1f, 0f); // Bright green
            
            // Set up outline using material properties
            scoreText.fontMaterial.EnableKeyword("OUTLINE_ON");
            scoreText.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
            scoreText.fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0.3f, 0f));
        }

        // Set status icon 
        GameObject statusIcon = entryObject.transform.Find("StatusIcon")?.gameObject;
        if (statusIcon != null)
        {
            bool isDeadOrDisconnected = !playerData.isActive;
            statusIcon.SetActive(isDeadOrDisconnected);
            
            if (isDeadOrDisconnected)
            {
                TextMeshProUGUI statusText = statusIcon.GetComponentInChildren<TextMeshProUGUI>();
                if (statusText != null)
                {
                    if (technoFont != null)
                        statusText.font = technoFont;
                    statusText.fontSize = nameFontSize * 0.8f; // Slightly smaller
                    statusText.text = "ELIMINATED";
                    statusText.color = new Color(1f, 0f, 0f); // Bright red for eliminated
                    
                    // Set up outline using material properties
                    statusText.fontMaterial.EnableKeyword("OUTLINE_ON");
                    statusText.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
                    statusText.fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.3f, 0f, 0f));
                }
            }
        }
    }

    // Helper class to work with player data
    public class PlayerData
    {
        public string playerId;
        public string playerName;
        public int score;
        public bool isActive;
        public int colorIndex;
    }
}
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

    [Header("Team Colors")]
    [SerializeField] private Color teamRedColor = new Color(1.0f, 0.2f, 0.2f); // Red team
    [SerializeField] private Color teamBlueColor = new Color(0.2f, 0.4f, 1.0f); // Blue team
    [SerializeField] private Color teamGreenColor = new Color(0.2f, 0.8f, 0.2f); // Green team
    [SerializeField] private Color teamYellowColor = new Color(1.0f, 0.8f, 0.2f); // Yellow team

    [Header("Font Settings")]
    [SerializeField] private float headerFontSize = 36f;
    [SerializeField] private float rankFontSize = 24f;
    [SerializeField] private float nameFontSize = 24f;
    [SerializeField] private float scoreFontSize = 24f;

    private List<GameObject> playerEntries = new List<GameObject>();
    private ConnectionManager connectionManager;
    private GameManager gameManager;

    private void Awake()
    {
        connectionManager = ConnectionManager.Instance;
        gameManager = GameManager.Instance;

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

        // Subscribe to game state changes to update the scoreboard
        if (gameManager != null)
        {
            gameManager.OnGameStateChanged += HandleGameStateChanged;
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe to prevent memory leaks
        if (gameManager != null)
        {
            gameManager.OnGameStateChanged -= HandleGameStateChanged;
        }
    }

    private void HandleGameStateChanged(GameState newState)
    {
        // Update the scoreboard when game state changes
        if (gameObject.activeInHierarchy)
        {
            Debug.Log($"Scoreboard updating due to game state change to {newState}");
            UpdatePlayerList();
        }
    }

    private void OnEnable()
    {
        Debug.Log("Scoreboard OnEnable - forcing refresh");

        // Make sure we have up-to-date references
        if (connectionManager == null)
            connectionManager = ConnectionManager.Instance;

        if (gameManager == null)
            gameManager = GameManager.Instance;

        // Force a refresh of the player list
        UpdatePlayerList();
    }

    public void UpdatePlayerList()
    {
        // Make sure we have necessary references
        connectionManager = ConnectionManager.Instance;
        gameManager = GameManager.Instance;

        // Check if we're in team battle mode
        GameMode currentMode = gameManager.gameMode;
        bool isTeamBattle = currentMode == GameMode.TeamBattle;

        Debug.Log($"[SCOREBOARD] Updating player list - Game Mode: {currentMode} (isTeamBattle: {isTeamBattle})");

        // Clear existing entries
        ClearPlayerEntries();

        if (isTeamBattle)
        {
            // Team Battle Mode - Group by teams
            UpdateTeamBattleScoreboard();
        }
        else
        {
            // Free-for-all Mode - Sort by individual score
            UpdateFreeForAllScoreboard();
        }

        // Update header if available
        if (headerText != null)
        {
            string gameMode = isTeamBattle ? "TEAM BATTLE" : "FREE-FOR-ALL";
            headerText.text = $"SCOREBOARD - {connectionManager.GetPlayerCount()} PLAYERS - {gameMode}";
        }
    }

    private void UpdateFreeForAllScoreboard()
    {
        // Get player data and sort by score
        var playerData = GetAllPlayerData().OrderByDescending(p => p.score).ToList();

        // Create player entries
        for (int i = 0; i < playerData.Count; i++)
        {
            CreatePlayerEntry(i + 1, playerData[i], i % 2 == 1);
        }
    }

    private void UpdateTeamBattleScoreboard()
    {
        // Get all player data
        var allPlayerData = GetAllPlayerData();

        // Group players by team
        var teamGroups = allPlayerData.GroupBy(p => p.teamNumber).OrderByDescending(g => g.Sum(p => p.score)).ToList();
        int currentRank = 1;
        int entryIndex = 0;

        // For each team, create entries with the same rank
        foreach (var teamGroup in teamGroups)
        {
            int teamNumber = teamGroup.Key;

            // Skip team 0 (unassigned/none)
            if (teamNumber == 0) continue;

            // Get team color based on team number
            Color teamColor = GetTeamColor(teamNumber);

            // Get team members sorted by individual score
            var teamMembers = teamGroup.OrderByDescending(p => p.score).ToList();

            // Create entries for each team member with same rank
            foreach (var playerData in teamMembers)
            {
                bool isAlternateRow = entryIndex % 2 == 1;
                CreateTeamPlayerEntry(currentRank, playerData, isAlternateRow, teamNumber, teamColor);
                entryIndex++;
            }

            // Increment rank for next team
            currentRank++;
        }

        // Add unassigned players at the bottom if any (team 0)
        var unassignedPlayers = allPlayerData.Where(p => p.teamNumber == 0).OrderByDescending(p => p.score).ToList();
        if (unassignedPlayers.Count > 0)
        {
            foreach (var playerData in unassignedPlayers)
            {
                bool isAlternateRow = entryIndex % 2 == 1;
                CreatePlayerEntry(0, playerData, isAlternateRow); // Rank 0 for unassigned
                entryIndex++;
            }
        }
    }

    private Color GetTeamColor(int teamNumber)
    {
        switch (teamNumber)
        {
            case 1: return teamRedColor;
            case 2: return teamBlueColor;
            case 3: return teamGreenColor;
            case 4: return teamYellowColor;
            default: return Color.white;
        }
    }

    private string GetTeamName(int teamNumber)
    {
        switch (teamNumber)
        {
            case 1: return "RED";
            case 2: return "BLUE";
            case 3: return "GREEN";
            case 4: return "YELLOW";
            default: return "NONE";
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
                    playerData.teamNumber = networkPlayerData.team;
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

    private void CreateTeamPlayerEntry(int rank, PlayerData playerData, bool isAlternateRow, int teamNumber, Color teamColor)
    {
        GameObject entryObject = Instantiate(playerEntryPrefab, playerListContainer);
        playerEntries.Add(entryObject);

        // Set position with spacing
        RectTransform rectTransform = entryObject.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = new Vector2(0, -rowSpacing * (playerEntries.Count - 1));
        }

        // Set background color based on team with alternating intensity
        Image background = entryObject.GetComponent<Image>();
        if (background != null)
        {
            // Create a darker/lighter version of the team color for alternating rows
            Color rowColor = teamColor * (isAlternateRow ? 0.7f : 0.9f);
            rowColor.a = 0.7f; // Semi-transparent
            background.color = rowColor;
        }

        // Set rank text with team styling
        TextMeshProUGUI rankText = entryObject.transform.Find("RankText")?.GetComponent<TextMeshProUGUI>();
        if (rankText != null)
        {
            if (technoFont != null)
                rankText.font = technoFont;
            rankText.fontSize = rankFontSize;
            rankText.text = rank.ToString();
            rankText.color = teamColor;

            // Create a new material instance to avoid shared material modifications
            rankText.fontMaterial = new Material(rankText.fontMaterial);
            rankText.fontMaterial.EnableKeyword("OUTLINE_ON");
            rankText.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
            rankText.fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
        }

        // Set player name with team prefix
        TextMeshProUGUI nameText = entryObject.transform.Find("NameText")?.GetComponent<TextMeshProUGUI>();
        if (nameText != null)
        {
            nameText.font = technoFont;
            nameText.fontSize = nameFontSize;

            // Add team name prefix to player name
            string teamPrefix = $"[{GetTeamName(teamNumber)}] ";

            string playerName = playerData.playerName;
            nameText.text = teamPrefix + playerName;

            // Color the text based on team color
            nameText.color = teamColor;

            // Create a new material instance to avoid shared material modifications
            nameText.fontMaterial = new Material(nameText.fontMaterial);
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
            scoreText.color = teamColor;

            // Create a new material instance to avoid shared material modifications
            scoreText.fontMaterial = new Material(scoreText.fontMaterial);
            scoreText.fontMaterial.EnableKeyword("OUTLINE_ON");
            scoreText.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
            scoreText.fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
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

                    // Create a new material instance to avoid shared material modifications
                    statusText.fontMaterial = new Material(statusText.fontMaterial);
                    statusText.fontMaterial.EnableKeyword("OUTLINE_ON");
                    statusText.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
                    statusText.fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.3f, 0f, 0f));
                }
            }
        }
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
            rectTransform.anchoredPosition = new Vector2(0, -rowSpacing * (playerEntries.Count - 1));
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
            rankText.text = rank > 0 ? rank.ToString() : "-";

            // Color coding for top ranks
            if (rank == 1)
                rankText.color = new Color(0f, 1f, 0f); // Bright green
            else if (rank == 2)
                rankText.color = new Color(0f, 0.8f, 0f); // Slightly dimmer green
            else if (rank == 3)
                rankText.color = new Color(0f, 0.6f, 0f); // Even dimmer green
            else if (rank == 0)
                rankText.color = new Color(0.5f, 0.5f, 0.5f); // Gray for unassigned

            // Create a new material instance to avoid shared material modifications
            rankText.fontMaterial = new Material(rankText.fontMaterial);
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

            // Create a new material instance to avoid shared material modifications
            nameText.fontMaterial = new Material(nameText.fontMaterial);
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

            // Create a new material instance to avoid shared material modifications
            scoreText.fontMaterial = new Material(scoreText.fontMaterial);
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

                    // Create a new material instance to avoid shared material modifications
                    statusText.fontMaterial = new Material(statusText.fontMaterial);
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
        public int teamNumber;
    }
}
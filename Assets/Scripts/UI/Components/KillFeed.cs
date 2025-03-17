using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hogtagon.Core.Infrastructure;

public class KillFeed : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private int maxMessages = 5;
    [SerializeField] private float messageDuration = 5f;
    [SerializeField] private GameObject killFeedItemPrefab;
    [SerializeField] private Transform killFeedContainer;

    private readonly string[] killMessages = {
        "DEMOLISHED",
        "EVISCERATED",
        "CLOBBERED",
        "OBLITERATED",
        "ANNIHILATED",
        "PULVERIZED",
        "DECIMATED",
    };

    private readonly string[] suicideMessages = {
        "took themselves out!",
        "couldn't handle the pressure!",
        "chose the easy way out!",
        "discovered gravity!",
        "made a fatal mistake!",
        "failed spectacularly!",
    };

    private Queue<GameObject> activeMessages = new Queue<GameObject>();
    private bool isPaused;
    private GameObject lastKillMessage;

    private void Awake() => ServiceLocator.RegisterService<KillFeed>(this);

    private void Start()
    {
        if (GameManager.instance != null)
        {
            GameManager.instance.OnGameStateChanged += HandleGameStateChange;
        }
    }

    private void OnDestroy()
    {
        ServiceLocator.UnregisterService<KillFeed>();
        if (GameManager.instance != null)
        {
            GameManager.instance.OnGameStateChanged -= HandleGameStateChange;
        }
    }

    private void HandleGameStateChange(GameState newState)
    {
        switch (newState)
        {
            case GameState.Pending:
            case GameState.Playing:
                isPaused = false;
                ClearAllMessages();
                break;
            case GameState.Ending:
                isPaused = true;
                ClearAllMessagesExceptLast();
                break;
        }
    }

    private void ClearAllMessages()
    {
        while (activeMessages.Count > 0)
        {
            GameObject message = activeMessages.Dequeue();
            if (message != null) Destroy(message);
        }
        lastKillMessage = null;
    }

    private void ClearAllMessagesExceptLast()
    {
        if (activeMessages.Count > 0)
        {
            lastKillMessage = activeMessages.Last();
            while (activeMessages.Count > 1)
            {
                GameObject message = activeMessages.Dequeue();
                if (message != null && message != lastKillMessage)
                {
                    Destroy(message);
                }
            }
        }
    }

    private void AddMessage(GameObject messageObj, string messageText)
    {
        if (messageObj.TryGetComponent<TextMeshProUGUI>(out var textComponent))
        {
            textComponent.text = messageText;
            lastKillMessage = messageObj;
            activeMessages.Enqueue(messageObj);

            if (activeMessages.Count > maxMessages)
            {
                GameObject oldestMessage = activeMessages.Dequeue();
                if (oldestMessage != lastKillMessage) Destroy(oldestMessage);
            }

            StartCoroutine(RemoveMessageAfterDuration(messageObj));
        }
    }

    public void AddKillMessage(string killerName, string victimName)
    {
        if (isPaused || !killFeedItemPrefab || !killFeedContainer)
        {
            Debug.LogWarning("KillFeed: Cannot add message - feed is paused or missing references");
            return;
        }

        string killMessage = killMessages[Random.Range(0, killMessages.Length)];
        string messageText = $"{killerName} <color=red>{killMessage}</color> {victimName}";
        
        GameObject messageObj = Instantiate(killFeedItemPrefab, killFeedContainer);
        AddMessage(messageObj, messageText);
    }

    public void AddSuicideMessage(string playerName)
    {
        if (isPaused || !killFeedItemPrefab || !killFeedContainer)
        {
            Debug.LogWarning("KillFeed: Cannot add message - feed is paused or missing references");
            return;
        }

        string suicideMessage = suicideMessages[Random.Range(0, suicideMessages.Length)];
        string messageText = $"<color=yellow>{playerName}</color> {suicideMessage}";
        
        GameObject messageObj = Instantiate(killFeedItemPrefab, killFeedContainer);
        AddMessage(messageObj, messageText);
    }

    private IEnumerator RemoveMessageAfterDuration(GameObject message)
    {
        yield return new WaitForSeconds(messageDuration);

        if (activeMessages.Contains(message))
        {
            activeMessages = new Queue<GameObject>(activeMessages.Where(m => m != message));
            if (message == lastKillMessage) lastKillMessage = null;
            Destroy(message);
        }
    }
}
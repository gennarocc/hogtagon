using UnityEngine;
using TMPro;

public class KillFeedItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI messageText;
    
    public void SetMessage(string text)
    {
        if (messageText != null)
        {
            messageText.text = text;
        }
    }
} 
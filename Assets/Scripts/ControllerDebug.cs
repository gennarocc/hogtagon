using UnityEngine;

public class ControllerDebug : MonoBehaviour
{
    void OnGUI()
    {
        GUI.Box(new Rect(10, 10, 300, 500), "Controller Debug");
        int y = 30;

        // Show connected controllers
        string[] controllers = Input.GetJoystickNames();
        GUI.Label(new Rect(20, y, 280, 20), $"Connected Controllers: {controllers.Length}");
        y += 25;
        foreach (string controller in controllers)
        {
            GUI.Label(new Rect(20, y, 280, 20), controller);
            y += 20;
        }
        y += 10;

        // Try various known Xbox trigger names
        string[] possibleTriggerNames = new string[] {
            "XboxOneRightTrigger",
            "XboxOneLeftTrigger", 
            "RT",
            "LT",
            "Right Trigger",
            "Left Trigger",
            "Trigger Right",
            "Trigger Left"
        };

        foreach (string name in possibleTriggerNames)
        {
            try
            {
                float value = Input.GetAxis(name);
                GUI.Label(new Rect(20, y, 280, 20), $"{name}: {value:F3}");
                y += 20;
            }
            catch {}
        }

        y += 10;
        GUI.Label(new Rect(20, y, 280, 20), "Raw Axis Values:");
        y += 20;

        // Show all raw axis values
        for (int i = 0; i < 20; i++)
        {
            try
            {
                float value = Input.GetAxis($"Joystick1Axis{i}");
                GUI.Label(new Rect(20, y, 280, 20), $"Axis {i}: {value:F3}");
                y += 20;
            }
            catch {}
        }

        // Also try Windows.Gaming namespace axis values if available
        y += 10;
        GUI.Label(new Rect(20, y, 280, 20), "Right Stick X: " + Input.GetAxis("Horizontal"));
        y += 20;
        GUI.Label(new Rect(20, y, 280, 20), "3rd axis (shared triggers): " + Input.GetAxis("3rd axis"));
    }
}
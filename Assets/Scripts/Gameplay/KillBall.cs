using UnityEngine;
using Unity.Netcode;

public class KillBall : NetworkBehaviour
{
    [SerializeField] private float blowupForce = 2;
    [SerializeField] public Vector3 pendingSize = new Vector3(20, 20, 20);
    [SerializeField] public Vector3 initialSize;
    [SerializeField] public Vector3 targetSize;
    [SerializeField] private float duration = 60;
    
    private void Update()
    {
        if (GameManager.instance.state == GameState.Pending)
        {
            // Ensure we always reset to the initial size in Pending state
            transform.localScale = pendingSize;
            return;
        }
        float scaleLerp = Mathf.Clamp01(GameManager.instance.gameTime / duration);
        transform.localScale = Vector3.Lerp(initialSize, targetSize, scaleLerp);
    }

    private void OnTriggerEnter(Collider col)
    {
        // Only the server should process trigger collisions for consistency
        if (!IsServer) return;

        // Check if this is a player
        if (!col.gameObject.CompareTag("Player")) return;

        // Get the NetworkObject from the colliding player
        NetworkObject playerNetObj = col.transform.root.GetComponent<NetworkObject>();
        if (playerNetObj == null) return;

        ulong clientId = playerNetObj.OwnerClientId;
        Debug.Log($"Server detected player {clientId} hit the kill ball");

        // Process the collision on the server
        ProcessPlayerCollision(clientId);
    }

    private void ProcessPlayerCollision(ulong clientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) return;

        var client = NetworkManager.Singleton.ConnectedClients[clientId];
        Debug.Log($"{clientId} - Add blow up force to {client.PlayerObject.name}");

        // Get the rigidbody
        Rigidbody rb = client.PlayerObject.GetComponentInChildren<Rigidbody>();
        if (rb == null) return;

        // Launch the car in a random angle with a lot of force
        rb.AddForce(new Vector3(Random.Range(-.6f, -.6f), 1, Random.Range(-.6f, .6f))
            * blowupForce * 10000, ForceMode.Impulse);

        // Add random rotational force
        Vector3 randomTorque = new Vector3(
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f)
        ).normalized * 1000f;
        rb.AddTorque(randomTorque, ForceMode.Impulse);

        // Set player to dead
        if (GameManager.instance.state == GameState.Playing)
        {
            GameManager.instance.PlayerDied(clientId);
        }

        // Get the NetworkHogController instead of HogController
        NetworkHogController hogController = client.PlayerObject.GetComponentInChildren<NetworkHogController>();
        if (hogController != null)
        {
            // Call the explosion effect on all clients
            hogController.ExplodeCarClientRpc();
        }
        else
        {
            Debug.LogWarning($"Couldn't find NetworkHogController for player {clientId}");
        }
    }
}
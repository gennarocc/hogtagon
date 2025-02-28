using UnityEngine;
using Unity.Netcode;

public class KillBall : NetworkBehaviour
{
    [SerializeField] private float blowupForce = 2;
    [SerializeField] public Vector3 initialSize;
    [SerializeField] public Vector3 targetSize;
    [SerializeField] private float duration = 60;
    [Header("Wwise")]
    [SerializeField] private AK.Wwise.Event CarExplosion;

    private void Update()
    {
        if (GameManager.instance.state == GameState.Pending)
        {
            transform.localScale = new Vector3(20, 20, 20);
            return;
        }
        float scaleLerp = Mathf.Clamp01(GameManager.instance.gameTime / duration);
        transform.localScale = Vector3.Lerp(initialSize, targetSize, scaleLerp);
    }

    private void OnTriggerEnter(Collider col)
    {
        if (!col.gameObject.CompareTag("Player")) return;
        if (!IsOwner) return;
        Debug.Log(message: "Add blow up force");
        var data = new CollisionData()
        {
            id = col.transform.root.gameObject.GetComponent<NetworkObject>().OwnerClientId
        };
        NotifyPlayerCollisionServerRpc(data);
    }

    [ServerRpc]
    private void NotifyPlayerCollisionServerRpc(CollisionData data)
    {
        var clientId = data.id;
        if (NetworkManager.ConnectedClients.ContainsKey(clientId))
        {
            var client = NetworkManager.Singleton.ConnectedClients[clientId];
            Debug.Log(message: clientId + " - Add blow up force to " + client.PlayerObject.name);

            // Get the rigidbody
            Rigidbody rb = client.PlayerObject.GetComponentInChildren<Rigidbody>();

            // Launch the car in a random angle with a lot of force
            rb.AddForce(new Vector3(Random.Range(-.6f, -.6f), 1, Random.Range(-.6f, .6f))
                * blowupForce * 10000, ForceMode.Impulse);

            // Add random rotational force
            Vector3 randomTorque = new Vector3(
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f)
            ).normalized * 1000f; // Adjust this multiplier as needed
            rb.AddTorque(randomTorque, ForceMode.Impulse);

            // Set player to dead
            if (GameManager.instance.state == GameState.Playing)
            {
                GameManager.instance.PlayerDied(clientId);
            }
            // FX
            client.PlayerObject.GetComponentInChildren<HogController>().ExplodeCarClientRpc();
        }
    }

    struct CollisionData : INetworkSerializable
    {
        public ulong id;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref id);
        }
    }
}
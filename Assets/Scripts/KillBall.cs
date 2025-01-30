using UnityEngine;
using Unity.Netcode;

public class KillBall : NetworkBehaviour
{
    [SerializeField] private float blowupForce = 2;
    [SerializeField] public Vector3 initialSize;
    [SerializeField] public Vector3 targetSize;
    [SerializeField] private float duration = 60;
    

    private void Update()
    {
       float scaleLerp = Mathf.Clamp01(GameManager.instance.gameTime / duration); 
       transform.localScale = Vector3.Lerp(initialSize, targetSize, scaleLerp);
    }


    private void OnTriggerEnter(Collider col)
    {
        if (!col.gameObject.CompareTag("Player")) return;
        if (!IsOwner) return;
        Debug.Log(message: "Add blow up force");
        var data = new CollisionData() {
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
            // Launch the car in a random angle with a lot of force.
            // TODO Set player status to Dead.
            client.PlayerObject.GetComponent<Rigidbody>().AddForce(new Vector3(Random.Range(-.6f, -.6f), 1, Random.Range(-.6f, .6f)) * blowupForce * 10000, ForceMode.Impulse);
        }
    }

    // [ClientRpc]
    // private void NotifyPlayerCollisionClientRPC(ServerRpcParams serverRpcParams = default)
    // {
    //     var clientId = serverRpcParams.Receive.SenderClientId;
    //     if (NetworkManager.ConnectedClients.ContainsKey(clientId))
    //     {
    //         var client = NetworkManager.ConnectedClients[clientId];
    //         float forceMultiplier = Random.Range(500f, 1000f);
    //         client.PlayerObject.GetComponent<Rigidbody>().AddForce(new Vector3(0, 1, 0) * forceMultiplier);
    //     }
    // }


    struct CollisionData : INetworkSerializable
    {
        public ulong id;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref id);
        }
    }
}
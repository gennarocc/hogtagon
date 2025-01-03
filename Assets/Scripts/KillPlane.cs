using UnityEngine;

public class KillPlane : MonoBehaviour
{
    void OnTriggerEnter(Collider collider)
    {
        if ( collider.gameObject.tag == "Player")
        {
            Debug.Log(collider.transform.root.gameObject.name);
            collider.transform.root.gameObject.TryGetComponent<Player>(out Player player);
            player.Respawn();
        }
        
    }
}

using Unity.Netcode.Components;
using UnityEngine;
using Unity.Netcode;

namespace Unity.Multiplayer.Samples.Utilities.ClientAuthority
{
    /// <summary>
    /// Extended ClientNetworkTransform that supports temporary collision authority
    /// </summary>
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        // Reference to hog controller to check for collision authority
        private NetworkHogController hogController;
        
        // Override normal network transform behavior
        private bool wasLocallyOwned;
        private bool isCurrentlyUnderCollisionAuthority = false;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            // Cache initial ownership
            wasLocallyOwned = IsOwner;
            
            // Find the NetworkHogController in the hierarchy (might be on parent or child)
            hogController = GetComponentInParent<NetworkHogController>();
            if (hogController == null)
            {
                hogController = GetComponentInChildren<NetworkHogController>();
            }
        }

        /// <summary>
        /// Used to determine who can write to this transform. Modified to support collision-based authority.
        /// </summary>
        protected override bool OnIsServerAuthoritative()
        {
            // First check - if we're the owner, we have authority
            if (IsOwner) return false;
            
            // Second check - if we have temporary collision authority from the server
            if (hogController != null && hogController.HasCollisionAuthorityFrom(NetworkManager.LocalClientId))
            {
                // Locally cache this state for the FixedUpdate checks
                isCurrentlyUnderCollisionAuthority = true;
                return false; // This client has temporary authority
            }
            
            // Reset when we don't have authority
            isCurrentlyUnderCollisionAuthority = false;
            
            // Normal behavior - client authority based on ownership
            return false;
        }
        
        private void FixedUpdate()
        {
            // If our authority changed due to a collision, send position updates
            if (isCurrentlyUnderCollisionAuthority)
            {
                // We're a non-owner with temporary collision authority - notify owner
                if (hogController != null)
                {
                    hogController.SendStateUpdate();
                }
            }
        }
    }
}
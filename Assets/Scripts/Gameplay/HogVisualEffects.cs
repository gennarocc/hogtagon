using UnityEngine;
using System.Collections;

public class HogVisualEffects : MonoBehaviour
{
    [Header("Effects")]
    [SerializeField] public ParticleSystem rearLeftWheelParticleSystem;
    [SerializeField] public ParticleSystem rearRightWheelParticleSystem;
    [SerializeField] public TrailRenderer rearLeftWheelTireSkid;
    [SerializeField] public TrailRenderer rearRightWheelTireSkid;
    [SerializeField] public GameObject explosionPrefab;
    [SerializeField] public ParticleSystem[] jumpParticleSystems = new ParticleSystem[2]; // RL, RR

    private bool driftingSoundOn = false;
    private GameObject currentExplosionInstance;

    // References
    private Transform hogTransform;
    private Vector3 centerOfMass;
    private ulong ownerClientId;

    public void Initialize(Transform hogTransform, Vector3 centerOfMass, ulong ownerClientId)
    {
        this.hogTransform = hogTransform;
        this.centerOfMass = centerOfMass;
        this.ownerClientId = ownerClientId;
    }

    public void UpdateDriftEffects(bool isDrifting, bool rearLeftGrounded, bool rearRightGrounded, bool canMove)
    {
        if (isDrifting)
        {
            // Left wheel effects
            if (rearLeftGrounded)
            {
                if (!driftingSoundOn && canMove)
                {
                    driftingSoundOn = true;
                }
                rearLeftWheelParticleSystem.Play();
                rearLeftWheelTireSkid.emitting = true;
            }
            else
            {
                if (driftingSoundOn)
                {
                    driftingSoundOn = false;
                }
                rearLeftWheelParticleSystem.Stop();
                rearLeftWheelTireSkid.emitting = false;
            }

            // Right wheel effects
            if (rearRightGrounded)
            {
                rearRightWheelParticleSystem.Play();
                rearRightWheelTireSkid.emitting = true;
            }
            else
            {
                rearRightWheelParticleSystem.Stop();
                rearRightWheelTireSkid.emitting = false;
            }
        }
        else
        {
            // Not drifting, turn off all effects
            StopAllEffects();
        }
    }

    public void PlayJumpEffects()
    {
        if (jumpParticleSystems != null && jumpParticleSystems.Length >= 2)
        {
            jumpParticleSystems[0].Play();
            jumpParticleSystems[1].Play();
        }
    }

    public void StopAllEffects()
    {
        rearLeftWheelParticleSystem.Stop();
        rearRightWheelParticleSystem.Stop();
        rearLeftWheelTireSkid.emitting = false;
        rearRightWheelTireSkid.emitting = false;

        if (driftingSoundOn)
        {
            SoundManager.Instance.PlayNetworkedSound(hogTransform.root.gameObject, SoundManager.SoundEffectType.TireScreechOff);
            driftingSoundOn = false;
        }
    }

    public void CreateExplosion(bool canMove)
    {
        // Store reference to instantiated explosion
        currentExplosionInstance = Instantiate(explosionPrefab, hogTransform.position + centerOfMass, hogTransform.rotation, hogTransform);

        // Play explosion sound
        SoundManager.Instance.PlayNetworkedSound(hogTransform.root.gameObject, SoundManager.SoundEffectType.CarExplosion);

        // Stop drift sounds if active
        if (driftingSoundOn)
        {
            SoundManager.Instance.PlayNetworkedSound(hogTransform.root.gameObject, SoundManager.SoundEffectType.TireScreechOff);
            driftingSoundOn = false;
        }

        Debug.Log("Exploding car for player - " + ConnectionManager.Instance.GetClientUsername(ownerClientId));

        // Cleanup explosion after delay
        StartCoroutine(CleanupExplosion());
    }

    private IEnumerator CleanupExplosion()
    {
        yield return new WaitForSeconds(3f);

        if (currentExplosionInstance != null)
        {
            Destroy(currentExplosionInstance);
            currentExplosionInstance = null;
        }
    }
}
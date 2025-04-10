# Visual Effects Setup for HogController

## Particle Systems and Visual Effects

The HogController relies on several visual effect components to provide feedback during gameplay. Here's how to set them up properly:

### Wheel Particle Systems (Drift Effects)
The HogController uses particle systems for the rear wheels to create drift effects:

1. Locate the `wheelParticleSystems` array in the HogController Inspector
2. Ensure this array has 2 elements (for rear left and rear right wheels)
3. Assign the appropriate particle systems that should activate when drifting
4. Typical setup:
   - Element 0: Rear Left wheel particle system
   - Element 1: Rear Right wheel particle system
5. If these particle systems don't exist on the prefab:
   - Check the ClientAuthoritativePlayer prefab for reference
   - Create new particle systems with appropriate settings for tire smoke/drift effects
   - Position them at the rear wheel locations

### Tire Skid Trail Renderers
Trail renderers create the skid marks when drifting:

1. Locate the `tireSkids` array in the HogController Inspector
2. Ensure this array has 2 elements (for rear left and rear right wheels)
3. Assign the appropriate trail renderers for skid marks
4. Typical setup:
   - Element 0: Rear Left wheel trail renderer
   - Element 1: Rear Right wheel trail renderer
5. If these don't exist:
   - Create new Trail Renderer components
   - Configure them with appropriate width, material (dark/black), and time settings
   - Position them at the rear wheel contact points

### Jump Particle Systems
Particle effects that trigger when the jump ability is used:

1. Locate the `jumpParticleSystems` array in the HogController Inspector
2. Ensure this array has 2 elements
3. Assign particle systems for the jump effect
4. Typical setup:
   - Element 0: Rear Left jump thrust particle system
   - Element 1: Rear Right jump thrust particle system
5. If missing:
   - Create new particle systems with appropriate settings for jet/thrust effects
   - Position them at the rear of the vehicle

### Explosion Effect
The explosion effect when a vehicle is destroyed:

1. Locate the `Explosion` field in the HogController Inspector
2. Assign a prefab that contains the explosion visual effect
3. This should be a self-contained prefab that can be instantiated at runtime
4. It should include particle effects, light effects, and potentially audio for the explosion

## Audio Setup

### Engine Audio Source
1. Locate the `engineAudioSource` field in the Inspector
2. Assign an AudioSource component that will play engine sounds
3. Ensure the AudioSource has appropriate 3D sound settings

### Wwise Audio Setup (if using)
1. Locate the `rpm` field in the Inspector
2. Assign the appropriate Wwise RTPC (Real-Time Parameter Control) reference
3. This controls how the engine sound changes based on RPM

## Troubleshooting Visual Effects

If visual effects aren't working correctly:

1. **Missing References**:
   - Check that all arrays have the correct number of elements
   - Verify all references are properly assigned
   - Look for null reference exceptions in the console

2. **Effects Not Showing**:
   - Ensure particle systems have the correct materials
   - Check that emission modules are properly configured
   - Verify that particles are large enough to be visible

3. **Effects Not Triggering**:
   - Check that drift detection is working (isDrifting NetworkVariable)
   - Verify jump detection is working (isJumping NetworkVariable)
   - Confirm that effect activation methods are being called

## Performance Considerations

- Use appropriate particle counts (not too high)
- Consider using particle system LOD (Level of Detail)
- Limit trail renderer time to avoid excessive draw calls
- Ensure particle materials use efficient shaders 
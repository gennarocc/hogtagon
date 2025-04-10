# Player Prefab Update Plan

## Step 1: Backup Original Prefab
Before making any changes, create a backup of the original Player prefab.

## Step 2: Examine Prefab Structures
1. Open both prefabs in the Unity Inspector:
   - Player.prefab
   - ClientAuthoritativePlayer.prefab

2. Note the key structural differences:
   - In ClientAuthoritativePlayer, the Rigidbody is on the root GameObject
   - In ClientAuthoritativePlayer, the HogController is on the same GameObject as the Rigidbody

## Step 3: Update Player Prefab Hierarchy
1. Select the Player prefab in the Project window
2. If the Rigidbody is not on the root GameObject:
   - Remove the Rigidbody from its current location
   - Add a Rigidbody component to the root GameObject
   - Configure the Rigidbody with the same settings (mass, drag, etc.)

3. Add the HogController component to the root GameObject (same as Rigidbody)
4. Remove any existing NetworkHogController component

## Step 4: Configure HogController Component
Set appropriate values for the new serialized fields:
- `steeringBufferSize`: 5
- `minDeadzone`: 3.0
- `maxDeadzone`: 10.0
- `maxSpeedForSteering`: 20.0
- `minSteeringResponse`: 0.6
- `maxSteeringResponse`: 1.0
- `inputWeightingMethod`: Linear
- `weightingFactor`: 1.0
- `serverValidationThreshold`: 5.0
- `clientUpdateInterval`: 0.05
- `stateUpdateInterval`: 0.1
- `visualSmoothingSpeed`: 10.0

## Step 5: Set References in HogController
Copy all references from the old NetworkHogController to the new HogController:
1. Wheel Colliders
2. Wheel Transforms
3. Particle Systems
4. Trail Renderers
5. Explosion GameObject
6. Jump Particle Systems
7. Audio references
8. Any other serialized references

## Step 6: Update Other Components
Ensure any scripts that reference NetworkHogController now reference HogController:
1. Player.cs (already updated)
2. HogDebugger.cs (if attached)
3. Other components that might reference the controller

## Step 7: Test in Unity Editor
1. Enter Play mode and verify:
   - Vehicle controls respond appropriately
   - Network synchronization works correctly
   - Visual effects display properly
   - Jump functionality works correctly
   - Audio plays properly

## Step 8: Testing Checklist
- [ ] Vehicle movement/steering is responsive
- [ ] Vehicle syncs properly over network
- [ ] Jump effect works correctly
- [ ] Particle effects display properly
- [ ] Audio plays correctly
- [ ] No console errors related to missing references
- [ ] Player interactions with other game systems work as expected

## Common Issues and Solutions
1. **Missing References**: If the controller can't find references, ensure all serialized fields are properly assigned
2. **Network Sync Issues**: Check server validation threshold and update intervals
3. **Visual Glitches**: Ensure particle systems and visual effects are properly referenced
4. **Input Responsiveness**: Adjust steering input smoothing parameters
5. **Physics Behavior**: Match the Rigidbody settings from the original prefab

## Notes
- The Player.cs script has already been updated to reference HogController instead of NetworkHogController
- The HogController combines both local and networked behavior in one component
- This migration simplifies the hierarchy and improves server authority for better cheat prevention 
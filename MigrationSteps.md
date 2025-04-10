# Migration Steps: Server-Authoritative Direct Steering

This document outlines the steps needed to migrate from the `ClientAuthoritativePlayer` prefab with `NetworkHogController` to an updated `Player` prefab that uses the enhanced server-authoritative `HogController` with direct steering controls.

## Current Changes Made

1. ✅ Enhanced `HogController.cs` with server-authoritative updates:
   - Added direct steering implementation from `NetworkHogController`
   - Added server-authoritative validation and state synchronization
   - Updated input handling to support direct steering controls
   - Enhanced visual effects and physics handling
   - Maintained compatibility with existing systems

## Next Steps in Unity Editor

### 1. Update Player Prefab Hierarchy

The `ClientAuthoritativePlayer` prefab has the Rigidbody at the top level, while the older `Player` prefab has a different hierarchy. Update the `Player` prefab to match:

1. Open both prefabs side-by-side to compare
2. Modify the `Player` prefab to have Rigidbody as the parent/main component
3. Ensure the camera setup is correctly moved to maintain the same positioning

### 2. Configure Updated HogController

1. Open the `Player` prefab and locate the `HogController` component
2. Set up the new parameters:
   - Network Settings: `serverValidationThreshold`, `clientUpdateInterval`, `stateUpdateInterval`, `visualSmoothingSpeed`
   - Input Smoothing Settings: `steeringBufferSize`, `minDeadzone`, `maxDeadzone`, etc.
   - Set appropriate acceleration and deceleration factors

### 3. Update Player Script References

1. Ensure the `Player` script properly references the `HogController` (not `NetworkHogController`)
2. Update any UI or other systems that reference `NetworkHogController` to work with `HogController` instead

### 4. Testing Configuration

1. Create a test scene with multiple instances of the updated Player prefab
2. Test the following scenarios:
   - Direct steering control with keyboard (WASD)
   - Direct steering control with gamepad
   - Server validation of player positions
   - Client-side prediction and visual smoothing
   - Jumps and explosion effects
   - Respawning after death

## Important Changes to Player.cs

If `Player.cs` has references to `NetworkHogController`, update them to use `HogController` instead:

```csharp
// Find the HogController component in children
HogController hogController = GetComponent<HogController>();

if (hogController != null)
{
    // Request respawn via HogController
    hogController.RequestRespawnServerRpc();
}
```

## Key Benefits of This Migration

1. **Server Authority**: More robust validation of player positions with server authority
2. **Direct Steering**: Improved handling with direct steering inputs
3. **Smoother Experience**: Enhanced input smoothing and client-side prediction
4. **Simplified Hierarchy**: Rigidbody at the top level provides better physics behavior
5. **Consolidation**: Reduced code redundancy by combining the best parts of both systems

## Notes

- If visual effects don't appear correctly, double-check the references to particle systems and trail renderers
- Ensure all network RPCs are set up with the correct `RequireOwnership` parameters
- Debug messages can be enabled by setting `debugMode = true` on the HogController 
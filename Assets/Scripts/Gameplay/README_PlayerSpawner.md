# Player Spawner System

This system allows you to spawn AI-controlled cars that drive straight forward when you press the "1" key. The cars automatically despawn after 10 seconds to prevent them from falling forever.

## Quick Setup

1. **Add to Scene**: Add the `PlayerSpawnerSetup` component to any GameObject in your scene
2. **Assign Prefab**: Drag the `ServerAuthoritativePlayer` prefab from `Assets/Prefabs/` to the "Manual Prefab Reference" field
3. **Play**: Press "1" during gameplay to spawn AI cars!

## How It Works

- **Input**: The "1" key is mapped to the `SpawnAICar` action in the Input System
- **Spawning**: Cars spawn 20 units in front of the player's current position
- **AI Driving**: Each spawned car gets an `AutoDriver` component that makes it drive straight forward
- **Auto Cleanup**: Cars are automatically destroyed after 10 seconds
- **Limit**: Maximum of 5 spawned cars at once to prevent performance issues

## Components

### PlayerSpawner
The main component that handles spawning and managing AI cars.

**Settings:**
- `Spawn Distance`: How far in front to spawn cars (default: 20)
- `Spawn Height`: Height above ground to spawn (default: 2)
- `Despawn Time`: Time before auto-destroying cars (default: 10 seconds)
- `Max Spawned Cars`: Maximum number of cars at once (default: 5)
- `AI Motor Torque`: How fast the AI cars drive (default: 800)

### PlayerSpawnerSetup
Helper component for easy setup. Automatically creates and configures the PlayerSpawner.

### Input System Integration
The system integrates with your existing Input System:
- Added `SpawnAICar` action to the Gameplay action map
- Bound to Keyboard "1" key
- Handled through the InputManager

## Technical Details

### AI Car Configuration
When a car is spawned, the system:
1. Disables networking components (NetworkObject, Player, HogController)
2. Adds/configures AutoDriver component for AI behavior
3. Ensures proper physics setup (Rigidbody, WheelColliders)
4. Sets up automatic despawn timer

### Performance Considerations
- Cars are limited to 5 maximum to prevent performance issues
- Cars auto-despawn after 10 seconds
- Destroyed cars are automatically cleaned up from tracking lists
- Only works during gameplay mode (not in menus)

## Troubleshooting

**"No player prefab assigned!" error:**
- Make sure to assign the ServerAuthoritativePlayer prefab to the PlayerSpawner component

**Cars don't spawn:**
- Check that you're in gameplay mode (not in a menu)
- Verify the "1" key binding in the Input Actions
- Check the console for error messages

**Cars spawn in wrong location:**
- The system spawns cars relative to the local player's position
- If no player is found, it falls back to world origin (0,0,0)

**Cars don't drive:**
- The AutoDriver component should be automatically added
- Check that the AI Motor Torque setting is not zero
- Verify that wheel colliders are enabled on the spawned car

## Customization

You can customize the behavior by modifying the PlayerSpawner settings:
- Change spawn distance/height for different positioning
- Adjust despawn time for longer/shorter car lifetime
- Modify AI motor torque for faster/slower driving
- Change max spawned cars limit for more/fewer cars

## Integration with Cinematic Camera

The spawned AI cars work great with the cinematic camera system! When an AI car jumps, it can trigger the same cinematic effects as player cars, creating dynamic trailer footage with multiple cars performing stunts. 
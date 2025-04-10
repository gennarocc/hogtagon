# Network Architecture of Server-Authoritative HogController

## Overview

The HogController implements a server-authoritative model with client-side prediction to provide responsive controls while maintaining security. Below is a detailed explanation of how the network components work together.

## Key Components

### NetworkVariables

The HogController uses several NetworkVariables to synchronize state:

1. **isDrifting**: Indicates whether the vehicle is currently drifting
   - Read Permission: Everyone
   - Write Permission: Server only

2. **isJumping**: Indicates whether the vehicle is currently jumping
   - Read Permission: Everyone
   - Write Permission: Server only

3. **jumpReady**: Indicates whether the jump ability is available
   - Read Permission: Everyone
   - Write Permission: Server only

4. **vehicleState**: Contains a snapshot of the vehicle's physical state
   - Read Permission: Everyone
   - Write Permission: Owner only
   - Structure includes position, rotation, velocity, angular velocity, steering angle, motor torque, and update ID

### RPCs (Remote Procedure Calls)

The HogController uses several RPCs to handle network communication:

1. **Server RPCs:**
   - `SendInputServerRpc`: Sends player input to the server
   - `JumpServerRpc`: Requests a jump action on the server
   - `RequestRespawnServerRpc`: Requests vehicle respawn
   - `RequestInitialStateServerRpc`: Requests initial state on client spawn
   - `SendStateForValidationServerRpc`: Sends state to server for validation

2. **Client RPCs:**
   - `NotifyJumpClientRpc`: Notifies clients about a jump action
   - `JumpEffectsClientRpc`: Triggers jump visual effects on clients
   - `ExplodeCarClientRpc`: Triggers vehicle explosion on clients
   - `SendInitialStateClientRpc`: Sends initial state to newly connected clients
   - `BroadcastServerStateClientRpc`: Broadcasts the server's authoritative state
   - `CorrectClientPositionClientRpc`: Forces a position correction on clients
   - `ExecuteRespawnClientRpc`: Executes respawn on clients

## Network Flow

### Initialization

1. When a client spawns:
   - Owner: `InitializeOwnerVehicle()` is called
   - Server (non-owner): `InitializeServerControlledVehicle()` is called
   - Remote Client: `RequestInitialStateServerRpc()` is called, then `InitializeClientVehicle()`

### Regular Update Flow

1. **Client Input Collection:**
   - Owner collects input (steering, acceleration, braking)
   - Input is smoothed and processed locally for immediate response
   - Input is sent to server via `SendInputServerRpc()`

2. **Server Processing:**
   - Server receives input via `SendInputServerRpc()`
   - Server applies physics with authoritative control
   - Server validates client state via `ValidateOwnerState()`
   - If discrepancy exceeds threshold, server corrects client with `CorrectClientPositionClientRpc()`

3. **State Synchronization:**
   - Server broadcasts state periodically via `BroadcastServerStateClientRpc()`
   - Clients receive state via `OnVehicleStateChanged()` callback
   - Clients apply state with visual smoothing for non-owner vehicles

### Special Actions

1. **Jump Action:**
   - Client requests jump via `JumpServerRpc()`
   - Server validates and processes jump
   - Server broadcasts jump effects via `JumpEffectsClientRpc()`
   - Server manages cooldown via `JumpCooldownServer()`

2. **Respawn Action:**
   - Client requests respawn via `RequestRespawnServerRpc()`
   - Server validates and processes respawn
   - Server broadcasts respawn via `ExecuteRespawnClientRpc()`

## Key Network Settings

The HogController has several configurable network parameters:

1. **serverValidationThreshold** (5.0f):
   - Maximum allowed discrepancy between client and server positions
   - Higher values reduce corrections but may allow more cheating
   - Lower values provide tighter synchronization but may cause more corrections

2. **clientUpdateInterval** (0.05f):
   - How often clients send updates to the server (20Hz)
   - Balances network traffic with responsiveness

3. **stateUpdateInterval** (0.1f):
   - How often server broadcasts state to clients (10Hz)
   - Balances network traffic with synchronization quality

4. **visualSmoothingSpeed** (10f):
   - How quickly visual representations should move toward target positions
   - Higher values make movement more responsive but potentially jerkier
   - Lower values make movement smoother but potentially laggy

## Optimization Techniques

1. **Input Batching:**
   - Inputs are collected and sent at regular intervals rather than every frame
   - Reduces network traffic while maintaining responsiveness

2. **State Interpolation:**
   - Remote vehicles visually interpolate between network updates
   - Creates smooth movement despite lower update frequency

3. **Client-Side Prediction:**
   - Owner applies physics locally before server confirmation
   - Provides immediate feedback while maintaining server authority

4. **Selective Synchronization:**
   - Only essential state variables are synchronized
   - Reduces network bandwidth usage

## Security Considerations

1. The server remains the authority on all important physics and game state
2. Client inputs are validated against reasonable ranges
3. Position discrepancies beyond thresholds are corrected
4. All jump and special abilities are server-validated
5. Server can override client state when necessary

## Troubleshooting Network Issues

1. **Rubberbanding:**
   - May indicate too low `serverValidationThreshold`
   - Consider increasing threshold or improving client prediction

2. **Delayed Response:**
   - May indicate high `clientUpdateInterval`
   - Consider decreasing interval for more frequent updates

3. **Jerky Remote Vehicles:**
   - May indicate low `visualSmoothingSpeed` or high `stateUpdateInterval`
   - Adjust smoothing or increase update frequency 
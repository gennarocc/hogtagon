# Cinematic Trailer Camera System

A comprehensive camera system for creating dramatic trailer sequences in Unity with Cinemachine.

## Features

- **Jump Detection**: Automatically triggers cinematic sequences when the car performs high jumps
- **Dynamic Camera Switching**: Seamlessly transitions between 5 different cinematic angles
- **Slow Motion Effects**: Dramatic time scaling during key moments
- **Zoom Effects**: Dynamic FOV changes for dramatic impact
- **Background Blur**: Depth of field effects to focus attention on the car
- **Under-Car Rotation**: Unique camera angle that rotates under the jumping car
- **Post Processing Integration**: Automatic motion blur and depth of field effects

## Camera Sequence

The system executes a 5-phase cinematic sequence:

1. **Jump Zoom** - Close-up zoom with slow motion as the car leaves the ground
2. **Orbit Rotation** - Wide orbital shot with background blur
3. **Under Car Rotation** - Dramatic low-angle shot from underneath the car
4. **Dynamic Chase** - Fast-paced chase camera with motion blur
5. **Slow Motion Chase** - Final dramatic slow-motion sequence

## Setup Instructions

### 1. Prerequisites
- Unity 2021.3 or later
- Cinemachine package installed
- Universal Render Pipeline (URP) with Post Processing

### 2. Camera Setup
Create 5 Cinemachine Virtual Cameras with these names:
- `CinematicCamera_JumpZoom`
- `CinematicCamera_OrbitRotation` 
- `CinematicCamera_UnderCarRotation`
- `CinematicCamera_DynamicChase`
- `CinematicCamera_SlowMotionChase`

### 3. Component Setup

#### Step 1: Add the Main Controller
1. Create an empty GameObject named "CinematicTrailerSystem"
2. Add the `CinematicTrailerController` script
3. Add the `CinematicCameraEffects` script to the same GameObject

#### Step 2: Add Setup Guide (Optional)
1. Add the `CinematicSetupGuide` script to the same GameObject
2. Use the inspector buttons to auto-configure the system

### 4. Inspector Configuration

#### CinematicTrailerController Settings:

**Camera References:**
- Drag each of your 5 cinematic cameras to the corresponding slots
- Assign your normal player camera (FreeLook)
- Set the car target (the Transform to follow)

**Trigger Settings:**
- `Auto Trigger On Jump`: Enable automatic activation when car jumps
- `Manual Trigger`: Enable manual control via keyboard
- `Trigger Key`: Key to manually start/stop sequences (default: C)

**Jump Detection:**
- `Jump Velocity Threshold`: Minimum upward velocity to trigger (default: 5)
- `Ground Check Distance`: Distance to check for ground contact (default: 2)
- `Ground Layer Mask`: Layers considered as ground
- `Air Time Threshold`: Minimum air time before triggering (default: 0.3s)

**Camera Effects:**
- `Zoom In FOV`: Field of view during zoom effect (default: 25)
- `Normal FOV`: Standard field of view (default: 60)
- `Follow Distance`: Distance cameras maintain from car (default: 8)

**Slow Motion:**
- `Slow Motion Time Scale`: Time scale during slow motion (default: 0.3)
- `Slow Motion Duration`: How long slow motion lasts (default: 2s)

**Sequence Timing:**
- `Total Sequence Duration`: Maximum length of entire sequence (default: 8s)
- `Camera Hold Duration`: Time each camera is active (default: 1.5s)

### 5. Post Processing Setup

#### Create a Post Process Volume:
1. Create a Global Volume in your scene
2. Create a Volume Profile asset
3. Add these effects to the profile:
   - **Depth of Field** (for background blur)
   - **Motion Blur** (for speed effects)
4. Assign the Volume to the `Post Process Volume` field

#### Recommended Settings:
- **Depth of Field**: Mode = Gaussian, Focus Distance = 8-12
- **Motion Blur**: Intensity = 0.3-0.7

### 6. Camera Component Configuration

For each cinematic camera, recommended components:

#### Jump Zoom Camera:
- **Body**: Transposer or 3rd Person Follow
- **Aim**: Composer
- **Noise**: Basic Multi-Channel Perlin (for camera shake)

#### Orbit Rotation Camera:
- **Body**: Orbital Transposer
- **Aim**: Composer
- Set orbital speed and radius for smooth rotation

#### Under Car Rotation Camera:
- **Body**: Transposer
- Set Follow Offset to (0, -2, -8) for under-car view
- **Aim**: Look At target

#### Dynamic Chase Camera:
- **Body**: 3rd Person Follow
- Set for dynamic following with shoulder offset
- **Aim**: Composer with screen composition

#### Slow Motion Chase Camera:
- **Body**: Transposer
- Set for dramatic trailing shot
- **Aim**: Look At with damping

## Usage

### Automatic Mode (Recommended)
1. Enable `Auto Trigger On Jump` in the CinematicTrailerController
2. Drive your car and perform jumps
3. The system automatically detects significant jumps and triggers the sequence

### Manual Mode
1. Enable `Manual Trigger` in the CinematicTrailerController
2. Press the trigger key (default: C) to start/stop sequences
3. Useful for testing and recording specific shots

### Scripting Integration

```csharp
// Start a cinematic sequence from code
cinematicController.StartCinematicSequence();

// End the sequence early
cinematicController.EndCinematicSequence();

// Check if sequence is active
if (cinematicController.IsInCinematicMode)
{
    // Do something during cinematic
}

// Trigger specific effects
cameraEffects.TriggerCameraShake(intensity: 2f, duration: 1f);
cameraEffects.SetFOVOverride(targetFOV: 30f, transitionTime: 2f);
```

## Customization

### Adding New Camera Phases
1. Create a new cinematic camera in the scene
2. Add it to the `cinematicCameras` list
3. Create a new `ExecuteCustomPhase()` coroutine method
4. Add it to the `ExecuteCinematicSequence()` method

### Modifying Transition Timing
- Adjust `cameraHoldDuration` for individual camera time
- Modify `transitionDuration` for blend speed between cameras
- Change `totalSequenceDuration` for overall sequence length

### Custom Trigger Conditions
Override the `DetectJump()` method to implement custom trigger logic:
```csharp
private void DetectJump()
{
    // Your custom jump detection logic here
    if (customJumpCondition)
    {
        StartCinematicSequence();
    }
}
```

## Troubleshooting

### Common Issues:

**Cameras not switching properly:**
- Check that all cameras have unique priorities
- Ensure camera references are properly assigned
- Verify Cinemachine Brain is on the main camera

**Jump detection not working:**
- Verify car has a Rigidbody component
- Check ground layer mask settings
- Adjust jump velocity threshold

**Post processing effects not appearing:**
- Ensure URP is properly configured
- Check that Volume Profile is assigned
- Verify Volume has correct layer/trigger settings

**Sequence starts too frequently:**
- Increase `jumpVelocityThreshold`
- Adjust `airTimeThreshold`
- Add cooldown logic if needed

### Performance Optimization:
- Disable unused camera effects when not in cinematic mode
- Use object pooling for camera shake noise settings
- Optimize post processing settings for target platform

## API Reference

### CinematicTrailerController
- `StartCinematicSequence()`: Begin the cinematic sequence
- `EndCinematicSequence()`: End the sequence and return to normal camera
- `SetCarTarget(Transform)`: Change the target being followed
- `IsInCinematicMode`: Property indicating if sequence is active
- `CurrentState`: Current phase of the cinematic sequence
- `SequenceProgress`: 0-1 progress through the sequence

### CinematicCameraEffects
- `TriggerCameraShake(intensity, duration)`: Add camera shake effect
- `SetFOVOverride(targetFOV, transitionTime)`: Override camera FOV
- `ApplyImpactEffect(impactPoint, intensity)`: Distance-based shake effect
- `SetCinematicLens(fov, nearClip, farClip)`: Configure camera lens
- `EnableDynamicFOV(enable)`: Toggle speed-based FOV changes

## Tips for Best Results

1. **Test in different scenarios**: Try various jump heights and speeds
2. **Adjust timing per camera**: Some shots work better with longer/shorter duration
3. **Use environment**: Position cameras to showcase the environment
4. **Sound design**: Add audio cues that sync with camera transitions
5. **Lighting**: Ensure good lighting for all camera angles
6. **Multiple takes**: The system allows for consistent, repeatable shots

## Version History

- v1.0: Initial release with 5-camera sequence system
- Features: Jump detection, slow motion, zoom effects, background blur 
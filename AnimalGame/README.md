# AnimalGame — Robot Map Movement Prototype

Open `Assets/Scenes/SampleScene.unity` and press Play. The demo creates itself at runtime.

## Controls

- `W` / `Up Arrow`: move forward
- `S` / `Down Arrow`: reverse
- `A` / `Left Arrow`: turn left
- `D` / `Right Arrow`: turn right

### Gamepad

- Left stick up/down: move forward/reverse
- Right stick left/right: steer

The stick uses an adjustable dead zone on the Robot Marker prefab. Turning now has independent angular acceleration and deceleration, so holding a direction gradually builds toward the maximum turn speed.

The robot's authoritative state is a 2D map position plus a facing direction. `RobotMover` is intentionally independent from the visual marker so a height-field traversal evaluator can be inserted next without rewriting the controls.

Steering has a constant angular speed. Movement uses separate launch acceleration, running acceleration, coasting deceleration, and active braking; all four values can be tuned on the Robot Marker prefab.

## Prefabs

- `Assets/Prefabs/Resources/Robot/RobotMarker.prefab`: movement and marker tuning
- `Assets/Prefabs/Resources/Camera/RobotCamera.prefab`: camera size, position damping, and rotation damping

The prefabs are generated automatically after scripts compile. They can also be regenerated from `Animal Game > Rebuild Robot Map Prefabs`.


### Core Layers

1. **Flight Physics & Cascaded PID Control**
   - **`FlightControlSystem.cs`**: Orchestrates the multi-tier cascade controller. Converts player or AI velocity demands into tilt angles, angles into angular rates, and rates into motor corrections.
   - **`VelocityToAngleController.cs`**: Translates planar velocity error into pitch and roll angles with braking deadbands.
   - **`AngleToRateController.cs`**: Translates tilt error into target pitch/roll rates with level-recovery multipliers.
   - **`RateController.cs`**: Computes fast rate corrections for pitch, roll, and yaw.
   - **`AltitudeController.cs`**: Regulates vertical climb/sink rates with tilt compensation (maintains altitude during steep banks).
   - **`DroneHardware.cs`**: Directly applies 4-corner motor thrust forces (`FL`, `FR`, `BL`, `BR`) and differential yaw torque to the `Rigidbody`.

2. **Autonomous Navigation & Obstacle Avoidance**
   - **`DroneNPCFollowTarget.cs`**: Directs the drone toward dynamic waypoints.
   - **Multi-Directional Raycasting**: Evaluates candidate directions across yaw and pitch angles when obstacles are detected.
   - **Anti-Orbit Guard**: Snaps orientation if the waypoint is passed, eliminating circular orbital drift.
   - **Collider Filtering**: Uses `ignoredColliders` to ignore squadmates and the target, preventing false avoidance triggers.
   - **Aerodynamic Banking**: Banks into turns based on yaw angular delta.

3. **Squad Coordination & Decentralized Brains**
   - **`DroneBrain` (inside `DroneDirector.cs`)**: Attached to every drone GameObject.
     - **Distance-Adaptive Catch-Up**: The further a drone is from its formation slot, the faster it flies (`adaptiveSpeed` scales with distance).
     - **Wingman Communication**: Drones communicate with peer brains. If a drone is in position while a wingman is lagging, it moderates lead speed to maintain squad cohesion.
   - **`DroneDirector.cs`**: Oversees high-level mission phases, dynamic target acquisition, and slot allocation.

---

## 2. Squad Behaviors & Combat Phases

### Distance-Based Following vs. Combat
- **`distToTarget > engagementDistance` (Default: 8.5m)**:
  - Drones fly in formation trailing behind the moving target (`FollowTarget`).
  - Speeds automatically match target speed with catch-up acceleration for lagging drones.
- **`distToTarget <= engagementDistance`**:
  - Drones transition into randomized combat actions.

### Randomized Combat Tactics (No Fixed Timers)
- **Coordinated Attack (40% chance)**: Multi-vector converging strikes (Pincer, Feint & Flank, High-Low Strike, Blitz Assault).
- **Surround / Encirclement (35% chance)**: Dynamic tactical formations cycling through 12 unique spatial profiles (Echelon, Overwatch, Asymmetric Pincer, Cross-Diagonal, etc.).
- **Distract & Flank (25% chance)**: One drone feints and draws enemy focus while wingmen flank rear blind spots.
- **Disengage & Reposition**: Breakout egress maneuvers to reset attack vectors.

### Synchronized Victory Formations
Triggered immediately when the target is eliminated:
- **Even Drone Counts (2, 4, 6)**: Automatically selects **Parallel Formation** (evenly spaced line-abreast).
- **Odd Drone Counts (3, 5, 7)**: Automatically selects **V Formation** (lead tip with symmetric wing pairs).
- **Optimal Slot Assignment**: Nearest drone takes the nearest slot without crossing paths.
- **2-Second Synchronization**: Travel speeds are scaled so all drones arrive within 1.2–1.5 seconds and stabilize into a cinematic hover.

---

## 3. Controls & Debug Interfaces

### Flight Controls (Manual Mode)
| Key | Action |
|---|---|
| **W / S** | Pitch Forward / Backward |
| **A / D** | Roll Left / Right |
| **Q / E** | Yaw Turn Left / Right |
| **Shift / Ctrl** | Throttle Climb / Descend |

### In-Game Telemetry & Tuning Panels
- **`F1`**: Toggle **Drone Runtime UI** (live speed, altitude, vertical speed graphs, motor thrust percentages, and kinematics).
- **`F2`**: Toggle **PID Tuner Panel** (runtime reflection-based sliders for `Kp`, `Ki`, `Kd`, and saturation limits on all active controllers).

---

## 4. Inspector Setup & Configuration

### `DroneDirector` Component Setup
Attach `DroneDirector` to an empty manager GameObject in your scene:

1. **Target**: Assign your enemy/target transform (or leave blank to auto-detect by `TargetCube`, `Target1`, or `targetTag`).
2. **Drone Spawner**:
   - `Drone Prefab`: Assign your drone prefab (or leave empty to duplicate a drone found in the scene).
   - `Drone Count`: Set squad size (e.g., `2`, `3`, `4`, `5`).
   - `Spawn On Start`: Checked (`true`).
3. **Distance Settings**:
   - `Engagement Distance`: `8.5`
   - `Follow Distance`: `6.0`
   - `Follow Height`: `1.8`
   - `Follow Formation Spacing`: `4.0`
4. **Speeds**:
   - `Follow Speed`: `6.5`
   - `Max Follow Speed`: `11.0`
   - `Follow Acceleration`: `7.0`
   - `Cruise Speed`: `5.5`
   - `Attack Speed`: `10.0`
5. **Victory Settings**:
   - `Victory Formation Spacing`: `4.2`
   - `Victory Formation Height`: `2.5`
   - `Formation Arrival Tolerance`: `0.35`
   - `Victory Travel Timeout`: `2.0`
   - `Cinematic Hold Duration`: `4.0`

### Drone GameObject Components
Ensure the drone template/prefab has:
- `Rigidbody` (Mass: `1.0`, Interpolate: `Interpolate`, Collision Detection: `Continuous`)
- `DroneHardware` (Motor transform references for `frontLeft`, `frontRight`, `backLeft`, `backRight`)
- `DroneInputs` (`isAIControlled = true` when spawned by Director)
- `FlightControlSystem` (Cascaded PID configurations)
- `DroneNPCFollowTarget` (Movement and obstacle avoidance)
- `DroneBrain` (Auto-attached and initialized by `DroneDirector`)

---

## 5. Project Directory Layout

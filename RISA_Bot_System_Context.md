# RISA-Bot: Complete System Context & Technical Documentation

> **Document Purpose:** This document serves as a comprehensive system context reference for AI agents, developers, and operators working with **RISA-Bot** (ROS 2 Intelligent System Autonomy — Competition Robot).

---

## 1. Executive Summary & System Overview

**RISA-Bot** is an autonomous competition robot system built on **ROS 2 Humble** (and Horizon TROS), running on an **RDK X5 (Horizon Sunrise board / Ubuntu 22.04 LTS ARM64)** computing platform. It is engineered to navigate a 6.4m × 4m multi-challenge competition track in full autonomy across multiple laps.

### Key Capabilities:
- **Modular ROS 2 Architecture:** Priority-ordered state machine controlling multi-sensor autonomy.
- **Sensor Fusion:** 2D LiDAR (obstacle detection & wall following), RGB Camera (line tracking & traffic lights), IMU (incline/ramp detection), and wheel encoders (precise odometry).
- **Edge AI Acceleration:** Real-time YOLOv5 object detection running on the RDK X5 BPU (Brain Processing Unit) via INT8 quantization.
- **Native Desktop App & Web Dashboard:** Windows WPF Companion App (`net10.0-windows`) and web services for telemetry, live camera streams with perception overlays, 2D SLAM mapping, dynamic parameter tuning, and remote AI model management.
- **Multi-Robot Deployment Tooling:** Paramiko-based scripts (`tools/deploy_to_robots_paramiko.py`, `tools/bulk_setup_robots.py`) that drive several robots at once over SSH (model upload, git pull, build, verify).

---

## 2. Hardware Architecture & Components

* **Single Board Computer (SBC):** RDK X5 (Horizon Sunrise / Ubuntu 22.04 ARM64) with integrated BPU AI hardware accelerator.
* **Motor Interface Board:** Rosmaster Motor Controller Board communicating over serial (`/dev/myserial` at 115200 baud) managed by `servo_controller.py`.
* **Primary Sensors:**
  * **2D LiDAR:** YDLiDAR Tmini Plus driven by `ydlidar_ros2_driver` publishing to `/scan`.
  * **RGB Camera:** Astra Mini Camera driven by `ros2_astra_camera` publishing to `/camera/color/image_raw`.
  * **Telemetry Sensors:** Onboard IMU (pitch, roll, yaw) and optical wheel encoders for odometry (`/odom`).
  * **Teleoperation:** Bluetooth/USB Gamepad support via standard ROS `joy` node.

---

## 3. Competition Track & The 9 Autonomous Challenges

The competition track is 6.4m × 4m. The robot navigates counter-clockwise across **2 Laps**:

```
Lap 1: START ──► Lane Follow ──► Obstruction ──► Roundabout ──► Boom Gate 1 ──► Tunnel ──► Boom Gate 2 ──► Hill ──► Bumper ──► Traffic Light ──► START (Lap 2)

Lap 2: Lane Follow ──► Obstruction ──► Roundabout (Gate Closed -> Inner Path) ──► Parallel Park ──► Drive ──► Perpendicular Park ──► FINISH
```

### Challenge Technical Specifications:

1. **Obstruction Avoidance (Challenge 1 - LiDAR):**
   * *Node:* `obstruction_avoidance.py`
   * *Mechanism:* Detects obstacles (0.8m × 0.4m block) within range (`detect_dist`). Executes a timed 3-phase steering maneuver: Steer Out $\rightarrow$ Pass $\rightarrow$ Return to Lane.
2. **Roundabout Navigation (Challenge 2 - Camera):**
   * *Node:* `auto_driver.py` + `line_follower_camera.py`
   * *Mechanism:* Follows painted circular lane lines. Routes to Exit 1 (Tunnel) on Lap 1, and Exit 2 (Parking area) on Lap 2 when Gate 1 closes.
3. **Tunnel Navigation (Challenge 3 - LiDAR Wall Follower):**
   * *Node:* `tunnel_wall_follower.py`
   * *Mechanism:* Operates in dark corridors where camera vision fails. Fits each corridor wall from LiDAR points with RANSAC, then steers with PD control on lateral centering error (`kp`) plus wall heading alignment (`kp_heading`). Publishes its suggestion on `/tunnel_cmd_vel`.
4. **Boom Gate Barrier (Challenge 4 - LiDAR + Camera):**
   * *Node:* `boom_gate_detector.py`
   * *Mechanism:* Combines a LiDAR forward-arc check for the barrier bar with HSV segmentation of the red bar in the lower camera ROI. Stops on closed gate and auto-resumes after consecutive open frames (`hysteresis`).
5. **Hill Ramp Climbing (Challenge 5 - IMU + Camera):**
   * *Node:* `auto_driver.py`
   * *Mechanism:* Monitored via `/imu/pitch`. Scales motor driving power up ramps while maintaining camera lane center.
6. **Bumper Traversal (Challenge 6 - Camera):**
   * *Node:* `line_follower_camera.py`
   * *Mechanism:* Crosses physical ground bumps smoothly under continuous camera line tracking.
7. **Traffic Light Recognition (Challenge 7 - Camera HSV / YOLO):**
   * *Node:* `traffic_light_detector.py` / `signage_detector.py`
   * *Mechanism:* HSV color contour & circular blob detection for Red, Yellow, and Green lights. Full stop on Red/Yellow; auto-proceed on Green.
8. **Parallel Parking (Challenge 8 - Odometry + LiDAR):**
   * *Node:* `parking_controller.py`
   * *Mechanism:* Measures slot (0.75m deep), executes state-driven reverse parking maneuver, waits 3 seconds (competition requirement), and re-enters lane.
9. **Perpendicular Parking (Challenge 9 - Odometry + LiDAR):**
   * *Node:* `parking_controller.py`
   * *Mechanism:* 90° turn into 0.4m wide slot, reverses until LiDAR sees back wall, waits 3 seconds, and completes competition run.

---

## 4. Software Architecture & ROS 2 Control Loop

### Control Chain:
```
Sensors (Camera, LiDAR, IMU) 
      │
      ▼
Perception Nodes (line_follower, obstruction, tunnel, traffic_light, signage)
      │ (Publish facts & candidate velocities)
      ▼
auto_driver.py (Priority State Machine @ 50 Hz)
      │
      ▼ /cmd_vel_auto_raw
cmd_safety_controller.py (Limits & E-Stop Enforcement)
      │
      ▼ /cmd_vel_auto
servo_controller.py (Rosmaster Serial Bridge) ──► Motors
```

### State Priority Hierarchy (`auto_driver.py`):
Evaluated top to bottom every 50 Hz tick; the first match wins. Full trigger table in `ARCHITECTURE.md` §3.

1. `MANUAL` — Joystick manual control active (`/auto_mode == false`)
2. `EMERGENCY_STOP` — E-stop active
3. `TRAFFIC_LIGHT` — Red/yellow seen or red latch held; waits for green
4. `BOOM_GATE` — Waiting for gate opening
5. `OBSTRUCTION` — LiDAR lateral avoidance active
6. `REVERSE_ADJUST` — Reversing away from front obstacle
7. `ROUNDABOUT` — Time-gated lane follow after the obstruction clears
8. `PARKING_IDLE` / `PARKING_PLAYBACK` — Lap 2 parking via recorded-trajectory playback
9. `TUNNEL` — Wall-following mode
10. `HILL` / `HILL_DESCENT` — Ramp climbing / controlled descent with pitch scaling
11. `LANE_RECOVERY` — Lane lost; stops in place
12. `LANE_FOLLOW` — Default camera line tracking

`FINISHED`, `PARALLEL_PARK` and `PERPENDICULAR_PARK` exist in the enum but are not selected by the current tick logic: after parking playback the robot resumes `LANE_FOLLOW`.

---

## 5. BPU AI Inference Pipeline

Edge AI sign and traffic light detection runs on the RDK X5 BPU using custom YOLOv5s models:

```
Camera (BGR) ──► Resize (640x640) ──► Convert NV12 Layout ──► BPU Forward Pass (pyeasy_dnn) ──► CPU NMS & Hysteresis ──► ROS 2 Topics
```

* **Compilation Flow:** PyTorch (`.pt`) $\rightarrow$ ONNX (`best.onnx`) $\rightarrow$ `patch_onnx_resize.py` $\rightarrow$ Horizon BPU Compiler (`hb_mapper` in Docker) $\rightarrow$ Quantized INT8 Model (`.bin`).
* **Deploy Target:** Uploaded directly to `/home/sunrise/risabot_signs_640x640_nv12.bin` on the robot.

---

## 6. Services & Companion Application Ecosystem

### Robot Network Listener Ports:
* **Port 8000:** Provisioning Portal & Launch Control Backend (`tools/wifi_provisioning/backend/app.py` - FastAPI/uvicorn). Manages ROS lifecycle (`risabot.service` start/stop), WiFi provisioning, and AI model upload/info/rollback.
* **Port 8080:** Web Dashboard (`dashboard.py`). Provides web endpoints for state telemetry, parameter get/set, and sensor views.
* **Port 1984:** `go2rtc` low-latency streaming server.
* **Port 1985:** `ros2go2rtc_bridge.py` MJPEG stream provider.

### Windows Desktop Companion App (`RisaBotApp`):
A WPF (.NET 10) application offering:
* **Auto Discovery & Pairing:** Discovers robots over local network via mDNS.
* **Live Perception Overlays:** Real-time camera streaming with line follower debug, obstacle boxes, traffic light status, and YOLO signage boxes.
* **2D LiDAR Visualizer:** Live point cloud view (`LidarView.cs`).
* **2D SLAM Map Viewer:** Live occupancy grid mapping rendered from `slam_toolbox` (`SlamView.xaml`).
* **Parameter Tuning Drawer:** Read/write ROS node parameters on the fly.
* **AI Model Manager:** Remote upload, validation, atomic swap, and rollback of BPU `.bin` models.
* **Telemetry & Data Logger:** Real-time speed, distance, IMU pitch, and state logging.

---

## 7. Development & Deployment Reference

### Workspace Directory Structure:
```
~/risabotcar_ws/src/RISA-bot/
├── ARCHITECTURE.md          # Complete node graph & state tables
├── CLAUDE.md                # AI developer guide & conventions
├── README.md                # Setup & build reference
├── RisaBotApp/              # Windows WPF Companion App source code
├── Guide/                   # Challenge breakdowns, tuning, and odometry guides
├── Workshop/                # Educational ROS 2 learning modules
├── src/
│   ├── risabot_automode/   # Main autonomy, state machine, perception & dashboard
│   ├── control_servo/      # Rosmaster hardware serial interface
│   ├── risabot_slam/       # slam_toolbox configuration
│   ├── ros2_astra_camera/  # Astra Mini RGB Camera driver
│   └── ydlidar_ros2_driver/# YDLiDAR driver & SDK
└── tools/
    ├── deploy_to_robots_paramiko.py  # Multi-robot deploy over SSH
    ├── bulk_setup_robots.py          # Multi-robot setup over SSH
    ├── install.sh           # One-click environment installer
    └── wifi_provisioning/   # Backend FastAPI portal (Port 8000)
```

### Essential Build Commands (Over SSH on Robot):
```bash
# Workspace aliases
cb         # Full rebuild (colcon build --symlink-install)
cbp <pkg>  # Fast rebuild of a single package (e.g. cbp risabot_automode)
cbc        # Clean rebuild of Python packages
sos        # Re-source workspace environment (source install/setup.bash)

# Launch Commands
ros2 launch risabot_automode bringup.launch.py      # Primary: full stack + dashboard + SLAM (what risabot.service / the app starts)
ros2 launch risabot_automode bringup.launch.py slam:=false   # Same, without SLAM to save CPU
ros2 launch risabot_automode competition.launch.py  # Older all-in-one launch: dashboard + camera bridge, but no SLAM
run_risabot                                         # Each core node in its own terminal tab (debugging)
```

---
*Generated for RISA-Bot System Architecture & AI Context Integration.*

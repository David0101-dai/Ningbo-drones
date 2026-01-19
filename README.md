# Ningbo Drone Simulator (Unity + Cesium) — LLM-Ready Multi-UAV Planning

A Unity-based UAV flight simulator for **multi-drone navigation** in **real-world 3D geospatial environments** using **Cesium for Unity**.  
It supports **waypoint route following (WGS84 LLH)**, **local obstacle avoidance**, **multi-view camera tracking (Cinemachine)**, and a **Planning Mode** where you can pick **Start/End** points on the map to generate a **runtime route** and switch a selected drone onto it.

A lightweight **Python FastAPI gateway** is included to connect Unity with a rule-based “mock LLM” now, and can be upgraded later to call a real LLM API (OpenAI / Zhipu / Qwen / local models) without changing the Unity execution layer.


## Features

- **Geospatial 3D World (Cesium for Unity)**
  - Streaming terrain + buildings and 3D tiles (city-scale scene).
- **Multi-UAV Simulation**
  - Multiple drones fly simultaneously.
- **Global Waypoint Navigation**
  - Routes: `Waypoints_A`, `Waypoints_B`, `Waypoints_C`, `Waypoints_Runtime`
  - Waypoints are densified for smoother movement.
- **Local Obstacle Avoidance**
  - Forward sensing + local grid detour planning (A* on a local grid).
- **Camera System**
  - Side / Rear chase / Top-down views (Cinemachine)
  - Switch tracked drone with A/D.
- **Planning Mode (UI)**
  - Pause the fleet, enter top-down view, pick Start/End on the map
  - Automatically lifts picked points (e.g., **+25m**)
  - Builds route under `Waypoints_Runtime` and applies it to the selected drone
- **LLM-Ready Control Pipeline**
  - Unity -> Python gateway (`/command`) -> JSON command list -> Unity executes only whitelisted commands

---

## Tech Stack

- **Unity** (recommended 2021/2022 LTS)
- **Cesium for Unity**
- **Cinemachine**
- **TextMeshPro**
- **Python 3.9+**
- **FastAPI + Uvicorn + Pydantic**

---

## Repository Layout (Typical)

- `Assets/Scripts/`
  - `DroneGeoNavigator.cs` — waypoint following using Cesium LLH (lon/lat/height)
  - `DroneGridAvoidance.cs` — local obstacle detection + grid detour
  - `SwitchView.cs`, `VcamMouseZoom.cs` — camera switching & zoom
  - `DroneCommandCenter.cs` — executes whitelisted actions (pause/speed/route)
  - `DroneFleetController.cs` — apply commands to all drones
  - `LLMManagerHttp.cs` — Unity HTTP client for the Python gateway
  - `PlanningModeController.cs` — enter/exit planning mode
  - `MapPickController.cs` — click-to-pick Start/End (with altitude offset)
  - `RuntimeWaypointsBuilder.cs` — generate runtime waypoints (WP1..WPn)
  - `ApplyRuntimeRouteController.cs` — apply runtime route to selected drone
  - `PlanningPanelUIBinder.cs` — auto binds UI buttons/dropdowns
- `llm_gateway/`
  - `gateway.py` — FastAPI server that returns JSON commands

---

## Quick Start

### 1) Run the Python Gateway

Open a terminal in your gateway folder (example: `llm_gateway/`):

```bash
pip install fastapi uvicorn pydantic
uvicorn gateway:app --host 127.0.0.1 --port 8000 --reload
````

Health check:

* Open this URL in a browser:

  * `http://127.0.0.1:8000/health`
* Expected output:

  * `{"ok": true}`

### 2) Unity: Connect to the Gateway

In the Unity scene, select the GameObject with `LLMManagerHttp` and set:

* `Gateway Url` = `http://127.0.0.1:8000/command`

Press Play, type a command in the UI input box, and click Send.

---

## Controls

### Camera / Drone Tracking (SwitchView)

* `1` = Side view
* `2` = Rear chase
* `3` = Top-down (city overview)
* `A` / `D` = Previous / Next drone (cycle)
* Mouse wheel = Zoom (if `VcamMouseZoom` is enabled)

### Planning Mode (UI Workflow)

Typical workflow:

1. **Pause All** (or Enter Planning Mode, which pauses the fleet)
2. Switch to **Top-down**
3. Click **Pick Start** and left-click on the map
4. Click **Pick End** and left-click on the map
5. Select the target drone (dropdown)
6. Click **Apply Runtime Route** to build `Waypoints_Runtime` and switch that drone to the new route
7. Resume / Exit Planning Mode

Picked points are lifted by a height offset (e.g., **+25m**) to avoid hugging the ground.

---

## Gateway Command Format (Current)

The gateway returns a JSON object:

```json
{
  "say": "…",
  "commands": [
    { "type": "pause", "drone": "current" },
    { "type": "resume", "drone": "current" },
    { "type": "pause_all" },
    { "type": "resume_all" },
    { "type": "set_speed", "drone": "current", "speed": 12.0 },
    { "type": "select_route", "drone": "current", "route": "Waypoints_B" }
  ]
}
```

Unity executes only supported command types (whitelisted) for safety.

---

## Known Limitations

* In **narrow urban corridors**, drones may deviate or trigger avoidance unexpectedly, depending on:

  * speed, densify step, smoothing lookahead, and avoidance sensing parameters
  * other scripts or physics components affecting transform/rigidbody
* Current runtime planning is a practical baseline; a full 3D global planner is a future upgrade.

---

## Roadmap (Next Phase)

* Add **true LLM integration** in `gateway.py`:

  * replace rule-based parsing with an LLM call
  * use structured tool/JSON outputs only
* Add a **scene state provider** (compact summary) to help LLM reason:

  * drone list + current positions + selected drone + start/end + constraints
* Multi-drone scheduling and conflict avoidance:

  * altitude layers, time staggering, route assignment policies
* Upgrade path planning:

  * combine vertical-clearance strategy with horizontal detours
  * iterative planning: propose → validate in Unity → retry


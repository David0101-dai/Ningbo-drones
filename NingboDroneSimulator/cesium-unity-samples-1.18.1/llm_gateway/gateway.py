from fastapi import FastAPI
from pydantic import BaseModel
from typing import Any, Dict, List, Optional
import re
import json

app = FastAPI()

class CommandRequest(BaseModel):
    text: str
    current_drone: Optional[str] = None
    routes: Optional[List[str]] = None
    scene_state: Optional[str] = None

@app.get("/health")
def health():
    return {"ok": True}

@app.post("/command")
def command(req: CommandRequest) -> Dict[str, Any]:
    text = (req.text or "").strip().lower()

    commands = []
    say_parts = []

    # Parse scene state
    if req.scene_state:
        try:
            scene = json.loads(req.scene_state)
            summary = scene.get("summary", {})
            print(f"[Gateway] Scene: {scene.get('droneCount', 0)} drones | "
                  f"Flying:{summary.get('flying',0)} "
                  f"Idle:{summary.get('idle',0)} "
                  f"Paused:{summary.get('paused',0)}")
        except Exception:
            pass

    # ========== QUERY ==========
    if "status" in text or "state" in text or "report" in text:
        return {"say": "Fleet status report", "commands": [{"type": "query_status"}]}

    if "list routes" in text or "available routes" in text:
        return {"say": "Available routes", "commands": [{"type": "query_routes"}]}

    if "info" in text or "where is" in text or "position" in text:
        return {"say": "Drone info", "commands": [{"type": "query_drone", "drone": "current"}]}

    # ========== FLEET ==========
    if "pause all" in text or "stop all" in text:
        return {"say": "Pausing all drones", "commands": [{"type": "pause_all"}]}

    if "resume all" in text or "start all" in text or "continue all" in text:
        return {"say": "Resuming all drones", "commands": [{"type": "resume_all"}]}

    # ========== GO TO ==========
    geo = re.search(r'(?:go\s*to|fly\s*to|navigate\s*to)\s+([0-9]+(?:\.[0-9]+))[,\\s]+([0-9]+(?:\.[0-9]+))(?:[,\\s]+([0-9]+(?:\.[0-9]+)?))?', text)
    if geo:
        lon = float(geo.group(1))
        lat = float(geo.group(2))
        h = float(geo.group(3)) if geo.group(3) else 50.0
        return {
            "say": f"Flying to ({lon}, {lat}, {h}m)",
            "commands": [{"type": "go_to", "drone": "current", "longitude": lon, "latitude": lat, "height": h}]
        }

    # ========== SINGLE DRONE ==========

    # pause / resume single
    if "pause" in text or "stop" in text:
        commands.append({"type": "pause", "drone": "current"})
        say_parts.append("Pausing current drone")
    elif "resume" in text or "continue" in text:
        commands.append({"type": "resume", "drone": "current"})
        say_parts.append("Resuming current drone")

    # speed - try multiple patterns
    speed_match = re.search(r'speed\s+([0-9]+(?:\.[0-9]+)?)', text)
    if not speed_match:
        speed_match = re.search(r'([0-9]+(?:\.[0-9]+)?)\s*(?:m/s|mps)', text)
    if not speed_match:
        speed_match = re.search(r'speed([0-9]+(?:\.[0-9]+)?)', text)
        
    if speed_match:
        sp_kmh = float(speed_match.group(1))
        sp_mps = sp_kmh / 3.6
        commands.append({"type": "set_speed", "drone": "current", "speed": sp_mps})
        say_parts.append(f"Speed set to {sp_kmh} km/h")


    # route
    route_candidates = req.routes if req.routes else []
    for r in route_candidates:
        r_clean = r.strip()
        r_short = r_clean.lower().replace("waypoints_", "")
        # Match "route b" or "route B"
        pattern = r'route\s+' + re.escape(r_short)
        if re.search(pattern, text):
            commands.append({"type": "select_route", "drone": "current", "route": r_clean})
            say_parts.append(f"Route changed to {r_clean}")
            break

    if not commands:
        return {
            "say": "Unknown command. Try: pause all / resume all / pause / resume / speed 20 / route b / status / info / go to 121.55 29.87",
            "commands": []
        }

    return {"say": ", ".join(say_parts), "commands": commands}
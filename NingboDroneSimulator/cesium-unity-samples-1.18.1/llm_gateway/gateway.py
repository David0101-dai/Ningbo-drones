from fastapi import FastAPI
from pydantic import BaseModel
from typing import Any, Dict, List, Optional
import re

app = FastAPI()

class CommandRequest(BaseModel):
    text: str
    current_drone: Optional[str] = None
    routes: Optional[List[str]] = None

@app.get("/health")
def health():
    return {"ok": True}

@app.post("/command")
def command(req: CommandRequest) -> Dict[str, Any]:
    text = (req.text or "").strip().lower()

    commands: List[Dict[str, Any]] = []
    say_parts: List[str] = []

    # ---------- pause all / resume all (优先于单机) ----------
    if any(k in text for k in ["全部暂停", "暂停所有", "全体暂停", "暂停全部", "pause all", "stop all"]):
        commands.append({"type": "pause_all"})
        say_parts.append("暂停全部无人机")

    elif any(k in text for k in ["全部继续", "继续所有", "全体继续", "继续全部", "resume all", "start all"]):
        commands.append({"type": "resume_all"})
        say_parts.append("继续全部无人机")

    # ---------- pause / resume (单机 current) ----------
    elif any(k in text for k in ["暂停", "pause", "stop"]):
        commands.append({"type": "pause", "drone": "current"})
        say_parts.append("暂停当前无人机")

    elif any(k in text for k in ["继续", "resume", "start"]):
        commands.append({"type": "resume", "drone": "current"})
        say_parts.append("继续当前无人机")

    # ---------- speed ----------
    m = re.search(r"(速度|speed)\s*([0-9]+(\.[0-9]+)?)", text, re.IGNORECASE)
    if not m:
        m = re.search(r"([0-9]+(\.[0-9]+)?)\s*(m/s|米每秒)", text)

    if m:
        sp = float(m.group(2) if m.lastindex and m.lastindex >= 2 else m.group(1))
        commands.append({"type": "set_speed", "drone": "current", "speed": sp})
        say_parts.append(f"速度设为 {sp}")

    # ---------- route ----------
    route_candidates = ["Waypoints_A", "Waypoints_B", "Waypoints_C", "Waypoints_Runtime"]
    if req.routes:
        route_candidates = req.routes

    for r in route_candidates:
        r_low = r.lower()
        if r_low in text or r_low.replace("waypoints_", "") in text:
            commands.append({"type": "select_route", "drone": "current", "route": r})
            say_parts.append(f"切换路线到 {r}")
            break

    if not commands:
        return {
            "say": "我没识别出可执行的控制指令。你可以说：暂停/继续/速度12/切到Waypoints_B/暂停全部/继续全部",
            "commands": []
        }

    return {
        "say": "，".join(say_parts),
        "commands": commands
    }

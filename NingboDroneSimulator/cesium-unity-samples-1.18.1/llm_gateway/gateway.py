from fastapi import FastAPI
from pydantic import BaseModel
from typing import Any, Dict, List, Optional
import re

app = FastAPI()

class CommandRequest(BaseModel):
    text: str
    # Unity 会把当前相机跟踪的无人机名发来（可选）
    current_drone: Optional[str] = None
    # 可用路线名（可选，用来帮助 LLM 不乱写）
    routes: Optional[List[str]] = None

@app.get("/health")
def health():
    return {"ok": True}

@app.post("/command")
def command(req: CommandRequest) -> Dict[str, Any]:
    """
    先用“规则 mock”跑通链路：
    - 暂停/继续
    - 调速
    - 切路线
    后面你想换成任何 LLM，只要在这里把 logic 换成调用模型即可。
    """
    text = req.text.strip().lower()

    commands: List[Dict[str, Any]] = []
    say_parts: List[str] = []

    # pause / resume
    if any(k in text for k in ["暂停", "stop", "pause"]):
        commands.append({"type": "pause", "drone": "current"})
        say_parts.append("暂停当前无人机")

    if any(k in text for k in ["继续", "resume", "start"]):
        commands.append({"type": "resume", "drone": "current"})
        say_parts.append("继续当前无人机")

    # speed: 支持 “速度12” “speed 12” “12m/s”
    m = re.search(r"(速度|speed)\s*([0-9]+(\.[0-9]+)?)", req.text, re.IGNORECASE)
    if not m:
        m = re.search(r"([0-9]+(\.[0-9]+)?)\s*(m/s|米每秒)", text)
    if m:
        sp = float(m.group(2) if m.lastindex and m.lastindex >= 2 else m.group(1))
        commands.append({"type": "set_speed", "drone": "current", "speed": sp})
        say_parts.append(f"速度设为 {sp}")

    # route: “waypoints_b” / “b路线” / “切到waypoints_c”
    # 你项目里 routeName 就是 Waypoints_A/B/C/Runtime
    route_candidates = ["Waypoints_A", "Waypoints_B", "Waypoints_C", "Waypoints_Runtime"]
    if req.routes:
        route_candidates = req.routes

    for r in route_candidates:
        if r.lower() in text or r.lower().replace("waypoints_", "") in text:
            commands.append({"type": "select_route", "drone": "current", "route": r})
            say_parts.append(f"切换路线到 {r}")
            break

    if not commands:
        return {
            "say": "我没识别出可执行的控制指令。你可以说：暂停/继续/速度12/切到Waypoints_B",
            "commands": []
        }

    return {
        "say": "，".join(say_parts),
        "commands": commands
    }

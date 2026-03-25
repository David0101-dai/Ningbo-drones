from fastapi import FastAPI
from pydantic import BaseModel
from typing import Any, Dict, List, Optional
import re
import json
import os
from openai import OpenAI

app = FastAPI()

# ====== DeepSeek Config ======
DEEPSEEK_API_KEY = os.environ.get("DEEPSEEK_API_KEY", "sk-43df162c22d44b13976fda2651ec548e")
DEEPSEEK_MODEL = "deepseek-chat"

client = OpenAI(
    api_key=DEEPSEEK_API_KEY,
    base_url="<https://api.deepseek.com/v1>"
)


class CommandRequest(BaseModel):
    text: str
    current_drone: Optional[str] = None
    routes: Optional[List[str]] = None
    scene_state: Optional[str] = None


@app.get("/health")
def health():
    return {"ok": True, "mode": "hybrid", "llm": DEEPSEEK_MODEL}


# ====== Rule Engine (fast path) ======

def try_rule_engine(text: str, routes: List[str]) -> Optional[Dict[str, Any]]:
    """Try to match simple commands with rules. Returns None if no match."""

    # ══════════════════════════════════════
    #  Pause / Resume
    # ══════════════════════════════════════

    if "pause all" in text or "stop all" in text:
        return {"say": "Pausing all drones", "commands": [{"type": "pause_all"}]}

    if "resume all" in text or "start all" in text or "continue all" in text:
        return {"say": "Resuming all drones", "commands": [{"type": "resume_all"}]}

    # ★ 新增：单独的 "stop" 命令 ★
    if text.strip() == "stop":
        return {"say": "Pausing current drone", "commands": [{"type": "pause", "drone": "current"}]}

    if "pause" in text and "all" not in text and "sim" not in text:
        return {"say": "Pausing current drone", "commands": [{"type": "pause", "drone": "current"}]}

    # ★ 新增：单独的 "stop" 也匹配（非精确匹配场景）★
    if "stop" in text and "all" not in text and "sim" not in text and "drone" not in text:
        return {"say": "Pausing current drone", "commands": [{"type": "pause", "drone": "current"}]}

    if ("resume" in text or "continue" in text) and "all" not in text and "sim" not in text:
        return {"say": "Resuming current drone", "commands": [{"type": "resume", "drone": "current"}]}

    # ══════════════════════════════════════
    #  Simulation Speed (Time Scale)
    # ══════════════════════════════════════

    if "pause sim" in text or "pause simulation" in text:
        return {"say": "Simulation paused", "commands": [{"type": "sim_pause"}]}

    if "resume sim" in text or "resume simulation" in text:
        return {"say": "Simulation resumed", "commands": [{"type": "sim_resume"}]}

    # "sim speed 10" or "simulation speed 50" or "timescale 5"
    sim_speed_match = re.search(r'(?:sim(?:ulation)?\\s*speed|timescale|time\\s*scale)\\s+([0-9]+(?:\\.[0-9]+)?)', text)
    if sim_speed_match:
        scale = float(sim_speed_match.group(1))
        scale = min(max(scale, 0.5), 50.0)  # clamp 0.5x ~ 50x
        return {"say": f"Simulation speed set to {scale}x",
                "commands": [{"type": "sim_speed", "speed": scale}]}

    # "10x speed" or "set 5x" or just "50x"
    x_speed_match = re.search(r'(\\d+(?:\\.\\d+)?)\\s*x\\s*(?:speed)?', text)
    if x_speed_match and ("sim" in text or "speed" in text or "fast" in text or "slow" in text):
        scale = float(x_speed_match.group(1))
        scale = min(max(scale, 0.5), 50.0)
        return {"say": f"Simulation speed set to {scale}x",
                "commands": [{"type": "sim_speed", "speed": scale}]}

    # ══════════════════════════════════════
    #  Drone Speed (individual)
    # ══════════════════════════════════════

    speed_match = re.search(r'(?:drone\\s+)?speed\\s+([0-9]+(?:\\.[0-9]+)?)', text)
    if not speed_match:
        speed_match = re.search(r'speed([0-9]+(?:\\.[0-9]+)?)', text)
    if speed_match and "sim" not in text:
        sp_kmh = float(speed_match.group(1))
        sp_mps = sp_kmh / 3.6
        return {"say": f"Drone speed set to {sp_kmh} km/h",
                "commands": [{"type": "set_speed", "drone": "current", "speed": sp_mps}]}

    # ══════════════════════════════════════
    #  Algorithm / Solver Selection
    # ══════════════════════════════════════

    if "solomon" in text and ("algo" in text or "solver" in text or "use" in text or "select" in text or "switch" in text):
        return {"say": "Switched to Solomon I1 Insertion algorithm",
                "commands": [{"type": "set_solver", "solver": "Solomon I1 Insertion"}]}

    if ("nearest" in text or "neighbor" in text) and ("algo" in text or "solver" in text or "use" in text or "select" in text or "switch" in text):
        return {"say": "Switched to Nearest Neighbor algorithm",
                "commands": [{"type": "set_solver", "solver": "Nearest Neighbor"}]}

    if ("clarke" in text or "wright" in text or "saving" in text) and ("algo" in text or "solver" in text or "use" in text or "select" in text or "switch" in text):
        return {"say": "Switched to Clarke-Wright Savings algorithm",
                "commands": [{"type": "set_solver", "solver": "Clarke-Wright Savings"}]}

    if "list algo" in text or "list solver" in text or "available algo" in text or "what algo" in text:
        return {"say": "Listing available algorithms",
                "commands": [{"type": "list_solvers"}]}

    # ══════════════════════════════════════
    #  Speed Mode (Routing Strategy)
    # ══════════════════════════════════════

    if "efficiency" in text and ("mode" in text or "speed mode" in text or "strategy" in text):
        return {"say": "Speed mode set to Efficiency (25 m/s)",
                "commands": [{"type": "set_speed_mode", "mode": "Efficiency"}]}

    if "economy" in text and ("mode" in text or "speed mode" in text or "strategy" in text):
        return {"say": "Speed mode set to Economy (10 m/s)",
                "commands": [{"type": "set_speed_mode", "mode": "Economy"}]}

    if "balanced" in text and ("mode" in text or "speed mode" in text or "strategy" in text):
        return {"say": "Speed mode set to Balanced (15 m/s)",
                "commands": [{"type": "set_speed_mode", "mode": "Balanced"}]}

    # ══════════════════════════════════════
    #  Solomon Workflow: Import / Solve / Dispatch / Export
    # ══════════════════════════════════════

    if "import" in text and ("solomon" in text or "dataset" in text or "order" in text):
        return {"say": "Opening import dialog",
                "commands": [{"type": "import_orders"}]}

    if "solve" in text and ("route" in text or "plan" in text):
        return {"say": "Solving routes with current algorithm",
                "commands": [{"type": "solve_routes"}]}

    if "dispatch" in text and "all" in text:
        return {"say": "Dispatching all planned routes",
                "commands": [{"type": "dispatch_all"}]}

    if "dispatch" in text:
        return {"say": "Dispatching all planned routes",
                "commands": [{"type": "dispatch_all"}]}

    if "stop" in text and "drone" in text and "all" in text:
        return {"say": "Stopping all drones",
                "commands": [{"type": "stop_all_drones"}]}

    if "export" in text and ("report" in text or "csv" in text or "result" in text):
        return {"say": "Exporting mission report",
                "commands": [{"type": "export_report"}]}

    if "refresh" in text and "status" in text:
        return {"say": "Refreshing routing status",
                "commands": [{"type": "refresh_status"}]}

    # ══════════════════════════════════════
    #  Orders
    # ══════════════════════════════════════

    if "test order" in text or "random order" in text:
        return {"say": "Creating test order",
                "commands": [{"type": "create_test_order"}]}

    if "order status" in text or "order list" in text:
        return {"say": "Showing order status",
                "commands": [{"type": "order_status"}]}

    if "location status" in text or "locations" in text or "list points" in text:
        return {"say": "Showing location status",
                "commands": [{"type": "location_status"}]}

    if "clear orders" in text or "clear all" in text:
        return {"say": "Clearing all orders",
                "commands": [{"type": "clear_orders"}]}

    if "sample order" in text or "save sample" in text:
        return {"say": "Saving sample order file",
                "commands": [{"type": "save_sample_orders"}]}

    # ══════════════════════════════════════
    #  Mission Status Queries
    # ══════════════════════════════════════

    if "mission" in text and ("status" in text or "summary" in text or "report" in text):
        return {"say": "Showing mission status",
                "commands": [{"type": "mission_status"}]}

    if "routing" in text and ("status" in text or "summary" in text):
        return {"say": "Showing routing solution summary",
                "commands": [{"type": "routing_status"}]}

    if "dispatch" in text and "status" in text:
        return {"say": "Showing dispatch status",
                "commands": [{"type": "dispatch_status"}]}

    # ══════════════════════════════════════
    #  Route Selection (legacy)
    # ══════════════════════════════════════

    for r in routes:
        r_clean = r.strip()
        r_short = r_clean.lower().replace("waypoints_", "")
        pattern = r'route\\s+' + re.escape(r_short)
        if re.search(pattern, text):
            return {"say": f"Route changed to {r_clean}",
                    "commands": [{"type": "select_route", "drone": "current", "route": r_clean}]}

    return None  # No rule matched


# ====== LLM Path (smart path) ======

SYSTEM_PROMPT = """You are an AI drone fleet dispatcher for a delivery simulation in Ningbo, China.

You receive the current scene state and a user command. You must respond with a JSON object containing:
- "say": a brief human-readable response (in English)
- "commands": an array of command objects to execute

Available command types:

=== Drone Control ===
1.  {"type": "pause_all"} - Pause all drones
2.  {"type": "resume_all"} - Resume all drones
3.  {"type": "pause", "drone": "<name>"} - Pause a specific drone
4.  {"type": "resume", "drone": "<name>"} - Resume a specific drone
5.  {"type": "set_speed", "drone": "<name>", "speed": <m/s>} - Set individual drone speed (user speaks km/h, divide by 3.6)
6.  {"type": "select_route", "drone": "<name>", "route": "<route_name>"} - Assign a route to a drone
7.  {"type": "go_to", "drone": "<name>", "longitude": <lon>, "latitude": <lat>, "height": <h>} - Fly to coordinates

=== Simulation Speed ===
8.  {"type": "sim_speed", "speed": <multiplier>} - Set simulation time scale (0.5 to 50)
9.  {"type": "sim_pause"} - Pause entire simulation (Time.timeScale = 0)
10. {"type": "sim_resume"} - Resume simulation at previous speed

=== Algorithm / Solver ===
11. {"type": "set_solver", "solver": "<name>"} - Switch routing algorithm (e.g. "Solomon I1 Insertion", "Nearest Neighbor", "Clarke-Wright Savings")
12. {"type": "list_solvers"} - List all available routing algorithms

=== Speed Mode (Planning Speed) ===
13. {"type": "set_speed_mode", "mode": "<mode>"} - Set speed mode: "Efficiency" (25m/s), "Balanced" (15m/s), or "Economy" (10m/s)

=== Solomon Workflow ===
14. {"type": "import_orders"} - Open file dialog to import Solomon dataset
15. {"type": "solve_routes"} - Solve routes using current algorithm and speed mode
16. {"type": "dispatch_all"} - Dispatch all planned routes to drones
17. {"type": "stop_all_drones"} - Emergency stop all drones
18. {"type": "export_report"} - Export mission report to CSV files
19. {"type": "refresh_status"} - Refresh routing status display

=== Orders ===
20. {"type": "create_test_order"} - Create a random test delivery order
21. {"type": "order_status"} - Show all delivery orders
22. {"type": "create_order", "route": "pickup_name,delivery_name,description"} - Create order from named locations
23. {"type": "location_status"} - Show all location points
24. {"type": "import_orders"} - Import orders from file
25. {"type": "save_sample_orders"} - Save sample order file
26. {"type": "clear_orders"} - Clear all orders

=== Status Queries ===
27. {"type": "query_status"} - Query fleet status
28. {"type": "query_drone", "drone": "<name>"} - Query single drone status
29. {"type": "query_routes"} - List available routes
30. {"type": "mission_status"} - Show mission summary (deliveries, on-time rate, etc.)
31. {"type": "routing_status"} - Show routing solution summary
32. {"type": "dispatch_status"} - Show active dispatch status

Rules:
- Use "current" as drone name when the user doesn't specify which drone
- Speed values from user are typically in km/h; convert to m/s (divide by 3.6) for drone speed
- Simulation speed is a multiplier (1x = normal, 10x = fast, 50x = very fast)
- If the user asks a question, return empty commands array and answer in "say"
- Always respond with valid JSON only, no markdown, no explanation outside the JSON
- The typical workflow is: import_orders → solve_routes → dispatch_all → export_report
- When user says "go faster" or "speed up simulation", use sim_speed with a higher multiplier
- When user says "slow down", reduce sim_speed (minimum 0.5x)
"""


def call_llm(user_text: str, scene_state: str, current_drone: str, routes: List[str]) -> Dict[str, Any]:
    """Call DeepSeek LLM for complex/ambiguous commands."""

    context = f"""Current scene state:
{scene_state}

Current selected drone: {current_drone}
Available routes: {', '.join(routes)}

User command: {user_text}

Respond with valid JSON only."""

    try:
        response = client.chat.completions.create(
            model=DEEPSEEK_MODEL,
            messages=[
                {"role": "system", "content": SYSTEM_PROMPT},
                {"role": "user", "content": context}
            ],
            temperature=0.1,
            max_tokens=500,
            response_format={"type": "json_object"}
        )

        content = response.choices[0].message.content.strip()
        print(f"[LLM] Raw response: {content[:200]}")

        result = json.loads(content)

        if "say" not in result:
            result["say"] = ""
        if "commands" not in result:
            result["commands"] = []

        return result

    except json.JSONDecodeError as e:
        print(f"[LLM] JSON parse error: {e}")
        return {"say": f"LLM returned invalid JSON: {str(e)}", "commands": []}
    except Exception as e:
        print(f"[LLM] Error: {e}")
        # ★ 新增：LLM 失败时的 fuzzy fallback ★
        return _llm_fallback(user_text, str(e))


# ★ 新增函数 ★
def _llm_fallback(text: str, error: str) -> Dict[str, Any]:
    """When LLM is unavailable, try basic fuzzy matching as last resort."""

    text_lower = text.strip().lower()

    # Common single-word commands that should have been in rule engine
    simple_map = {
        "stop": {"say": "Stopping current drone", "commands": [{"type": "pause", "drone": "current"}]},
        "go": {"say": "Resuming current drone", "commands": [{"type": "resume", "drone": "current"}]},
        "start": {"say": "Resuming current drone", "commands": [{"type": "resume", "drone": "current"}]},
        "halt": {"say": "Pausing all drones", "commands": [{"type": "pause_all"}]},
        "status": {"say": "Querying status", "commands": [{"type": "query_status"}]},
        "help": {"say": "Available commands: stop, go, pause, resume, speed [N], sim speed [N]x, "
                        "solve routes, dispatch all, import orders, mission status, export report. "
                        "Note: LLM is currently offline.", "commands": []},
    }

    if text_lower in simple_map:
        result = simple_map[text_lower]
        result["say"] += f" (offline mode - LLM unavailable: {error})"
        print(f"[Fallback] Matched '{text_lower}' in offline mode")
        return result

    # Nothing matched at all
    return {
        "say": f"Sorry, I couldn't understand '{text}'. LLM is also unavailable ({error}). "
               f"Try: stop, pause, resume, speed 60, sim speed 10, solve routes, dispatch all",
        "commands": []
    }


# ====== Main Endpoint ======

@app.post("/command")
def command(req: CommandRequest) -> Dict[str, Any]:
    text = (req.text or "").strip().lower()

    routes = [r.strip() for r in (req.routes or [])]
    current_drone = req.current_drone or ""
    scene_state = req.scene_state or ""

    # Log scene state
    if scene_state:
        try:
            scene = json.loads(scene_state)
            summary = scene.get("summary", {})
            print(f"[Gateway] Scene: {scene.get('droneCount', 0)} drones | "
                  f"Flying:{summary.get('flying', 0)} "
                  f"Idle:{summary.get('idle', 0)} "
                  f"Paused:{summary.get('paused', 0)}")
        except Exception:
            pass

    # Step 1: Try rule engine first (fast path)
    rule_result = try_rule_engine(text, routes)
    if rule_result is not None:
        print(f"[Gateway] Rule engine matched: {text}")
        return rule_result

    # Step 2: Fall through to LLM (smart path)
    print(f"[Gateway] No rule match, calling LLM for: {text}")
    llm_result = call_llm(text, scene_state, current_drone, routes)
    return llm_result
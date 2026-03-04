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
    base_url="https://api.deepseek.com/v1"
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

    if "pause all" in text or "stop all" in text:
        return {"say": "Pausing all drones", "commands": [{"type": "pause_all"}]}

    if "resume all" in text or "start all" in text or "continue all" in text:
        return {"say": "Resuming all drones", "commands": [{"type": "resume_all"}]}

    if "pause" in text and "all" not in text:
        return {"say": "Pausing current drone", "commands": [{"type": "pause", "drone": "current"}]}

    if ("resume" in text or "continue" in text) and "all" not in text:
        return {"say": "Resuming current drone", "commands": [{"type": "resume", "drone": "current"}]}

    # speed
    speed_match = re.search(r'speed\s+([0-9]+(?:\.[0-9]+)?)', text)
    if not speed_match:
        speed_match = re.search(r'speed([0-9]+(?:\.[0-9]+)?)', text)
    if speed_match:
        sp_kmh = float(speed_match.group(1))
        sp_mps = sp_kmh / 3.6
        return {"say": f"Speed set to {sp_kmh} km/h", "commands": [{"type": "set_speed", "drone": "current", "speed": sp_mps}]}
    
    # orders
    if "test order" in text or "random order" in text:
        return {"say": "Creating test order", "commands": [{"type": "create_test_order"}]}

    if "order status" in text or "order list" in text:
        return {"say": "Order status", "commands": [{"type": "order_status"}]}

    if "location status" in text or "locations" in text or "list points" in text:
        return {"say": "Location status", "commands": [{"type": "location_status"}]}

    if "clear orders" in text:
        return {"say": "Clearing all orders", "commands": [{"type": "clear_orders"}]}

    if "sample order" in text or "save sample" in text:
        return {"say": "Saving sample order file", "commands": [{"type": "save_sample_orders"}]}

    if "import orders" in text or "load orders" in text:
        return {"say": "Importing orders from file", "commands": [{"type": "import_orders"}]}

    # route
    for r in routes:
        r_clean = r.strip()
        r_short = r_clean.lower().replace("waypoints_", "")
        pattern = r'route\s+' + re.escape(r_short)
        if re.search(pattern, text):
            return {"say": f"Route changed to {r_clean}", "commands": [{"type": "select_route", "drone": "current", "route": r_clean}]}

    return None  # No rule matched, fall through to LLM


# ====== LLM Path (smart path) ======

SYSTEM_PROMPT = """You are an AI drone fleet dispatcher for a city simulation in Ningbo, China.

You receive the current scene state and a user command. You must respond with a JSON object containing:
- "say": a brief human-readable response (in English)
- "commands": an array of command objects to execute

Available command types:
1. {"type": "pause_all"} - Pause all drones
2. {"type": "resume_all"} - Resume all drones
3. {"type": "pause", "drone": "<name>"} - Pause a specific drone
4. {"type": "resume", "drone": "<name>"} - Resume a specific drone
5. {"type": "set_speed", "drone": "<name>", "speed": <m/s>} - Set drone speed (value in m/s, user speaks in km/h so divide by 3.6)
6. {"type": "select_route", "drone": "<name>", "route": "<route_name>"} - Assign a route
7. {"type": "query_status"} - Query fleet status
8. {"type": "query_drone", "drone": "<name>"} - Query single drone status
9. {"type": "query_routes"} - List available routes
10. {"type": "go_to", "drone": "<name>", "longitude": <lon>, "latitude": <lat>, "height": <h>} - Fly to coordinates
11. {"type": "create_test_order"} - Create a random test delivery order for automatic assignment
12. {"type": "order_status"} - Show all delivery orders and their status
13. {"type": "create_order", "route": "pickup_name,delivery_name,description"} - Create order from named locations
14. {"type": "location_status"} - Show all location points
15. {"type": "import_orders"} - Import orders from JSON file
16. {"type": "save_sample_orders"} - Save sample order file for reference
17. {"type": "clear_orders"} - Clear all orders

Rules:
- Use "current" as drone name when the user doesn't specify which drone
- Speed values from user are in km/h, convert to m/s (divide by 3.6) for the speed field
- If the user asks a question that doesn't need a command, return empty commands array and answer in "say"
- Always respond with valid JSON only, no markdown, no explanation outside the JSON
- Available drone names and routes are provided in the scene state
- When user asks to create a delivery/test order, use create_test_order
- When user asks about orders or deliveries, use order_status
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
        return {"say": f"LLM error: {str(e)}", "commands": []}


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
                  f"Flying:{summary.get('flying',0)} "
                  f"Idle:{summary.get('idle',0)} "
                  f"Paused:{summary.get('paused',0)}")
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

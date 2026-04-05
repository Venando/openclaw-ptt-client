#!/usr/bin/env python3
"""
OpenClaw PTT Client — Python TTS Long-Running Service
Loads the Coqui TTS model once at startup, then processes requests from stdin.
"""

import sys
import json
import os
import tempfile
import traceback
from typing import Optional

# Flush helper
def flush_print(line: str):
    print(line, flush=True)

def flush_json(obj: dict):
    print(json.dumps(obj), flush=True)

# ── Load Coqui TTS once at startup ────────────────────────────────────────────
DEFAULT_MODEL = "tts_models/multilingual/mxtts/vits"

try:
    from TTS.api import TTS

    model_name = os.environ.get("TTS_MODEL", DEFAULT_MODEL)
    tts = TTS(model_name=model_name, progress_bar=False)
    flush_print("READY")
except Exception as e:
    # Write error and bail
    flush_json({"error": f"Failed to load TTS model: {e}"})
    sys.exit(1)

# ── Main request loop ─────────────────────────────────────────────────────────
def handle_request(data: dict):
    req_id = data.get("id", "unknown")
    text = data.get("text")
    voice = data.get("voice")       # not currently used by Coqui TTS multilingual model
    model = data.get("model")       # override model per-request

    if not text:
        flush_json({"id": req_id, "error": "Missing required field: text"})
        flush_print(f"DONE:{req_id}")
        return

    try:
        selected_model = model if model else None

        with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
            out_path = f.name

        # Synthesize
        if selected_model:
            tts.tts_to_file(text=text, file_path=out_path, model_name=selected_model)
        else:
            tts.tts_to_file(text=text, file_path=out_path)

        flush_json({"id": req_id, "path": out_path})
        flush_print(f"DONE:{req_id}")

    except Exception as ex:
        flush_json({"id": req_id, "error": str(ex)})
        flush_print(f"DONE:{req_id}")

# ── stdin reader (line-buffered JSON) ────────────────────────────────────────
def run():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue

        if line == "EXIT":
            # Clean shutdown
            break

        try:
            data = json.loads(line)
            handle_request(data)
        except json.JSONDecodeError:
            # Try to extract an id for error reporting
            try:
                partial = json.loads(line[:256])
                req_id = partial.get("id", "unknown")
            except Exception:
                req_id = "unknown"
            flush_json({"id": req_id, "error": f"Invalid JSON: {line[:100]}"})
            flush_print(f"DONE:{req_id}")

if __name__ == "__main__":
    run()

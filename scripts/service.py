"""
TTS Service — startup, request handling, and main loop.
All abstractions are imported from siblings; engine from sibling module.
"""

import sys
from datetime import datetime

from abstractions import (
    log, env, fs, clock, io, send, _stdout_to_stderr,
)
from engine import CoquiTTSEngine


# ── Global state ───────────────────────────────────────────────────────────────
tts: object | None = None
_started: bool = False


def start() -> None:
    """Initialize logging and load the TTS model. Safe to call multiple times."""
    global tts, _started
    if _started:
        return
    _started = True

    # Persistent log file
    _log_dir = env.get("TTS_LOG_DIR", __import__('os').path.join(fs.temp_dir(), "openclaw-ptt-logs"))
    fs.makedirs(_log_dir, exist_ok=True)
    _log_file = __import__('os').path.join(_log_dir, f"tts_{datetime.now().strftime('%Y%m%d_%H%M%S')}.log")
    _file_handler = __import__('logging').FileHandler(_log_file, encoding="utf-8")
    _file_handler.setLevel(__import__('logging').INFO)
    _file_handler.setFormatter(__import__('logging').Formatter("[%(asctime)s] %(levelname)s: %(message)s"))
    log.addHandler(_file_handler)

    model_name = env.get("TTS_MODEL") or None

    log.info("=== TTS Service starting ===")
    log.info(f"Log file: {_log_file}")
    log.info(f"TTS_MODEL env: {model_name!r}")
    log.info(f"Python version: {sys.version}")
    log.info(f"Python executable: {sys.executable}")

    if model_name:
        engine = CoquiTTSEngine(model_name, clock, log)
        try:
            engine.ensure_ready()
            tts = engine
            io.write_raw("READY")
        except Exception as e:
            log.exception(f"Critical failure: could not load TTS model on GPU or CPU: {e}")
            send({"error": f"Failed to load TTS model: {e}"})
            sys.exit(1)


# ── Request handler ────────────────────────────────────────────────────────────
def handle_request(data: dict) -> None:
    req_id = data.get("id", "unknown")
    text = data.get("text")

    if not text:
        send({"id": req_id, "error": "Missing required field: text"})
        io.write_raw(f"DONE:{req_id}")
        return

    if tts is None:
        send({"id": req_id, "error": "No TTS model loaded"})
        io.write_raw(f"DONE:{req_id}")
        return

    try:
        with fs.temp_file(suffix=".wav", delete=False) as f:
            out_path = f.name

        t0 = clock.monotonic()
        with _stdout_to_stderr():
            tts.tts_to_file(text=text, file_path=out_path)

        file_size = fs.getsize(out_path)
        elapsed = clock.monotonic() - t0
        log.info(__import__('json').dumps({"perf": True, "id": req_id, "bytes": file_size, "time": round(elapsed, 2)}))
        send({"id": req_id, "path": out_path})
        io.write_raw(f"DONE:{req_id}")

    except Exception:
        log.exception(f"Request {req_id} failed")
        send({"id": req_id, "error": "TTS synthesis failed"})
        io.write_raw(f"DONE:{req_id}")


# ── Main stdin loop ────────────────────────────────────────────────────────────
def run() -> None:
    start()
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        if line == "EXIT":
            break
        try:
            handle_request(__import__('json').loads(line))
        except __import__('json').JSONDecodeError:
            try:
                req_id = __import__('json').loads(line[:256]).get("id", "unknown")
            except Exception:
                req_id = "unknown"
            send({"id": req_id, "error": f"Invalid JSON: {line[:100]}"})
            io.write_raw(f"DONE:{req_id}")

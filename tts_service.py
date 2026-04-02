#!/usr/bin/env python3
"""
OpenClaw PTT Client — Python TTS Long-Running Service
Loads the Coqui TTS model once at startup, then processes requests from stdin.
"""

import sys
import json
import os
import tempfile
import logging
from datetime import datetime

# Stdout is reserved exclusively for the JSON protocol.
# All debug output goes to stderr and a persistent log file.
logging.basicConfig(
    level=logging.INFO,
    format="[TTS-PY] %(levelname)s: %(message)s",
    stream=sys.stderr,
)
log = logging.getLogger("tts")

# ── Persistent log file ────────────────────────────────────────────────────────
_log_dir = os.environ.get("TTS_LOG_DIR", os.path.join(tempfile.gettempdir(), "openclaw-ptt-logs"))
os.makedirs(_log_dir, exist_ok=True)
_log_file = os.path.join(_log_dir, f"tts_{datetime.now().strftime('%Y%m%d_%H%M%S')}.log")
_file_handler = logging.FileHandler(_log_file, encoding="utf-8")
_file_handler.setLevel(logging.DEBUG)
_file_handler.setFormatter(logging.Formatter("[%(asctime)s] %(levelname)s: %(message)s"))
log.addHandler(_file_handler)


import contextlib

# Force UTF-8 on stdout so Cyrillic in JSON responses never hits cp1252
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


def send(obj: dict) -> None:
    print(json.dumps(obj), flush=True)


@contextlib.contextmanager
def _stdout_to_stderr():
    """Redirect stdout to stderr during TTS synthesis.

    Coqui calls print(sens) internally after splitting sentences, which would
    corrupt the JSON protocol on stdout and crash on non-ASCII text under cp1252.
    Swap the reference directly — never wrap stderr.buffer in a new TextIOWrapper,
    as closing the wrapper would destroy the underlying buffer and kill stderr.
    """
    old = sys.stdout
    sys.stdout = sys.stderr
    try:
        yield
    finally:
        sys.stdout = old


# ── Global state ───────────────────────────────────────────────────────────────
tts: object | None = None

# ── Load Coqui TTS once at startup ────────────────────────────────────────────
model_name = os.environ.get("TTS_MODEL") or None

log.info("=== TTS Service starting ===")
log.info(f"Log file: {_log_file}")
log.info(f"TTS_MODEL env: {model_name!r}")
log.info(f"Python version: {sys.version}")
log.info(f"Python executable: {sys.executable}")

if model_name:
    log.info(f"Attempting to load model: {model_name!r}")
    try:
        import torch
        import time
        from TTS.api import TTS

        can_use_cuda = torch.cuda.is_available()
        log.info(f"CUDA available: {can_use_cuda}")
        
        device = "cuda" if can_use_cuda else "cpu"

        t0 = time.monotonic()
        try:
            if not can_use_cuda:
                raise RuntimeError("CUDA not available")

            tts = TTS(model_name=model_name, progress_bar=False).to(device)
            log.info(f"TTS loaded on CUDA in {time.monotonic() - t0:.1f}s")

        except Exception as cuda_err:
            log.warning(f"FALLBACK_REASON: CUDA initialization failed ({cuda_err}). Switching to CPU.")
            if can_use_cuda:
                try:
                    torch.cuda.empty_cache()
                except Exception:
                    pass

            t0 = time.monotonic()
            tts = TTS(model_name=model_name, progress_bar=False, gpu=False)
            log.info(f"TTS loaded on CPU in {time.monotonic() - t0:.1f}s")

        log.info(f"Model loaded successfully: {model_name}")
        print("READY", flush=True)

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
        print(f"DONE:{req_id}", flush=True)
        return

    if tts is None:
        send({"id": req_id, "error": "No TTS model loaded"})
        print(f"DONE:{req_id}", flush=True)
        return

    try:
        with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
            out_path = f.name

        t0 = time.monotonic()
        with _stdout_to_stderr():
            tts.tts_to_file(text=text, file_path=out_path)

        file_size = os.path.getsize(out_path)
        elapsed = time.monotonic() - t0
        log.info(json.dumps({"perf": True, "id": req_id, "bytes": file_size, "time": round(elapsed, 2)}))
        send({"id": req_id, "path": out_path})
        print(f"DONE:{req_id}", flush=True)

    except Exception:
        log.exception(f"Request {req_id} failed")
        send({"id": req_id, "error": "TTS synthesis failed"})
        print(f"DONE:{req_id}", flush=True)


# ── Main stdin loop ────────────────────────────────────────────────────────────
def run() -> None:
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        if line == "EXIT":
            break
        try:
            handle_request(json.loads(line))
        except json.JSONDecodeError:
            try:
                req_id = json.loads(line[:256]).get("id", "unknown")
            except Exception:
                req_id = "unknown"
            send({"id": req_id, "error": f"Invalid JSON: {line[:100]}"})
            print(f"DONE:{req_id}", flush=True)


if __name__ == "__main__":
    run()
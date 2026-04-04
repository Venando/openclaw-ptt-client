"""
TTS Engine: TTSEngine interface and CoquiTTSEngine implementation.
Does not depend on abstractions.py — receives them via constructor.
"""

import logging
from abc import ABC, abstractmethod


# ── TTS Engine abstraction ──────────────────────────────────────────────────────
class TTSEngine(ABC):
    @abstractmethod
    def tts_to_file(self, text: str, file_path: str) -> None:
        ...

    @abstractmethod
    def is_ready(self) -> bool:
        ...


class CoquiTTSEngine(TTSEngine):
    def __init__(self, model_name: str | None, clock, log: logging.Logger) -> None:
        self._tts: object | None = None
        self._model_name = model_name
        self._clock = clock
        self._log = log

    def is_ready(self) -> bool:
        return self._tts is not None

    def _load(self) -> None:
        if self._tts is not None:
            return
        if not self._model_name:
            return

        import torch
        from TTS.api import TTS

        can_use_cuda = torch.cuda.is_available()
        self._log.info(f"CUDA available: {can_use_cuda}")

        device = "cuda" if can_use_cuda else "cpu"
        t0 = self._clock.monotonic()

        try:
            if not can_use_cuda:
                raise RuntimeError("CUDA not available")

            self._tts = TTS(model_name=self._model_name, progress_bar=False).to(device)
            self._log.info(f"TTS loaded on CUDA in {self._clock.monotonic() - t0:.1f}s")

        except Exception as cuda_err:
            self._log.warning(f"FALLBACK_REASON: CUDA initialization failed ({cuda_err}). Switching to CPU.")
            if can_use_cuda:
                try:
                    torch.cuda.empty_cache()
                except Exception:
                    pass

            t0 = self._clock.monotonic()
            self._tts = TTS(model_name=self._model_name, progress_bar=False, gpu=False)
            self._log.info(f"TTS loaded on CPU in {self._clock.monotonic() - t0:.1f}s")

        self._log.info(f"Model loaded successfully: {self._model_name}")

    def tts_to_file(self, text: str, file_path: str) -> None:
        self._load()
        if self._tts is None:
            raise RuntimeError("No TTS model loaded")
        self._tts.tts_to_file(text=text, file_path=file_path)

    def ensure_ready(self) -> None:
        """Load the model and raise on failure. Safe to call multiple times."""
        self._load()
        if self._tts is None and self._model_name:
            raise RuntimeError("Failed to load TTS model")

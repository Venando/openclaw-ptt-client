namespace OpenClawPTT.Services;

/// <summary>Explicit PTT recording state machine replacing volatile bool flags in PttController.</summary>
public enum PttState
{
    /// <summary>Waiting for hotkey press.</summary>
    Idle,
    /// <summary>Actively recording audio.</summary>
    Recording,
    /// <summary>Transcription in-flight after recording stopped.</summary>
    Processing
}

public interface IPttStateMachine
{
    /// <summary>Current recording state.</summary>
    PttState CurrentState { get; }

    /// <summary>Hotkey was pressed — transitions Idle→Recording or triggers toggle.</summary>
    void OnHotkeyPressed();

    /// <summary>Hotkey was released — transitions Recording→Processing (hold-to-talk).</summary>
    void OnHotkeyReleased();

    /// <summary>Called after audio recording has started.</summary>
    void OnRecordingStarted();

    /// <summary>Called after audio recording has stopped.</summary>
    void OnRecordingStopped();

    /// <summary>Called when transcription completes.</summary>
    void OnProcessingCompleted();

    /// <summary>True when the loop should start recording (state just became Recording).</summary>
    bool ShouldStartRecording { get; }

    /// <summary>True when the loop should stop and transcribe (state just became Processing).</summary>
    bool ShouldStopRecording { get; }

    /// <summary>True when a toggle-press should stop recording (toggle mode, Recording state).</summary>
    bool ShouldToggleRecording { get; }

    /// <summary>Resets to Idle state, clearing all transient flags.</summary>
    void Reset();
}

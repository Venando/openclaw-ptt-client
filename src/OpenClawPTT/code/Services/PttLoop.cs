using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

/// <summary>
/// The main PTT event loop. Coordinates hotkey state machine, audio recording,
/// transcription, and console input polling.
/// </summary>
public sealed class PttLoop : IPttLoop
{
    private readonly IPttStateMachine _stateMachine;
    private readonly IAudioService _audioService;
    private readonly ITextMessageSender _textSender;
    private readonly IConsoleOutput _console;
    private readonly IInputHandler _inputHandler;
    private readonly AppConfig _config;
    private readonly IPttController _pttController;
    private bool _disposed;

    public PttLoop(
        IPttStateMachine stateMachine,
        IAudioService audioService,
        ITextMessageSender textSender,
        IConsoleOutput console,
        IInputHandler inputHandler,
        IPttController pttController,
        AppConfig config)
    {
        _stateMachine = stateMachine;
        _audioService = audioService;
        _textSender = textSender;
        _console = console;
        _inputHandler = inputHandler;
        _pttController = pttController;
        _config = config;
    }

    public PttLoopExitCode ExitCode { get; private set; } = PttLoopExitCode.Ok;

    public async Task<PttLoopExitCode> RunAsync(CancellationToken ct)
    {
        _stateMachine.Reset();

        while (!ct.IsCancellationRequested)
        {
            // Drive the state machine from hotkey events
            if (_pttController.PollHotkeyPressed())
                _stateMachine.OnHotkeyPressed();

            if (_pttController.PollHotkeyRelease())
                _stateMachine.OnHotkeyReleased();

            // Handle state-driven recording actions
            if (_stateMachine.ShouldStartRecording && !_audioService.IsRecording)
            {
                _audioService.StartRecording();
                _stateMachine.OnRecordingStarted();
            }

            if (_stateMachine.ShouldStopRecording)
            {
                var transcribed = await _pttController.StopAndTranscribeAsync(ct);
                if (transcribed != null)
                    await _textSender.SendAsync(transcribed, ct);
                _stateMachine.OnProcessingCompleted();
            }

            // Handle console input
            var inputResult = await _inputHandler.HandleInputAsync(ct);
            if (inputResult == -1) return PttLoopExitCode.Ok;
            if (inputResult == 100) return PttLoopExitCode.Restart;
        }

        return PttLoopExitCode.Ok;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _pttController.Dispose();
            _audioService.Dispose();
            _disposed = true;
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

/// <summary>
/// The main PTT event loop. Coordinates hotkey state machine, audio recording,
/// transcription, and console input polling.
/// </summary>
public sealed class AppLoop : IAppLoop
{
    private readonly IPttStateMachine _pttStateMachine;
    private readonly IAudioService _audioService;
    private readonly ITextMessageSender _textSender;
    private readonly IInputHandler _inputHandler;
    private readonly IPttController _pttController;
    private bool _disposed;

    public AppLoop(
        IPttStateMachine stateMachine,
        IAudioService audioService,
        ITextMessageSender textSender,
        IInputHandler inputHandler,
        IPttController pttController)
    {
        _pttStateMachine = stateMachine;
        _audioService = audioService;
        _textSender = textSender;
        _inputHandler = inputHandler;
        _pttController = pttController;
    }

    public AppLoopExitCode ExitCode { get; private set; } = AppLoopExitCode.Ok;

    public async Task<AppLoopExitCode> RunAsync(CancellationToken ct)
    {
        if (_disposed) return AppLoopExitCode.Ok;
        _pttStateMachine.Reset();

        while (!ct.IsCancellationRequested)
        {
            // Drive the state machine from hotkey events
            if (_pttController.PollHotkeyPressed())
                _pttStateMachine.OnHotkeyPressed();

            if (_pttController.PollHotkeyRelease())
                _pttStateMachine.OnHotkeyReleased();

            // Handle state-driven recording actions
            if (_pttStateMachine.ShouldStartRecording && !_audioService.IsRecording)
            {
                _audioService.StartRecording();
                _pttStateMachine.OnRecordingStarted();
            }

            if (_pttStateMachine.ShouldStopRecording)
            {
                var transcribed = await _audioService.StopAndTranscribeAsync(ct);
                if (transcribed != null)
                {
                    try { await _textSender.SendAsync(transcribed, ct); }
                    catch { /* swallow: network/send errors do not kill the PTT loop */ }
                }
                _pttStateMachine.OnProcessingCompleted();
            }

            // Handle console input
            var inputResult = await _inputHandler.HandleInputAsync(ct);
            if (inputResult == InputResult.Quit) return AppLoopExitCode.Ok;
            if (inputResult == InputResult.Restart) return AppLoopExitCode.Restart;
        }

        return AppLoopExitCode.Ok;
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

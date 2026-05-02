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
    private readonly bool _requireConfirmBeforeSend;
    private bool _disposed;

    public AppLoop(
        IPttStateMachine stateMachine,
        IAudioService audioService,
        ITextMessageSender textSender,
        IInputHandler inputHandler,
        IPttController pttController,
        bool requireConfirmBeforeSend = false)
    {
        _pttStateMachine = stateMachine;
        _audioService = audioService;
        _textSender = textSender;
        _inputHandler = inputHandler;
        _pttController = pttController;
        _requireConfirmBeforeSend = requireConfirmBeforeSend;
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

            // Check for cancellation (Escape key pressed during recording)
            if (_pttController.PollCancelRecording())
            {
                _pttStateMachine.Reset();
                _audioService.StopDiscard();
            }

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
                    if (_requireConfirmBeforeSend)
                    {
                        ConsoleUi.PrintMarkup("[deepskyblue3]  ─[/] [bold]Press hotkey to send[/] [grey]or Escape to discard[/] [deepskyblue3]─[/]");
                        bool sent = false;
                        while (!ct.IsCancellationRequested)
                        {
                            await Task.Delay(50, ct);
                            if (_pttController.PollHotkeyPressed())
                            {
                                sent = true;
                                break;
                            }
                            if (_pttController.PollCancelRecording())
                            {
                                ConsoleUi.PrintMarkup("[grey]  ─ Message discarded ─[/]");
                                break;
                            }
                        }

                        if (sent)
                        {
                            try { await _textSender.SendAsync(transcribed, ct); }
                            catch { /* swallow */ }
                        }
                    }
                    else
                    {
                        try { await _textSender.SendAsync(transcribed, ct); }
                        catch { /* swallow: network/send errors do not kill the PTT loop */ }
                    }
                }
                _pttStateMachine.OnProcessingCompleted();
            }

            // Console input is now handled by StreamShell via StreamShellInputHandler

            // Yield: prevent spin-loop. Without this the while-loop burns 100% CPU
            // polling hotkey flags that are almost always false.
            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { break; }
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

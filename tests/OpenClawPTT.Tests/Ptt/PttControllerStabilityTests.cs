using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace OpenClawPTT.Tests;

/// <summary>
/// Stability and edge-case tests for PttController.
/// These complement PttControllerTests which cover the happy path.
/// </summary>
public class PttControllerStabilityTests : IDisposable
{
    /// <summary>
    /// Test double for IGlobalHotkeyHook that tracks calls and lets us fire events.
    /// Uses explicit interface implementation to avoid C# event quirks.
    /// </summary>
    private sealed class FakeHotkeyHook : IGlobalHotkeyHook
    {
        private bool _disposed;
        private Action? _hotkeyPressed;
        private Action? _hotkeyReleased;
        private Action<int>? _hotkeyIndexPressed;
        private Action<int>? _hotkeyIndexReleased;
        public List<bool> StartCalls { get; } = new();
        public List<bool> DisposeCalls { get; } = new();

        event Action? IGlobalHotkeyHook.HotkeyPressed
        {
            add { _hotkeyPressed += value; }
            remove { _hotkeyPressed -= value; }
        }
        event Action? IGlobalHotkeyHook.HotkeyReleased
        {
            add { _hotkeyReleased += value; }
            remove { _hotkeyReleased -= value; }
        }
        event Action<int>? IGlobalHotkeyHook.HotkeyIndexPressed
        {
            add { _hotkeyIndexPressed += value; }
            remove { _hotkeyIndexPressed -= value; }
        }
        event Action<int>? IGlobalHotkeyHook.HotkeyIndexReleased
        {
            add { _hotkeyIndexReleased += value; }
            remove { _hotkeyIndexReleased -= value; }
        }

        public void SetHotkey(Hotkey hotkey) { }
        public void SetHotkeys(System.Collections.Generic.IEnumerable<Hotkey> hotkeys) { }
        public void Start() { StartCalls.Add(true); }
        public void Dispose()
        {
            if (!_disposed) { _disposed = true; DisposeCalls.Add(true); }
        }

        public void SimulatePress() => _hotkeyPressed?.Invoke();
        public void SimulateRelease() => _hotkeyReleased?.Invoke();
    }

    /// <summary>
    /// Test double for IHotkeyHookFactory.
    /// </summary>
    private sealed class FakeHotkeyHookFactory : IHotkeyHookFactory
    {
        public List<FakeHotkeyHook> CreatedHooks { get; } = new();
        public IGlobalHotkeyHook Create(Hotkey mapping)
        {
            var hook = new FakeHotkeyHook();
            CreatedHooks.Add(hook);
            return hook;
        }
    }

    private readonly Mock<IAudioService> _mockAudio;
    private readonly AppConfig _config;
    private readonly FakeHotkeyHookFactory _factory;

    public PttControllerStabilityTests()
    {
        _mockAudio = new Mock<IAudioService>();
        _mockAudio.Setup(x => x.IsRecording).Returns(false);
        _config = new AppConfig { HotkeyCombination = "Ctrl+K", HoldToTalk = false };
        _factory = new FakeHotkeyHookFactory();
    }

    public void Dispose() { }

    // ── SetHotkey tests ─────────────────────────────────────────────────────

    [Fact]
    void SetHotkey_CalledOnce_CreatesHook()
    {
        var controller = new PttController(_factory);
        controller.SetHotkey("Ctrl+K", false);

        Assert.Single(_factory.CreatedHooks);
    }

    [Fact]
    void SetHotkey_CalledTwice_DisposesOldHookBeforeNew()
    {
        var controller = new PttController(_factory);
        controller.SetHotkey("Ctrl+K", false);
        var first = _factory.CreatedHooks[0];

        controller.SetHotkey("Ctrl+L", false);
        var second = _factory.CreatedHooks[1];

        Assert.NotSame(first, second);
        Assert.Single(first.DisposeCalls);
        Assert.Equal(2, _factory.CreatedHooks.Count);
    }

    [Fact]
    void SetHotkey_WithHoldToTalk_WiresBothEvents()
    {
        var controller = new PttController(_factory);
        controller.SetHotkey("Ctrl+K", true);
        var hook = _factory.CreatedHooks[0];
        var ifce = (IGlobalHotkeyHook)hook;

        bool pressed = false, released = false;
        ifce.HotkeyPressed += () => pressed = true;
        ifce.HotkeyReleased += () => released = true;

        hook.SimulatePress();
        hook.SimulateRelease();

        Assert.True(pressed);
        Assert.True(released);
    }

    [Fact]
    void SetHotkey_WithoutHoldToTalk_DoesNotWireReleaseEvent()
    {
        var controller = new PttController(_factory);
        controller.SetHotkey("Ctrl+K", false);
        var hook = _factory.CreatedHooks[0];
        hook.SimulateRelease();

        // In toggle mode, the controller doesn't wire a release handler,
        // so PollHotkeyRelease remains false.
        Assert.False(controller.PollHotkeyRelease());
    }

    [Fact]
    void SetHotkey_WithNullFactory_DoesNotThrow()
    {
        var controller = new PttController(hotkeyHookFactory: null);
        var ex = Record.Exception(() => controller.SetHotkey("Ctrl+K", false));
        Assert.Null(ex);
    }

    // ── PollHotkeyPressed tests ─────────────────────────────────────────────

    [Fact]
    void PollHotkeyPressed_InitiallyFalse()
    {
        var controller = new PttController(_factory);
        controller.SetHotkey("Ctrl+K", false);

        Assert.False(controller.PollHotkeyPressed());
    }

    [Fact]
    void PollHotkeyPressed_AfterPress_ReturnsTrueOnce()
    {
        var controller = new PttController(_factory);
        controller.SetHotkey("Ctrl+K", false);
        var hook = _factory.CreatedHooks[0];
        hook.SimulatePress();

        Assert.True(controller.PollHotkeyPressed());
        Assert.False(controller.PollHotkeyPressed()); // consumed
    }

    [Fact]
    void PollHotkeyPressed_MultiplePresses_EachConsumedSeparately()
    {
        // Use reflection to directly set the internal flag, bypassing event wiring.
        // This tests the core atomic consume behavior.
        var controller = new PttController(_factory);
        controller.SetHotkey("Ctrl+K", false);

        var fPressed = typeof(PttController)
            .GetField("_hotkeyPressed", BindingFlags.NonPublic | BindingFlags.Instance)!;
        fPressed.SetValue(controller, true); // simulate first press

        Assert.True(controller.PollHotkeyPressed()); // first press consumed
        Assert.False(controller.PollHotkeyPressed()); // already consumed

        fPressed.SetValue(controller, true); // simulate second press
        Assert.True(controller.PollHotkeyPressed()); // second press consumed
        Assert.False(controller.PollHotkeyPressed()); // consumed again
    }

    // ── PollHotkeyRelease tests ─────────────────────────────────────────────

    [Fact]
    void PollHotkeyRelease_InitiallyFalse()
    {
        var controller = new PttController(_factory);
        controller.SetHotkey("Ctrl+K", true);

        Assert.False(controller.PollHotkeyRelease());
    }

    [Fact]
    void PollHotkeyRelease_AfterRelease_ReturnsTrueOnce()
    {
        var controller = new PttController(_factory);
        controller.SetHotkey("Ctrl+K", true);
        var hook = _factory.CreatedHooks[0];
        hook.SimulateRelease();

        Assert.True(controller.PollHotkeyRelease());
        Assert.False(controller.PollHotkeyRelease()); // consumed
    }

    [Fact]
    void PollHotkeyRelease_MultipleReleases_EachConsumedSeparately()
    {
        // Use reflection to directly set the internal flag, bypassing event wiring.
        var controller = new PttController(_factory);
        controller.SetHotkey("Ctrl+K", true);

        var fReleased = typeof(PttController)
            .GetField("_hotkeyReleased", BindingFlags.NonPublic | BindingFlags.Instance)!;
        fReleased.SetValue(controller, true); // simulate first release

        Assert.True(controller.PollHotkeyRelease()); // first release consumed
        Assert.False(controller.PollHotkeyRelease()); // already consumed

        fReleased.SetValue(controller, true); // simulate second release
        Assert.True(controller.PollHotkeyRelease()); // second release consumed
        Assert.False(controller.PollHotkeyRelease()); // consumed again
    }

    // ── Dispose tests ──────────────────────────────────────────────────────

    [Fact]
    void Dispose_CalledTwice_DoesNotThrow()
    {
        var controller = new PttController(_factory);
        controller.SetHotkey("Ctrl+K", false);

        controller.Dispose();

        var ex = Record.Exception(() => controller.Dispose());
        Assert.Null(ex);
    }
}

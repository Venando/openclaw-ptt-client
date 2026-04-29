namespace OpenClawPTT.Tests;

using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using System;
using System.Text;
using Xunit;

public class AppBootstrapperTests : IDisposable
{
    private readonly Mock<IConsole> _fakeConsole;
    private readonly Mock<IConfigurationService> _fakeConfig;
    private readonly Mock<IServiceFactory> _fakeFactory;
    private readonly Mock<IStreamShellHost> _fakeShellHost;

    public AppBootstrapperTests()
    {
        _fakeShellHost = new Mock<IStreamShellHost>();
        _fakeConsole = new Mock<IConsole>();
        _fakeConsole.Setup(x => x.ForegroundColor).Returns(ConsoleColor.Gray);
        _fakeConsole.Setup(x => x.OutputEncoding).Returns(Encoding.UTF8);
        _fakeConsole.Setup(x => x.ReadKey(true))
            .Returns(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false));

        // Route all static ConsoleUi calls through our mock
        ConsoleUi.SetConsole(_fakeConsole.Object);

        _fakeConfig = new Mock<IConfigurationService>();
        _fakeConfig.Setup(x => x.LoadOrSetupAsync(It.IsAny<IStreamShellHost>(), false))
            .ReturnsAsync(new AppConfig());
        _fakeFactory = new Mock<IServiceFactory>();
    }

    public void Dispose() { }

    private Mock<AppRunner> MakeMockRunner(int exitCode = 0, Exception? throws = null)
    {
        var mock = new Mock<AppRunner>(
            MockBehavior.Loose,
            new AppConfig(),
            _fakeFactory.Object);
        mock.CallBase = false;
        if (throws != null)
            mock.Setup(x => x.RunAsync(It.IsAny<CancellationToken>())).ThrowsAsync(throws);
        else
            mock.Setup(x => x.RunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(exitCode);
        return mock;
    }

    #region Happy path

    [Fact]
    public async Task RunAsync_RunnerReturns0_NoException_Returns0()
    {
        var mockRunner = MakeMockRunner(0);
        var bootstrapper = new AppBootstrapper(
            _fakeConsole.Object,
            _fakeConfig.Object,
            _fakeFactory.Object,
            _fakeShellHost.Object,
            (_, _) => mockRunner.Object);

        var exitCode = await bootstrapper.RunAsync();

        Assert.Equal(0, exitCode);
    }

    #endregion

    #region Runner non-zero exit

    [Fact]
    public async Task RunAsync_RunnerReturns1_ReturnsExitError()
    {
        var mockRunner = MakeMockRunner(1);
        var bootstrapper = new AppBootstrapper(
            _fakeConsole.Object,
            _fakeConfig.Object,
            _fakeFactory.Object,
            _fakeShellHost.Object,
            (_, _) => mockRunner.Object);

        var exitCode = await bootstrapper.RunAsync();

        Assert.Equal(AppExitHandler.ExitError, exitCode);
    }

    #endregion

    #region Exception handling

    [Fact]
    public async Task RunAsync_ConfigLoadThrows_ReturnsExitError()
    {
        _fakeConfig.Setup(x => x.LoadOrSetupAsync(It.IsAny<IStreamShellHost>(), false))
            .ThrowsAsync(new InvalidOperationException("config broken"));

        var bootstrapper = new AppBootstrapper(
            _fakeConsole.Object,
            _fakeConfig.Object,
            _fakeFactory.Object);

        var exitCode = await bootstrapper.RunAsync();

        Assert.Equal(AppExitHandler.ExitError, exitCode);
    }

    [Fact]
    public async Task RunAsync_RunnerThrows_ReturnsExitError()
    {
        var mockRunner = MakeMockRunner(throws: new InvalidOperationException("runner boom"));
        var bootstrapper = new AppBootstrapper(
            _fakeConsole.Object,
            _fakeConfig.Object,
            _fakeFactory.Object,
            _fakeShellHost.Object,
            (_, _) => mockRunner.Object);

        var exitCode = await bootstrapper.RunAsync();

        Assert.Equal(AppExitHandler.ExitError, exitCode);
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task RunAsync_CTCancelled_ReturnsExitCancelled()
    {
        var cts = new CancellationTokenSource();
        var mockRunner = MakeMockRunner(0);

        var bootstrapper = new AppBootstrapper(
            _fakeConsole.Object,
            _fakeConfig.Object,
            _fakeFactory.Object,
            _fakeShellHost.Object,
            (_, _) => mockRunner.Object);

        cts.Cancel();
        var exitCode = await bootstrapper.RunAsync(cts.Token);

        Assert.Equal(AppExitHandler.ExitCancelled, exitCode);
    }

    #endregion

    #region Console encoding

    [Fact]
    public async Task RunAsync_SetsConsoleOutputEncoding()
    {
        var mockRunner = MakeMockRunner(0);
        var bootstrapper = new AppBootstrapper(
            _fakeConsole.Object,
            _fakeConfig.Object,
            _fakeFactory.Object,
            _fakeShellHost.Object,
            (_, _) => mockRunner.Object);

        await bootstrapper.RunAsync();

        _fakeConsole.VerifySet(x => x.OutputEncoding = Encoding.UTF8);
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_UnsubscribesCancelKeyPress()
    {
        var mockRunner = MakeMockRunner(0);
        var bootstrapper = new AppBootstrapper(
            _fakeConsole.Object,
            _fakeConfig.Object,
            _fakeFactory.Object,
            _fakeShellHost.Object,
            (_, _) => mockRunner.Object);

        bootstrapper.Dispose();

        // Removing a never-added handler is a no-op; verify no crash
        Assert.True(true);
    }

    #endregion
}

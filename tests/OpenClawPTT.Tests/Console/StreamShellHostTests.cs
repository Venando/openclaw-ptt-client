using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests;

public class StreamShellHostTests
{
    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var host = new StreamShellHost();
        Assert.NotNull(host);
        host.Dispose();
    }
}

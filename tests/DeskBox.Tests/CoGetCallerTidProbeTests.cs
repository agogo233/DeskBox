using System.Runtime.InteropServices;
using Xunit.Abstractions;

namespace DeskBox.Tests;

/// <summary>
/// Pins the undocumented case of CoGetCallerTID: what it returns on a thread
/// that is not servicing any COM call. PreferredDropEffectFilterDataObject
/// treats S_FALSE as "caller in another process"; if a no-context call ever
/// returned S_FALSE, WinUI's in-process mask derivation would read
/// DV_E_FORMATETC and external drags would silently change behavior. The OS
/// docs cover only the in-call cases (S_OK = same-process caller, S_FALSE =
/// different-process caller); these probes lock the remaining case.
/// </summary>
public sealed class CoGetCallerTidProbeTests
{
    private const int S_FALSE = 1;
    private const uint CoinitMultithreaded = 0x0;

    private readonly ITestOutputHelper _output;

    public CoGetCallerTidProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // Classic DllImport on purpose: the test project is not compiled with
    // unsafe blocks for LibraryImport source generation, and the Platform
    // interop ratchet only scans src/DeskBox.
    [DllImport("ole32.dll")]
    private static extern int CoGetCallerTID(out uint threadId);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [Fact]
    public void PlainThread_NoComContext_IsNotCrossProcess()
    {
        int hr = CoGetCallerTID(out uint threadId);
        _output.WriteLine($"no-context CoGetCallerTID hr=0x{hr:X8} tid={threadId}");
        Assert.NotEqual(S_FALSE, hr);
    }

    [Fact]
    public void CoInitializedThread_NoActiveCall_IsNotCrossProcess()
    {
        int initHr = CoInitializeEx(0, CoinitMultithreaded);
        try
        {
            int hr = CoGetCallerTID(out uint threadId);
            _output.WriteLine(
                $"coinitialized(no active call) CoInitializeEx=0x{initHr:X8} " +
                $"CoGetCallerTID hr=0x{hr:X8} tid={threadId}");
            Assert.NotEqual(S_FALSE, hr);
        }
        finally
        {
            if (initHr >= 0)
            {
                CoUninitialize();
            }
        }
    }
}

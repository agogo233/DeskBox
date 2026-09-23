using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using DeskBox.Controls;
using DeskBox.Platform;
using Windows.ApplicationModel.DataTransfer;

namespace DeskBox.Helpers;

/// <summary>
/// Minimal IDropSource: ESC cancels, releasing the source button drops, and
/// the system drag cursors are used for feedback. Every QueryContinueDrag
/// call is counted and logged (first calls at normal verbosity) so a starved
/// drag loop — the failure mode where the modal loop never sees mouse input
/// and never ends — is visible in the default log.
/// </summary>
[GeneratedComInterface]
[Guid("00000121-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface INativeDropSource
{
    [PreserveSig]
    int QueryContinueDrag(int escapePressed, uint keyState);

    [PreserveSig]
    int GiveFeedback(uint effect);
}

[GeneratedComClass]
internal sealed partial class NativeFileDragDropSource : INativeDropSource
{
    private const int SOk = 0;
    private const int DragDropSDrop = 0x00040100;
    private const int DragDropSCancel = 0x00040101;
    private const int DragDropSUseDefaultCursors = 0x00040102;
    private const uint MkLbutton = 0x0001;
    private const uint MkRbutton = 0x0002;

    private int _queryContinueCount;
    private int _lastKeyState = -1;

    /// <summary>
    /// Number of QueryContinueDrag calls so far. A drag loop that has started
    /// but never calls it is starved (no input reaches OLE).
    /// </summary>
    internal int QueryContinueCount => Volatile.Read(ref _queryContinueCount);

    int INativeDropSource.QueryContinueDrag(int escapePressed, uint keyState)
    {
        int count = Interlocked.Increment(ref _queryContinueCount);
        int lastKeyState = Interlocked.Exchange(ref _lastKeyState, (int)keyState);
        bool keyStateChanged = lastKeyState != (int)keyState;
        if (count <= 3 || keyStateChanged || escapePressed != 0)
        {
            App.Log(
                $"[NativeDragOut] QueryContinueDrag #{count} " +
                $"escape={escapePressed} keyState=0x{keyState:X}");
        }
        else
        {
            App.LogVerbose(
                $"[NativeDragOut] QueryContinueDrag #{count} " +
                $"escape={escapePressed} keyState=0x{keyState:X}");
        }

        if (escapePressed != 0)
        {
            return DragDropSCancel;
        }

        if ((keyState & (MkLbutton | MkRbutton)) == 0)
        {
            return DragDropSDrop;
        }

        return SOk;
    }

    int INativeDropSource.GiveFeedback(uint effect)
    {
        if (Volatile.Read(ref _queryContinueCount) == 0 &&
            _giveFeedbackLogged is false)
        {
            _giveFeedbackLogged = true;
            App.Log(
                $"[NativeDragOut] GiveFeedback effect=0x{effect:X} " +
                "(no QueryContinueDrag yet)");
        }

        return DragDropSUseDefaultCursors;
    }

    private bool _giveFeedbackLogged;
}

/// <summary>
/// Drives a native OLE drag-out for widget files on a dedicated STA thread:
/// builds the Shell data object, stamps a single-value Preferred DropEffect
/// (Move) and a DeskBox source tag onto it, and runs DoDragDrop with the full
/// Copy|Move|Link allowed-effect set. Owning both knobs separately is the
/// point: WinUI derives the external allowed mask from RequestedOperation
/// and copies it into the preferred drop effect, which couples "what targets
/// may do" to "what the default should be" — the multi-bit leak that made
/// Windows 10 Explorer prompt for an operation on every drop.
///
/// The dedicated thread exists because a UI-thread DoDragDrop starves: the
/// drag loop's hidden capture window never sees the mouse stream (the XAML
/// input island owns it), QueryContinueDrag is never called, and the
/// never-ending session holds the system-wide OLE drag lock — every drag in
/// every process dies until ours is killed. Running the loop on its own STA
/// puts the capture window, and therefore the mouse stream, on that thread.
///
/// Must be started from the UI thread; completion is delivered back on the
/// UI dispatcher. 1a scope: drag visuals are the system cursors only.
/// </summary>
internal static unsafe partial class NativeFileDragOut
{
    public const uint DropeffectCopy = 1;
    public const uint DropeffectMove = 2;
    public const uint DropeffectLink = 4;
    public const uint AllowedEffectsAll =
        DropeffectCopy | DropeffectMove | DropeffectLink;

    private const uint DvaspectContent = 1;
    private const uint TymedHGlobal = 1;

    private static readonly ushort s_preferredDropEffectFormat =
        Win32Helper.GetRegisteredClipboardFormat("Preferred DropEffect");

    private static readonly ushort s_sourceTagFormat =
        Win32Helper.GetRegisteredClipboardFormat("DeskBox Native Drag Source");

    // Set once a drag-out in this process has actually stamped the source-tag
    // format onto a data object. Until then no live object can carry the tag,
    // so drop targets skip the read entirely — asking a foreign
    // (out-of-process) object for the format is a guaranteed-failure
    // cross-process round trip on every DragEnter.
    private static bool s_sourceTagEverStamped;

    private static Thread? s_dragThread;
    private static System.Collections.Concurrent.BlockingCollection<Action>? s_dragQueue;
    private static NativeFileDragDropSource? s_activeDropSource;
    private static readonly object s_dragThreadGate = new();

    /// <summary>
    /// Payload of the private source-tag format: the widget id on the first
    /// line, one source path per following line, UTF-16 with a trailing NUL.
    /// DeskBox drop targets use it to recognize their own drag and no-op;
    /// foreign processes simply ignore an unknown registered format.
    /// </summary>
    internal static byte[] BuildSourceTagPayload(string widgetId, IReadOnlyList<string> paths)
    {
        var builder = new StringBuilder(widgetId);
        foreach (string path in paths)
        {
            builder.Append('\n').Append(path);
        }

        builder.Append('\0');
        return Encoding.Unicode.GetBytes(builder.ToString());
    }

    /// <summary>
    /// Reads the DeskBox source tag stamped by the drag-out off a live OLE
    /// data-object pointer. Returns false when the object does not carry the
    /// tag (foreign or WinUI-routed drag).
    /// </summary>
    internal static unsafe bool TryReadSourceTag(
        nint dataObject,
        out string widgetId,
        out IReadOnlyList<string> paths)
    {
        widgetId = string.Empty;
        paths = Array.Empty<string>();
        if (dataObject == 0 || s_sourceTagFormat == 0 ||
            !Volatile.Read(ref s_sourceTagEverStamped))
        {
            return false;
        }

        try
        {
            var receiver = new NativeOleDataObject(dataObject);
            var format = new NativeFormatEtc
            {
                ClipboardFormat = s_sourceTagFormat,
                TargetDevice = 0,
                Aspect = DvaspectContent,
                Index = -1,
                MediumType = TymedHGlobal
            };

            if (receiver.GetData(ref format, out NativeStorageMedium medium) != 0 ||
                medium.Content == 0)
            {
                return false;
            }

            try
            {
                if (!Win32Helper.TryReadGlobalMemoryText(
                        medium.Content,
                        out string payload))
                {
                    return false;
                }

                string[] lines = payload.Split('\n');
                if (lines.Length == 0 || lines[0].Length == 0)
                {
                    return false;
                }

                widgetId = lines[0];
                paths = lines.Skip(1)
                    .Where(line => line.Length > 0)
                    .ToArray();
                return true;
            }
            finally
            {
                Win32Helper.ReleaseStorageMedium(ref medium);
            }
        }
        catch (Exception ex)
        {
            App.LogVerbose($"[NativeDragOut] Source-tag read failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts a native drag-out for <paramref name="sourcePaths"/> on the
    /// dedicated STA thread and returns immediately. When the drag loop ends,
    /// <paramref name="onCompleted"/> runs on the UI dispatcher with the
    /// mapped final effect and a flag saying whether the loop completed at
    /// all. The UI thread stays free, so in-process drop targets (our own
    /// widget windows) keep receiving DragOver/Drop while the drag is live.
    /// </summary>
    internal static void BeginFileDragOut(
        IReadOnlyList<string> sourcePaths,
        string widgetId,
        Action<DataPackageOperation, bool> onCompleted)
    {
        var queue = EnsureDragThread();
        var source = new NativeFileDragDropSource();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int done = 0;
        Interlocked.Exchange(ref s_activeDropSource, source);

        App.Log(
            $"[NativeDragOut] Starting on drag thread " +
            $"paths={sourcePaths.Count} managedThreadId={Environment.CurrentManagedThreadId}");

        using var watchdog = new System.Threading.Timer(
            _ =>
            {
                if (Volatile.Read(ref done) == 0 &&
                    ReferenceEquals(Volatile.Read(ref s_activeDropSource), source) &&
                    source.QueryContinueCount == 0)
                {
                    App.Log(
                        "[NativeDragOut] WATCHDOG: QueryContinueDrag was " +
                        "never called — the drag loop is starved and holds " +
                        "the system-wide OLE drag lock. Kill DeskBox.exe to " +
                        "release it.");
                }
            },
            null,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(15));

        _ = queue.TryAdd(() =>
        {
            uint finalEffect;
            bool ran;
            try
            {
                ran = TryRunFileDragOutCore(
                    sourcePaths,
                    widgetId,
                    source,
                    stopwatch,
                    out finalEffect);
            }
            catch (Exception ex)
            {
                App.Log($"[NativeDragOut] Drag thread failed: {ex}");
                ran = false;
                finalEffect = 0;
            }
            finally
            {
                Interlocked.Exchange(ref done, 1);
            }

            DataPackageOperation result = ran
                ? MapOleEffect(finalEffect)
                : DataPackageOperation.None;
            Microsoft.UI.Dispatching.DispatcherQueue? dispatcher =
                App.UiDispatcherQueue;
            if (dispatcher is null)
            {
                App.Log(
                    "[NativeDragOut] Completion dropped: UI dispatcher " +
                    "unavailable");
                return;
            }

            _ = dispatcher.TryEnqueue(() => onCompleted(result, ran));
        });
    }

    internal static DataPackageOperation MapOleEffect(uint effect) =>
        effect switch
        {
            DropeffectCopy => DataPackageOperation.Copy,
            DropeffectMove => DataPackageOperation.Move,
            DropeffectLink => DataPackageOperation.Link,
            _ => DataPackageOperation.None
        };

    private static System.Collections.Concurrent.BlockingCollection<Action> EnsureDragThread()
    {
        lock (s_dragThreadGate)
        {
            if (s_dragQueue is not null)
            {
                return s_dragQueue;
            }

            var queue = new System.Collections.Concurrent.BlockingCollection<Action>();
            var thread = new Thread(() => DragThreadProc(queue))
            {
                IsBackground = true,
                Name = "DeskBox-NativeDragLoop",
            };
            thread.SetApartmentState(ApartmentState.STA);
            s_dragQueue = queue;
            thread.Start();
            App.Log(
                "[NativeDragOut] Dedicated drag thread created " +
                $"managedThreadId={thread.ManagedThreadId}");
            return queue;
        }
    }

    private static void DragThreadProc(
        System.Collections.Concurrent.BlockingCollection<Action> queue)
    {
        int oleHResult = Win32Helper.InitializeOleOnCurrentThread();
        App.Log(
            $"[NativeDragOut] Drag thread OLE initialized " +
            $"hr=0x{oleHResult:X8} managedThreadId={Environment.CurrentManagedThreadId}");
        try
        {
            foreach (Action work in queue.GetConsumingEnumerable())
            {
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    App.Log($"[NativeDragOut] Drag thread work failed: {ex}");
                }
            }
        }
        finally
        {
            Win32Helper.UninitializeOleOnCurrentThread();
        }
    }

    private static bool TryRunFileDragOutCore(
        IReadOnlyList<string> sourcePaths,
        string widgetId,
        NativeFileDragDropSource dropSource,
        System.Diagnostics.Stopwatch stopwatch,
        out uint finalEffect)
    {
        finalEffect = 0;

        nint dataObject = NativeShellFileDragProvider.CreateShellDataObject(sourcePaths);
        if (dataObject == 0)
        {
            App.Log(
                "[NativeDragOut] SHCreateDataObject failed " +
                $"paths={sourcePaths.Count}");
            return false;
        }

        nint dropSourcePointer = 0;
        try
        {
            if (!TryStampSourceFormats(dataObject, widgetId, sourcePaths))
            {
                return false;
            }

            dropSourcePointer = (nint)ComInterfaceMarshaller<INativeDropSource>
                .ConvertToUnmanaged(dropSource);
            if (dropSourcePointer == 0)
            {
                App.Log(
                    "[NativeDragOut] Drop-source marshalling returned null");
                return false;
            }

            int dragHResult = Win32Helper.RunOleDragDrop(
                dataObject,
                dropSourcePointer,
                AllowedEffectsAll,
                out finalEffect);
            App.Log(
                $"[NativeDragOut] DoDragDrop finished " +
                $"hr=0x{dragHResult:X8} finalEffect=0x{finalEffect:X} " +
                $"elapsedMs={stopwatch.ElapsedMilliseconds} " +
                $"queryContinueCalls={dropSource.QueryContinueCount} " +
                $"paths={sourcePaths.Count}");
            return dragHResult >= 0;
        }
        finally
        {
            if (dropSourcePointer != 0)
            {
                ComInterfaceMarshaller<INativeDropSource>.Free((void*)dropSourcePointer);
            }

            NativeShellFileDragProvider.ReleaseInterface(dataObject);
        }
    }

    private static bool TryStampSourceFormats(
        nint dataObject,
        string widgetId,
        IReadOnlyList<string> paths)
    {
        var receiver = new NativeOleDataObject(dataObject);

        if (s_preferredDropEffectFormat != 0)
        {
            uint preferred = DropeffectMove;
            if (!TrySetGlobalData(
                    receiver,
                    s_preferredDropEffectFormat,
                    BitConverter.GetBytes(preferred),
                    "Preferred DropEffect"))
            {
                return false;
            }
        }

        if (s_sourceTagFormat != 0 &&
            !TrySetGlobalData(
                receiver,
                s_sourceTagFormat,
                BuildSourceTagPayload(widgetId, paths),
                "DeskBox Native Drag Source"))
        {
            return false;
        }

        Volatile.Write(ref s_sourceTagEverStamped, true);
        return true;
    }

    private static bool TrySetGlobalData(
        NativeOleDataObject receiver,
        ushort clipboardFormat,
        byte[] payload,
        string formatName)
    {
        if (!Win32Helper.TryCreateGlobalMemory(payload, out nint globalMemory))
        {
            App.Log($"[NativeDragOut] GlobalAlloc failed for {formatName}");
            return false;
        }

        var medium = new NativeStorageMedium
        {
            MediumType = TymedHGlobal,
            Content = globalMemory,
            ReleaseUnknown = 0
        };
        var format = new NativeFormatEtc
        {
            ClipboardFormat = clipboardFormat,
            TargetDevice = 0,
            Aspect = DvaspectContent,
            Index = -1,
            MediumType = TymedHGlobal
        };

        int setResult = receiver.SetData(ref format, ref medium, release: true);
        if (setResult < 0)
        {
            // The data object rejected the medium, so ownership never moved.
            Win32Helper.FreeGlobalMemory(globalMemory);
            App.Log(
                $"[NativeDragOut] SetData({formatName}) failed " +
                $"hr=0x{setResult:X8}");
            return false;
        }

        return true;
    }
}

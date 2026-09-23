using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using DeskBox.Helpers;
using DeskBox.Platform;

namespace DeskBox.Controls;

[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
[Guid("0000010E-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface INativeDataObject
{
    [PreserveSig]
    int GetData(nint format, nint medium);

    [PreserveSig]
    int GetDataHere(nint format, nint medium);

    [PreserveSig]
    int QueryGetData(nint format);

    [PreserveSig]
    int GetCanonicalFormatEtc(nint formatIn, nint formatOut);

    [PreserveSig]
    int SetData(nint format, nint medium, int release);

    [PreserveSig]
    int EnumFormatEtc(uint direction, nint enumerator);

    [PreserveSig]
    int DAdvise(nint format, uint flags, nint adviseSink, nint connection);

    [PreserveSig]
    int DUnadvise(uint connection);

    [PreserveSig]
    int EnumDAdvise(nint enumerator);
}

/// <summary>
/// Forwards every IDataObject call to the Shell data object but answers
/// out-of-process reads of CFSTR_PREFERREDDROPEFFECT with DV_E_FORMATETC.
/// WinUI stores DataPackage.RequestedOperation in the data object as the
/// preferred drop effect and derives the external allowed-operation mask
/// from it, so the value must stay readable in-process. A multi-bit preferred
/// effect, however, makes Explorer prompt for the operation on every plain
/// drop; hiding it from other processes leaves the mask intact while Explorer
/// falls back to its ordinary defaults, exactly like a drag that originated
/// in Explorer itself.
/// </summary>
[GeneratedComClass]
internal sealed unsafe partial class PreferredDropEffectFilterDataObject :
    INativeDataObject
{
    private const int GetDataSlot = 3;
    private const int GetDataHereSlot = 4;
    private const int QueryGetDataSlot = 5;
    private const int GetCanonicalFormatEtcSlot = 6;
    private const int SetDataSlot = 7;
    private const int EnumFormatEtcSlot = 8;
    private const int DAdviseSlot = 9;
    private const int DUnadviseSlot = 10;
    private const int EnumDAdviseSlot = 11;
    private const int ReleaseSlot = 2;
    private const int DV_E_FORMATETC = unchecked((int)0x80040064);

    private static readonly ushort s_preferredDropEffectFormat =
        Win32Helper.GetRegisteredClipboardFormat("Preferred DropEffect");

    private nint _inner;

    internal PreferredDropEffectFilterDataObject(nint inner)
    {
        if (inner == 0)
        {
            throw new ArgumentException(
                "The inner data-object pointer is null.",
                nameof(inner));
        }

        _inner = inner;
        AddRef(inner);
    }

    ~PreferredDropEffectFilterDataObject()
    {
        nint inner = Interlocked.Exchange(ref _inner, 0);
        if (inner != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)
                (*(nint**)inner)[ReleaseSlot])(inner);
        }
    }

    internal static bool IsPreferredDropEffect(nint format) =>
        format != 0 &&
        ((NativeFormatEtc*)format)->ClipboardFormat ==
        s_preferredDropEffectFormat;

    /// <summary>
    /// True while servicing a COM call that originated in another process
    /// (an external drop target). In-process callers, including WinUI's own
    /// DataPackage plumbing and internal drop targets, are not in a COM call
    /// context or report the same process. The no-call-context case is
    /// pinned by CoGetCallerTidProbeTests; if a WinAppSDK update ever made an
    /// in-process direct call report S_FALSE, the mask derivation would read
    /// DV_E_FORMATETC and the verbose log below would stop showing
    /// in-process reads — that is the signal to re-run the drag matrix.
    /// Not a security boundary (see Win32Helper.IsComCallerOutOfProcess).
    /// </summary>
    internal static bool IsOutOfProcessCaller() =>
        Win32Helper.IsComCallerOutOfProcess();

    private static bool HidesFormat(nint format)
    {
        if (!IsPreferredDropEffect(format))
        {
            return false;
        }

        if (!IsOutOfProcessCaller())
        {
            App.LogVerbose(
                "[DragStart] Preferred DropEffect read in-process " +
                "(WinUI mask derivation intact)");
            return false;
        }

        App.LogVerbose(
            "[DragStart] Preferred DropEffect hidden from an " +
            "out-of-process drop target");
        return true;
    }

    public int GetData(nint format, nint medium) =>
        HidesFormat(format)
            ? DV_E_FORMATETC
            : ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Slot(GetDataSlot))(
                _inner, format, medium);

    public int GetDataHere(nint format, nint medium) =>
        HidesFormat(format)
            ? DV_E_FORMATETC
            : ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Slot(GetDataHereSlot))(
                _inner, format, medium);

    public int QueryGetData(nint format) =>
        HidesFormat(format)
            ? DV_E_FORMATETC
            : ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(QueryGetDataSlot))(
                _inner, format);

    public int GetCanonicalFormatEtc(nint formatIn, nint formatOut) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Slot(GetCanonicalFormatEtcSlot))(
            _inner, formatIn, formatOut);

    public int SetData(nint format, nint medium, int release) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, int, int>)Slot(SetDataSlot))(
            _inner, format, medium, release);

    public int EnumFormatEtc(uint direction, nint enumerator) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, nint, int>)Slot(EnumFormatEtcSlot))(
            _inner, direction, enumerator);

    public int DAdvise(nint format, uint flags, nint adviseSink, nint connection) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, nint, nint, int>)Slot(DAdviseSlot))(
            _inner, format, flags, adviseSink, connection);

    public int DUnadvise(uint connection) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(DUnadviseSlot))(
            _inner, connection);

    public int EnumDAdvise(nint enumerator) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(EnumDAdviseSlot))(
            _inner, enumerator);

    private nint Slot(int slot)
    {
        nint inner = _inner;
        if (inner == 0)
        {
            throw new ObjectDisposedException(nameof(PreferredDropEffectFilterDataObject));
        }

        nint* vtable = *(nint**)inner;
        if (vtable == null || vtable[slot] == 0)
        {
            throw new InvalidOperationException(
                $"The Shell data-object vtable does not contain slot {slot}.");
        }

        return vtable[slot];
    }

    private static void AddRef(nint unknown) =>
        _ = ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)unknown)[1])(unknown);

    /// <summary>
    /// Explicit managed root for the wrapper of the most recent drag. The
    /// source-generated CCW pins the object while native references exist,
    /// but during a drag the only native reference is the one WinUI's
    /// undocumented IDataObjectProvider contract is assumed to hold. Rooting
    /// the wrapper here makes its survival independent of that assumption;
    /// the next drag replaces it, letting the previous wrapper (and its CCW)
    /// be collected so its finalizer releases the inner data object.
    /// </summary>
    private static PreferredDropEffectFilterDataObject? s_activeWrapper;

    /// <summary>
    /// Returns an IDataObject pointer for a filtering wrapper around
    /// <paramref name="shellDataObject"/>. The caller owns one reference on
    /// the returned pointer and must release it with
    /// <see cref="ReleaseInterfacePointer"/>.
    /// </summary>
    internal static nint CreateInterfacePointer(nint shellDataObject)
    {
        var wrapper = new PreferredDropEffectFilterDataObject(shellDataObject);
        s_activeWrapper = wrapper;
        nint pointer = (nint)ComInterfaceMarshaller<INativeDataObject>
            .ConvertToUnmanaged(wrapper);
        if (pointer == 0)
        {
            s_activeWrapper = null;
            throw new InvalidOperationException(
                "Source-generated IDataObject marshalling returned a null pointer.");
        }

        return pointer;
    }

    internal static void ReleaseInterfacePointer(nint pointer)
    {
        if (pointer != 0)
        {
            ComInterfaceMarshaller<INativeDataObject>.Free((void*)pointer);
        }
    }
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lumen.UI.Rendering.Media;

[SupportedOSPlatform("windows10.0.19041.0")]
internal static class MediaFoundationNativeMethods
{
    public const uint MF_VERSION = 0x00020070;
    public const uint MFSTARTUP_FULL = 0;

    public const uint MF_SOURCE_READER_FIRST_VIDEO_STREAM = 0xFFFFFFFC;
    public const uint MF_SOURCE_READER_FIRST_AUDIO_STREAM = 0xFFFFFFFD;
    public const uint MF_SOURCE_READER_ALL_STREAMS = 0xFFFFFFFE;

    public const uint MF_SOURCE_READERF_ERROR = 0x00000001;
    public const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x00000002;
    public const uint MF_SOURCE_READERF_NEWSTREAM = 0x00000004;
    public const uint MF_SOURCE_READERF_NATIVEMEDIATYPECHANGED = 0x00000010;
    public const uint MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED = 0x00000020;
    public const uint MF_SOURCE_READERF_STREAMTICK = 0x00000100;

    public static readonly Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = new("4B826A84-7D1E-4F4F-9122-B1F72DBCDC2F");
    public static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("A634A91C-822B-41B9-A494-4DE4643612B0");

    public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f827-4970-b48b-30238e8ec436");
    public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-4282-4409-9b32-e6fd2a430453");
    public static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MF_MT_FRAME_RATE = new("c4594fda-4243-4e4a-be97-c0f5977946a0");
    public static readonly Guid MF_PD_DURATION = new("6c990a77-3e3d-4be7-85c7-0f135a32b5e1");

    public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFMediaType_Audio = new("73646175-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFVideoFormat_ARGB32 = new("00000015-0000-0010-8000-00AA00389B71");

    [StructLayout(LayoutKind.Explicit)]
    public struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(2)] public ushort wReserved1;
        [FieldOffset(4)] public ushort wReserved2;
        [FieldOffset(6)] public ushort wReserved3;
        [FieldOffset(8)] public long hVal;
        [FieldOffset(8)] public IntPtr ptrVal;
    }

    [ComImport]
    [Guid("2CD2D921-C447-44A7-A13C-4ADAB51E0129")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFAttributes
    {
        int GetItem([In] ref Guid guidKey, IntPtr pValue);
        int GetItemType([In] ref Guid guidKey, out int pType);
        int CompareItem([In] ref Guid guidKey, IntPtr Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        int Compare(IMFAttributes pTheirs, int MatchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        int GetUINT32([In] ref Guid guidKey, out uint punValue);
        int GetUINT64([In] ref Guid guidKey, out ulong punValue);
        int GetDouble([In] ref Guid guidKey, out double pfValue);
        int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        int GetString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, uint cchBufSize, out uint pcchLength);
        int GetAllocatedString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        int GetBlob([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize, out uint pcbBlobSize);
        int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        int SetItem([In] ref Guid guidKey, IntPtr Value);
        int DeleteItem([In] ref Guid guidKey);
        int DeleteAllItems();
        int SetUINT32([In] ref Guid guidKey, uint unValue);
        int SetUINT64([In] ref Guid guidKey, ulong unValue);
        int SetDouble([In] ref Guid guidKey, double fValue);
        int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        int SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        int SetBlob([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize);
        int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        int LockStore();
        int UnlockStore();
        int GetCount(out uint pcItems);
        int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        int CopyAllItems(IMFAttributes pDest);
    }

    [ComImport]
    [Guid("44AE383D-6349-436F-AA91-1D922BCAC017")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaType : IMFAttributes
    {
        // Inherited IMFAttributes methods
        new int GetItem([In] ref Guid guidKey, IntPtr pValue);
        new int GetItemType([In] ref Guid guidKey, out int pType);
        new int CompareItem([In] ref Guid guidKey, IntPtr Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        new int Compare(IMFAttributes pTheirs, int MatchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        new int GetUINT32([In] ref Guid guidKey, out uint punValue);
        new int GetUINT64([In] ref Guid guidKey, out ulong punValue);
        new int GetDouble([In] ref Guid guidKey, out double pfValue);
        new int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        new int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        new int GetString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, uint cchBufSize, out uint pcchLength);
        new int GetAllocatedString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        new int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        new int GetBlob([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize, out uint pcbBlobSize);
        new int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        new int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        new int SetItem([In] ref Guid guidKey, IntPtr Value);
        new int DeleteItem([In] ref Guid guidKey);
        new int DeleteAllItems();
        new int SetUINT32([In] ref Guid guidKey, uint unValue);
        new int SetUINT64([In] ref Guid guidKey, ulong unValue);
        new int SetDouble([In] ref Guid guidKey, double fValue);
        new int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        new int SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        new int SetBlob([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize);
        new int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        new int LockStore();
        new int UnlockStore();
        new int GetCount(out uint pcItems);
        new int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        new int CopyAllItems(IMFAttributes pDest);

        // IMFMediaType methods
        int GetMajorType(out Guid pguidMajorType);
        int IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool pfCompressed);
        int IsEqual(IMFMediaType pIMediaType, out uint pdwFlags);
        int GetRepresentation(Guid guidRepresentation, out IntPtr ppvRepresentation);
        int FreeRepresentation(Guid guidRepresentation, IntPtr pvRepresentation);
    }

    [ComImport]
    [Guid("70AE66CE-C5D2-469E-A634-27764E88E627")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSourceReader
    {
        int GetStreamSelection(uint dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] out bool pfSelected);
        int SetStreamSelection(uint dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] bool fSelected);
        int GetNativeMediaType(uint dwStreamIndex, uint dwMediaTypeIndex, out IMFMediaType ppMediaType);
        int GetCurrentMediaType(uint dwStreamIndex, out IMFMediaType ppMediaType);
        int SetMediaType(uint dwStreamIndex, IntPtr pdwReserved, IMFMediaType pMediaType);
        int SetCurrentPosition([In] ref Guid guidTimeFormat, [In] ref PROPVARIANT varPosition);
        int ReadSample(
            uint dwStreamIndex,
            uint dwControlFlags,
            out uint pdwActualStreamIndex,
            out uint pdwStreamFlags,
            out long pllTimestamp,
            out IMFSample ppSample);
        int Flush(uint dwStreamIndex);
        int GetServiceForStream(uint dwStreamIndex, [In] ref Guid guidService, [In] ref Guid riid, out IntPtr ppvObject);
        int GetPresentationAttribute(uint dwStreamIndex, [In] ref Guid guidAttribute, out PROPVARIANT pvarAttribute);
    }

    [ComImport]
    [Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSample : IMFAttributes
    {
        // Inherited IMFAttributes methods
        new int GetItem([In] ref Guid guidKey, IntPtr pValue);
        new int GetItemType([In] ref Guid guidKey, out int pType);
        new int CompareItem([In] ref Guid guidKey, IntPtr Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        new int Compare(IMFAttributes pTheirs, int MatchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        new int GetUINT32([In] ref Guid guidKey, out uint punValue);
        new int GetUINT64([In] ref Guid guidKey, out ulong punValue);
        new int GetDouble([In] ref Guid guidKey, out double pfValue);
        new int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        new int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        new int GetString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, uint cchBufSize, out uint pcchLength);
        new int GetAllocatedString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        new int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        new int GetBlob([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize, out uint pcbBlobSize);
        new int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        new int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        new int SetItem([In] ref Guid guidKey, IntPtr Value);
        new int DeleteItem([In] ref Guid guidKey);
        new int DeleteAllItems();
        new int SetUINT32([In] ref Guid guidKey, uint unValue);
        new int SetUINT64([In] ref Guid guidKey, ulong unValue);
        new int SetDouble([In] ref Guid guidKey, double fValue);
        new int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        new int SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        new int SetBlob([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize);
        new int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        new int LockStore();
        new int UnlockStore();
        new int GetCount(out uint pcItems);
        new int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        new int CopyAllItems(IMFAttributes pDest);

        // IMFSample methods
        int GetSampleFlags(out uint pdwSampleFlags);
        int SetSampleFlags(uint dwSampleFlags);
        int GetSampleTime(out long pllSampleTime);
        int SetSampleTime(long hnsSampleTime);
        int GetSampleDuration(out long pllSampleDuration);
        int SetSampleDuration(long hnsSampleDuration);
        int GetBufferCount(out uint pdwBufferCount);
        int GetBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
        int ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
        int AddBuffer(IMFMediaBuffer pBuffer);
        int RemoveBufferByIndex(uint dwIndex);
        int RemoveAllBuffers();
        int GetTotalLength(out uint pcbTotalLength);
        int CopyToBuffer(IMFMediaBuffer pBuffer);
    }

    [ComImport]
    [Guid("045FA593-87D1-4228-9A0E-87186B94FA88")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaBuffer
    {
        int Lock(out IntPtr ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);
        int Unlock();
        int GetCurrentLength(out uint pcbCurrentLength);
        int SetCurrentLength(uint cbCurrentLength);
        int GetMaxLength(out uint pcbMaxLength);
    }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFStartup(uint Version, uint dwFlags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMediaType(out IMFMediaType ppMFMediaType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateAttributes(out IMFAttributes ppMFAttributes, uint cInitialSize);

    [DllImport("mfreadwrite.dll", ExactSpelling = true)]
    public static extern int MFCreateSourceReaderFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string pwszURL,
        IMFAttributes? pAttributes,
        out IMFSourceReader ppSourceReader);
}

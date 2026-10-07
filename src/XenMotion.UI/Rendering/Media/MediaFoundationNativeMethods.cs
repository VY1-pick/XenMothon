using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace XenMotion.UI.Rendering.Media;

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

    public static readonly Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = new("FB394F3D-CCF1-42EE-BBB3-F9B845D5681D");
    public static readonly Guid MF_SOURCE_READER_D3D_MANAGER = new("EC822DA2-E1E9-4B29-A0D8-563C719F5269");

    [ComImport, Guid("E7174CFA-1C9E-48B1-8866-626226BFC258"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFDXGIBuffer
    {
        [PreserveSig] int GetResource(ref Guid iid, out IntPtr resource);
        [PreserveSig] int GetSubresourceIndex(out uint index);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid iid, out IntPtr value);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr value);
    }
    public static readonly Guid MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING = new("0F81DA2C-B537-4672-A8B2-A681B17307A3");
    public static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("A634A91C-822B-41B9-A494-4DE4643612B0");

    public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MF_PD_DURATION = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");

    public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFMediaType_Audio = new("73647561-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFVideoFormat_ARGB32 = new("00000015-0000-0010-8000-00AA00389B71");

    [StructLayout(LayoutKind.Explicit, Size = 24)]
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
    [Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFAttributes
    {
        [PreserveSig]
        int GetItem([In] ref Guid guidKey, IntPtr pValue);
        [PreserveSig]
        int GetItemType([In] ref Guid guidKey, out int pType);
        [PreserveSig]
        int CompareItem([In] ref Guid guidKey, IntPtr Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig]
        int Compare(IMFAttributes pTheirs, int MatchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig]
        int GetUINT32([In] ref Guid guidKey, out uint punValue);
        [PreserveSig]
        int GetUINT64([In] ref Guid guidKey, out ulong punValue);
        [PreserveSig]
        int GetDouble([In] ref Guid guidKey, out double pfValue);
        [PreserveSig]
        int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        [PreserveSig]
        int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        [PreserveSig]
        int GetString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig]
        int GetAllocatedString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        [PreserveSig]
        int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig]
        int GetBlob([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig]
        int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig]
        int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig]
        int SetItem([In] ref Guid guidKey, IntPtr Value);
        [PreserveSig]
        int DeleteItem([In] ref Guid guidKey);
        [PreserveSig]
        int DeleteAllItems();
        [PreserveSig]
        int SetUINT32([In] ref Guid guidKey, uint unValue);
        [PreserveSig]
        int SetUINT64([In] ref Guid guidKey, ulong unValue);
        [PreserveSig]
        int SetDouble([In] ref Guid guidKey, double fValue);
        [PreserveSig]
        int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        [PreserveSig]
        int SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig]
        int SetBlob([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize);
        [PreserveSig]
        int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig]
        int LockStore();
        [PreserveSig]
        int UnlockStore();
        [PreserveSig]
        int GetCount(out uint pcItems);
        [PreserveSig]
        int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig]
        int CopyAllItems(IMFAttributes pDest);
    }

    [ComImport]
    [Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaType : IMFAttributes
    {
        // Inherited IMFAttributes methods
        [PreserveSig]
        new int GetItem([In] ref Guid guidKey, IntPtr pValue);
        [PreserveSig]
        new int GetItemType([In] ref Guid guidKey, out int pType);
        [PreserveSig]
        new int CompareItem([In] ref Guid guidKey, IntPtr Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig]
        new int Compare(IMFAttributes pTheirs, int MatchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig]
        new int GetUINT32([In] ref Guid guidKey, out uint punValue);
        [PreserveSig]
        new int GetUINT64([In] ref Guid guidKey, out ulong punValue);
        [PreserveSig]
        new int GetDouble([In] ref Guid guidKey, out double pfValue);
        [PreserveSig]
        new int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        [PreserveSig]
        new int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        [PreserveSig]
        new int GetString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig]
        new int GetAllocatedString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        [PreserveSig]
        new int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig]
        new int GetBlob([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig]
        new int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig]
        new int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig]
        new int SetItem([In] ref Guid guidKey, IntPtr Value);
        [PreserveSig]
        new int DeleteItem([In] ref Guid guidKey);
        [PreserveSig]
        new int DeleteAllItems();
        [PreserveSig]
        new int SetUINT32([In] ref Guid guidKey, uint unValue);
        [PreserveSig]
        new int SetUINT64([In] ref Guid guidKey, ulong unValue);
        [PreserveSig]
        new int SetDouble([In] ref Guid guidKey, double fValue);
        [PreserveSig]
        new int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        [PreserveSig]
        new int SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig]
        new int SetBlob([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize);
        [PreserveSig]
        new int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig]
        new int LockStore();
        [PreserveSig]
        new int UnlockStore();
        [PreserveSig]
        new int GetCount(out uint pcItems);
        [PreserveSig]
        new int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig]
        new int CopyAllItems(IMFAttributes pDest);

        // IMFMediaType methods
        [PreserveSig]
        int GetMajorType(out Guid pguidMajorType);
        [PreserveSig]
        int IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool pfCompressed);
        [PreserveSig]
        int IsEqual(IMFMediaType pIMediaType, out uint pdwFlags);
        [PreserveSig]
        int GetRepresentation(Guid guidRepresentation, out IntPtr ppvRepresentation);
        [PreserveSig]
        int FreeRepresentation(Guid guidRepresentation, IntPtr pvRepresentation);
    }

    [ComImport]
    [Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSourceReader
    {
        [PreserveSig]
        int GetStreamSelection(uint dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] out bool pfSelected);
        [PreserveSig]
        int SetStreamSelection(uint dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] bool fSelected);
        [PreserveSig]
        int GetNativeMediaType(uint dwStreamIndex, uint dwMediaTypeIndex, out IMFMediaType ppMediaType);
        [PreserveSig]
        int GetCurrentMediaType(uint dwStreamIndex, out IMFMediaType ppMediaType);
        [PreserveSig]
        int SetMediaType(uint dwStreamIndex, IntPtr pdwReserved, IMFMediaType pMediaType);
        [PreserveSig]
        int SetCurrentPosition([In] ref Guid guidTimeFormat, [In] ref PROPVARIANT varPosition);
        [PreserveSig]
        int ReadSample(
            uint dwStreamIndex,
            uint dwControlFlags,
            out uint pdwActualStreamIndex,
            out uint pdwStreamFlags,
            out long pllTimestamp,
            out IMFSample ppSample);
        [PreserveSig]
        int Flush(uint dwStreamIndex);
        [PreserveSig]
        int GetServiceForStream(uint dwStreamIndex, [In] ref Guid guidService, [In] ref Guid riid, out IntPtr ppvObject);
        [PreserveSig]
        int GetPresentationAttribute(uint dwStreamIndex, [In] ref Guid guidAttribute, out PROPVARIANT pvarAttribute);
    }

    [ComImport]
    [Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSample : IMFAttributes
    {
        // Inherited IMFAttributes methods
        [PreserveSig]
        new int GetItem([In] ref Guid guidKey, IntPtr pValue);
        [PreserveSig]
        new int GetItemType([In] ref Guid guidKey, out int pType);
        [PreserveSig]
        new int CompareItem([In] ref Guid guidKey, IntPtr Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig]
        new int Compare(IMFAttributes pTheirs, int MatchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig]
        new int GetUINT32([In] ref Guid guidKey, out uint punValue);
        [PreserveSig]
        new int GetUINT64([In] ref Guid guidKey, out ulong punValue);
        [PreserveSig]
        new int GetDouble([In] ref Guid guidKey, out double pfValue);
        [PreserveSig]
        new int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        [PreserveSig]
        new int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        [PreserveSig]
        new int GetString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig]
        new int GetAllocatedString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        [PreserveSig]
        new int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig]
        new int GetBlob([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig]
        new int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig]
        new int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig]
        new int SetItem([In] ref Guid guidKey, IntPtr Value);
        [PreserveSig]
        new int DeleteItem([In] ref Guid guidKey);
        [PreserveSig]
        new int DeleteAllItems();
        [PreserveSig]
        new int SetUINT32([In] ref Guid guidKey, uint unValue);
        [PreserveSig]
        new int SetUINT64([In] ref Guid guidKey, ulong unValue);
        [PreserveSig]
        new int SetDouble([In] ref Guid guidKey, double fValue);
        [PreserveSig]
        new int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        [PreserveSig]
        new int SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig]
        new int SetBlob([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize);
        [PreserveSig]
        new int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig]
        new int LockStore();
        [PreserveSig]
        new int UnlockStore();
        [PreserveSig]
        new int GetCount(out uint pcItems);
        [PreserveSig]
        new int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig]
        new int CopyAllItems(IMFAttributes pDest);

        // IMFSample methods
        [PreserveSig]
        int GetSampleFlags(out uint pdwSampleFlags);
        [PreserveSig]
        int SetSampleFlags(uint dwSampleFlags);
        [PreserveSig]
        int GetSampleTime(out long pllSampleTime);
        [PreserveSig]
        int SetSampleTime(long hnsSampleTime);
        [PreserveSig]
        int GetSampleDuration(out long pllSampleDuration);
        [PreserveSig]
        int SetSampleDuration(long hnsSampleDuration);
        [PreserveSig]
        int GetBufferCount(out uint pdwBufferCount);
        [PreserveSig]
        int GetBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
        [PreserveSig]
        int ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
        [PreserveSig]
        int AddBuffer(IMFMediaBuffer pBuffer);
        [PreserveSig]
        int RemoveBufferByIndex(uint dwIndex);
        [PreserveSig]
        int RemoveAllBuffers();
        [PreserveSig]
        int GetTotalLength(out uint pcbTotalLength);
        [PreserveSig]
        int CopyToBuffer(IMFMediaBuffer pBuffer);
    }

    [ComImport]
    [Guid("045FA593-8799-42B8-BC8D-8968C6453507")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaBuffer
    {
        [PreserveSig]
        int Lock(out IntPtr ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);
        [PreserveSig]
        int Unlock();
        [PreserveSig]
        int GetCurrentLength(out uint pcbCurrentLength);
        [PreserveSig]
        int SetCurrentLength(uint cbCurrentLength);
        [PreserveSig]
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

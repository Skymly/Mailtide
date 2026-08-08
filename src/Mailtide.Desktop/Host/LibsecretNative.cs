using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mailtide.Desktop.Host;

/// <summary>
/// Minimal P/Invoke surface for libsecret simple password APIs.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class LibsecretNative
{
    private const string Libsecret = "secret-1";
    private const string GLib = "glib-2.0";

    public const int SchemaNone = 0;
    public const int SchemaAttributeString = 0;
    public const string DefaultCollection = "default";

    [DllImport(Libsecret, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr secret_schema_new(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int flags,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attributeName,
        int attributeType,
        IntPtr endNull);

    [DllImport(Libsecret, CallingConvention = CallingConvention.Cdecl)]
    public static extern void secret_schema_unref(IntPtr schema);

    [DllImport(Libsecret, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool secret_password_store_sync(
        IntPtr schema,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string collection,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string password,
        IntPtr cancellable,
        ref IntPtr error,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attributeName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attributeValue,
        IntPtr endNull);

    [DllImport(Libsecret, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr secret_password_lookup_sync(
        IntPtr schema,
        IntPtr cancellable,
        ref IntPtr error,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attributeName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attributeValue,
        IntPtr endNull);

    [DllImport(Libsecret, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool secret_password_clear_sync(
        IntPtr schema,
        IntPtr cancellable,
        ref IntPtr error,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attributeName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attributeValue,
        IntPtr endNull);

    [DllImport(Libsecret, CallingConvention = CallingConvention.Cdecl)]
    public static extern void secret_password_free(IntPtr password);

    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void g_error_free(IntPtr error);

    public static string? ReadUtf8(IntPtr ptr) =>
        ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);

    public static string? ReadGErrorMessage(IntPtr error)
    {
        if (error == IntPtr.Zero)
        {
            return null;
        }

        var gerror = Marshal.PtrToStructure<GErrorNative>(error);
        return ReadUtf8(gerror.Message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GErrorNative
    {
        public uint Domain;
        public int Code;
        public IntPtr Message;
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PowerOw;

internal sealed record FileInspection(string Status, string Publisher = "", string Product = "", string Version = "", string Hash = "");

internal static class FileInspector
{
    internal static FileInspection Inspect(string? target)
    {
        if (!CommandPath.IsLocalPath(target))
            return new("NoTarget");
        string path = target!;
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed)
            return new("NoTarget");
        if (!File.Exists(path))
            return new("MissingTarget");
        SafePaths.RequirePlain(path);
        var version = FileVersionInfo.GetVersionInfo(path);
        using var file = File.OpenRead(path);
        string hash = Convert.ToHexString(SHA256.HashData(file));
        int code = Verify(path);
        string publisher = "";
        if (code == 0)
        {
            try
            {
                // CreateFromSignedFile extracts the signer from the PE signature, which was just verified by WinTrust.
#pragma warning disable SYSLIB0057
                using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
                publisher = certificate.Subject;
            }
            catch (CryptographicException) { }
        }
        return new FileInspection(code == 0 ? "Signed" : code == unchecked((int)0x800B0100) ? "NoSignature" : "SignatureUnverified",
            publisher, version.FileDescription ?? "", version.FileVersion ?? "", hash);
    }

    private static int Verify(string file)
    {
        Guid policy = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        var info = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = file };
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        Marshal.StructureToPtr(info, pointer, false);
        var data = new TrustData
        {
            Size = (uint)Marshal.SizeOf<TrustData>(), Choice = 1, FileInfo = pointer, UI = 2,
            StateAction = 1, ProviderFlags = 0x1000 | 0x10
        };
        try { return WinVerifyTrust(IntPtr.Zero, ref policy, ref data); }
        finally
        {
            data.StateAction = 2;
            _ = WinVerifyTrust(IntPtr.Zero, ref policy, ref data);
            Marshal.DestroyStructure<TrustFile>(pointer);
            Marshal.FreeHGlobal(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public IntPtr File, Subject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public IntPtr Policy, Sip;
        public uint UI, Revocation, Choice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr State, Url;
        public uint ProviderFlags, UIContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
}

using System.Runtime.InteropServices;
using System.Text;

namespace AOVPN.Desktop;

internal static class CredentialStore
{
    private const uint Generic = 1;
    private const string Target = "AOVPN-PostLogin";

    public static (string Username, string Password)? Read()
    {
        if (!CredRead(Target, Generic, 0, out var credential))
            return null;
        try
        {
            var value = Marshal.PtrToStructure<CREDENTIAL>(credential);
            var username = value.UserName ?? string.Empty;
            var bytes = new byte[value.CredentialBlobSize];
            Marshal.Copy(value.CredentialBlob, bytes, 0, bytes.Length);
            return (username, Encoding.Unicode.GetString(bytes).TrimEnd('\0'));
        }
        finally
        {
            CredFree(credential);
        }
    }

    public static void Write(string username, string password)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmdkey.exe",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add($"/generic:{Target}");
        process.StartInfo.ArgumentList.Add($"/user:{username.Trim()}");
        process.StartInfo.ArgumentList.Add($"/pass:{password}");
        if (!process.Start())
            throw new InvalidOperationException("Windows Credential Manager could not be started.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Windows Credential Manager rejected the credential (exit code {process.ExitCode}).");
        if (Read() is not { } saved || !string.Equals(saved.Username, username.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException("Windows Credential Manager did not preserve the username.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}

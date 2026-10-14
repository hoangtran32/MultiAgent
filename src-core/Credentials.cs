using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace MultiAgentHub.Core;

public interface ISecretStore
{
    string Read(string id);
    void Write(string id, string secret);
    void Delete(string id);
}

// Windows Credential Manager keeps API keys outside JSON, Git and exported history.
public sealed class CredentialSecrets : ISecretStore
{
    private const string Prefix = "MultiAgentHub/";
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
    public string Read(string id)
    {
        if (!CredRead(Prefix + id, 1, 0, out var pointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 1168) return "";
            throw new Win32Exception(error);
        }
        try { var value = Marshal.PtrToStructure<Credential>(pointer); return Marshal.PtrToStringUni(value.CredentialBlob, (int)value.CredentialBlobSize / 2) ?? ""; }
        finally { CredFree(pointer); }
    }
    public void Write(string id, string secret)
    {
        if (secret.Length * 2 > 2560) throw new ArgumentException("API key quá dài.");
        IntPtr blob = Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var credential = new Credential { TargetName = Prefix + id, Type = 1, Persist = 2, UserName = "MultiAgentHub", CredentialBlob = blob, CredentialBlobSize = (uint)Encoding.Unicode.GetByteCount(secret) };
            if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
    }
    public void Delete(string id)
    {
        if (!CredDelete(Prefix + id, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}

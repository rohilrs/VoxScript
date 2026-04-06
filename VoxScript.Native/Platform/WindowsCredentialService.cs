using System.Runtime.InteropServices;
using System.Text;
using VoxScript.Core.Settings;

namespace VoxScript.Native.Platform;

/// <summary>Reads/writes credentials via Windows Credential Manager (DPAPI-backed).</summary>
public sealed class WindowsCredentialService : IApiKeyStore
{
    private const string AppPrefix = "VoxScript_";

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag,
        out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree([In] IntPtr buffer);

    private const uint CRED_TYPE_GENERIC = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    // IApiKeyStore implementation
    public void StoreKey(string service, string key) => Store(service, key);
    public string? GetKey(string service) => Retrieve(service);
    public void DeleteKey(string service) => Delete(service);

    public void Store(string serviceName, string value)
    {
        var blob = Encoding.UTF8.GetBytes(value);
        var gcHandle = GCHandle.Alloc(blob, GCHandleType.Pinned);
        try
        {
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = AppPrefix + serviceName,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = gcHandle.AddrOfPinnedObject(),
                Persist = 2, // CRED_PERSIST_LOCAL_MACHINE
                UserName = Environment.UserName,
            };
            if (!CredWrite(ref cred, 0))
                throw new InvalidOperationException(
                    $"CredWrite failed: {Marshal.GetLastWin32Error()}");
        }
        finally { gcHandle.Free(); }
    }

    public string? Retrieve(string serviceName)
    {
        if (!CredRead(AppPrefix + serviceName, CRED_TYPE_GENERIC, 0, out var ptr))
            return null;

        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (cred.CredentialBlobSize == 0) return null;
            var blob = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, blob, 0, blob.Length);
            return Encoding.UTF8.GetString(blob);
        }
        finally { CredFree(ptr); }
    }

    public void Delete(string serviceName) =>
        CredDelete(AppPrefix + serviceName, CRED_TYPE_GENERIC, 0);
}

using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace AtmMonitor.Agent.Identity;

public sealed record AgentCredentials(Guid AtmId, string AgentKey, string TerminalId);

public interface ICredentialStore
{
    AgentCredentials? Load();
    void Save(AgentCredentials credentials);
    void Delete();
}

/// <summary>
/// Persists the agent key. On Windows it is encrypted with DPAPI (machine scope) so it is useless if the file is copied
/// to another machine. On Linux the file is created with 0600 permissions; use disk encryption / TPM for more.
/// </summary>
public sealed class FileCredentialStore(string directory, ILogger<FileCredentialStore> logger) : ICredentialStore
{
    private static readonly byte[] Entropy = "AtmMonitor.Agent.v1"u8.ToArray();
    private string FilePath => Path.Combine(directory, "credentials.dat");

    public AgentCredentials? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(FilePath);
            if (OperatingSystem.IsWindows())
            {
                bytes = Unprotect(bytes);
            }

            return JsonSerializer.Deserialize<AgentCredentials>(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            logger.LogError(ex, "Stored credentials are unreadable; the agent will need to re-enroll");
            return null;
        }
    }

    public void Save(AgentCredentials credentials)
    {
        Directory.CreateDirectory(directory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(credentials);
        if (OperatingSystem.IsWindows())
        {
            bytes = Protect(bytes);
        }

        var tmp = FilePath + ".tmp";
        if (!OperatingSystem.IsWindows())
        {
            using var _ = new FileStream(tmp, new FileStreamOptions
            {
                Mode = FileMode.Create, Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            });
        }

        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, FilePath, overwrite: true);
    }

    public void Delete()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.LocalMachine);

    [SupportedOSPlatform("windows")]
    private static byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.LocalMachine);
}


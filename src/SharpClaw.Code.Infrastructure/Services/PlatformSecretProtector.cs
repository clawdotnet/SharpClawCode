using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using SharpClaw.Code.Infrastructure.Abstractions;

namespace SharpClaw.Code.Infrastructure.Services;

/// <summary>
/// Protects local secrets with Windows DPAPI or a user-only AES key on macOS and Linux.
/// </summary>
public sealed class PlatformSecretProtector(IUserProfilePaths userProfilePaths) : ISecretProtector
{
    private const string DpapiPrefix = "dpapi:v1:";
    private const string AesPrefix = "aesgcm:v1:";
    private const string KeyFileName = "secret-protection.key";
    private static readonly byte[] AssociatedData = "SharpClaw.Code.PlatformSecretProtector.v1"u8.ToArray();

    /// <inheritdoc />
    public bool CanProtect => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        if (!CanProtect)
        {
            throw new PlatformNotSupportedException("Protected local secret storage is unavailable on this platform.");
        }

        return OperatingSystem.IsWindows()
            ? DpapiPrefix + ProtectWithDpapi(plaintext)
            : AesPrefix + ProtectWithAes(plaintext);
    }

    /// <inheritdoc />
    public string Unprotect(string protectedPayload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedPayload);
        if (protectedPayload.StartsWith(AesPrefix, StringComparison.Ordinal))
        {
            return UnprotectWithAes(protectedPayload[AesPrefix.Length..]);
        }

        if (protectedPayload.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("DPAPI-protected secrets can only be read on Windows.");
            }

            return UnprotectWithDpapi(protectedPayload[DpapiPrefix.Length..]);
        }

        if (OperatingSystem.IsWindows())
        {
            return UnprotectWithDpapi(protectedPayload);
        }

        throw new CryptographicException("The protected secret uses an unknown or legacy platform format.");
    }

    private string ProtectWithAes(string plaintext)
    {
        var key = GetOrCreateUnixKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, AssociatedData);

            var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
            nonce.CopyTo(payload, 0);
            tag.CopyTo(payload, nonce.Length);
            ciphertext.CopyTo(payload, nonce.Length + tag.Length);
            return Convert.ToBase64String(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    private string UnprotectWithAes(string encodedPayload)
    {
        var payload = Convert.FromBase64String(encodedPayload);
        if (payload.Length < 28)
        {
            throw new CryptographicException("The protected secret payload is incomplete.");
        }

        var key = GetOrCreateUnixKey();
        var nonce = payload.AsSpan(0, 12);
        var tag = payload.AsSpan(12, 16);
        var ciphertext = payload.AsSpan(28);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private byte[] GetOrCreateUnixKey()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("AES key-file protection is available only on macOS and Linux.");
        }

        var root = userProfilePaths.GetUserSharpClawRoot();
        var directoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Directory.CreateDirectory(root, directoryMode);
        File.SetUnixFileMode(root, directoryMode);
        var keyPath = Path.Combine(root, KeyFileName);

        if (!File.Exists(keyPath))
        {
            var temporaryKeyPath = Path.Combine(root, $".{KeyFileName}.{Guid.NewGuid():N}.tmp");
            var generatedKey = RandomNumberGenerator.GetBytes(32);
            try
            {
                using var stream = new FileStream(temporaryKeyPath, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.WriteThrough,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                });
                stream.Write(generatedKey);
                stream.Flush(flushToDisk: true);
                File.Move(temporaryKeyPath, keyPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(keyPath))
            {
                // Another process created the same user-scoped key first.
            }
            finally
            {
                if (File.Exists(temporaryKeyPath))
                {
                    File.Delete(temporaryKeyPath);
                }

                CryptographicOperations.ZeroMemory(generatedKey);
            }
        }

        if ((File.GetAttributes(keyPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new CryptographicException($"The local secret-protection key at '{keyPath}' cannot be a symbolic link.");
        }

        File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var key = File.ReadAllBytes(keyPath);
        return key.Length == 32
            ? key
            : throw new CryptographicException($"The local secret-protection key at '{keyPath}' is invalid.");
    }

    [SupportedOSPlatform("windows")]
    private static string ProtectWithDpapi(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return Convert.ToBase64String(ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    [SupportedOSPlatform("windows")]
    private static string UnprotectWithDpapi(string encodedPayload)
    {
        var protectedBytes = Convert.FromBase64String(encodedPayload);
        var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

using FluentAssertions;
using System.Security.Cryptography;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Services;

namespace SharpClaw.Code.UnitTests.Infrastructure;

/// <summary>
/// Verifies portable user-scoped secret protection.
/// </summary>
public sealed class PlatformSecretProtectorTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"sharpclaw-secret-{Guid.NewGuid():N}");

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Ensures protected payloads round-trip without persisting plaintext.
    /// </summary>
    [Fact]
    public void Protect_should_roundtrip_without_plaintext()
    {
        var protector = new PlatformSecretProtector(new TestUserProfilePaths(_tempDirectory));

        var payload = protector.Protect("super-secret-value");

        payload.Should().NotContain("super-secret-value");
        protector.Unprotect(payload).Should().Be("super-secret-value");
        protector.CanProtect.Should().BeTrue();
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(_tempDirectory).Should().Be(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.GetUnixFileMode(Path.Combine(_tempDirectory, "secret-protection.key")).Should().Be(
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>
    /// Ensures tampered payloads cannot be decrypted.
    /// </summary>
    [Fact]
    public void Unprotect_should_reject_tampered_payload()
    {
        var protector = new PlatformSecretProtector(new TestUserProfilePaths(_tempDirectory));
        var payload = protector.Protect("super-secret-value");
        var prefixLength = payload.LastIndexOf(':') + 1;
        var bytes = Convert.FromBase64String(payload[prefixLength..]);
        bytes[^1] ^= 1;
        var tamperedPayload = payload[..prefixLength] + Convert.ToBase64String(bytes);

        var act = () => protector.Unprotect(tamperedPayload);

        act.Should().Throw<CryptographicException>();
    }

    private sealed class TestUserProfilePaths(string root) : IUserProfilePaths
    {
        public string GetUserHomeDirectory() => root;

        public string GetUserSharpClawRoot() => root;

        public string GetUserCustomCommandsDirectory() => Path.Combine(root, "commands");
    }
}

using FluentAssertions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Web.Services;

namespace SharpClaw.Code.UnitTests.Runtime;

/// <summary>Exercises malicious paths, network destinations, and interrupted durable writes.</summary>
public sealed class LaunchSecurityRegressionTests
{
    [Theory]
    [InlineData("../..")]
    [InlineData("..\\..")]
    [InlineData("/tmp")]
    [InlineData("C:\\users")]
    [InlineData(".")]
    public void Package_ids_cannot_be_paths(string id)
        => FluentActions.Invoking(() => LocalPackagePath.ValidateId(id)).Should().Throw<ArgumentException>();

    [Theory]
    [InlineData("../other-workspace")]
    [InlineData("/tmp/elsewhere")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:\\users")]
    public void Session_ids_cannot_be_paths(string sessionId)
    {
        var resolver = SharpClaw.Code.UnitTests.Support.TestRuntimeStorageResolver.Create(Path.GetTempPath());
        FluentActions.Invoking(() => resolver.GetSessionSnapshotPath(Path.GetTempPath(), sessionId)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => resolver.GetCheckpointPath(Path.GetTempPath(), "session-1", sessionId)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Generated_session_ids_resolve_inside_the_sessions_root()
    {
        var resolver = SharpClaw.Code.UnitTests.Support.TestRuntimeStorageResolver.Create(Path.GetTempPath());
        var workspace = Path.GetTempPath();
        resolver.GetSessionSnapshotPath(workspace, $"session-{Guid.NewGuid():N}")
            .Should().StartWith(resolver.GetSessionsRoot(workspace) + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void Dangling_symlink_cannot_hide_an_outside_write()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sharpclaw-links-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.txt");
            var link = Path.Combine(root, "new.txt");
            try { File.CreateSymbolicLink(link, outside); }
            catch (UnauthorizedAccessException) { return; }
            catch (PlatformNotSupportedException) { return; }
            var paths = new PathService();
            paths.GetCanonicalFullPath(link).Should().Be(paths.GetCanonicalFullPath(outside));
            FluentActions.Invoking(() => LocalPackagePath.Resolve(paths, root, "new.txt")).Should().Throw<ArgumentException>();
            File.Exists(outside).Should().BeFalse();
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("http://127.0.0.1/secret")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://192.0.2.10/")]
    [InlineData("http://192.0.0.8/")]
    [InlineData("http://[2002:c0a8:101::1]/")]
    [InlineData("http://[2001:0:4136:e378::1]/")]
    [InlineData("file:///etc/passwd")]
    public async Task Web_fetch_rejects_private_destinations_before_sending(string url)
    {
        using var client = new HttpClient(new RejectNetwork());
        var service = new WebFetchService(client);
        await FluentActions.Awaiting(() => service.FetchAsync(url, CancellationToken.None)).Should().ThrowAsync<HttpRequestException>();
    }

    [Theory]
    [InlineData("http://192.0.78.9/")]
    [InlineData("http://93.184.215.14/")]
    [InlineData("http://[2606:4700::1111]/")]
    public async Task Web_fetch_allows_public_literal_destinations(string url)
    {
        using var client = new HttpClient(new AcceptNetwork());
        var service = new WebFetchService(client);
        (await service.FetchAsync(url, CancellationToken.None)).Content.Should().Contain("ok");
    }

    [Fact]
    public async Task Web_fetch_truncates_oversized_bodies()
    {
        using var client = new HttpClient(new AcceptNetwork(new string('a', WebFetchService.MaxBodyBytes + 4096)));
        var service = new WebFetchService(client);
        (await service.FetchAsync("http://192.0.78.9/", CancellationToken.None)).Content.Length.Should().BeLessThanOrEqualTo(WebFetchService.MaxBodyBytes);
    }

    [Fact]
    public async Task Web_fetch_revalidates_redirect_targets()
    {
        using var client = new HttpClient(new RedirectToMetadata());
        var service = new WebFetchService(client);
        await FluentActions.Awaiting(() => service.FetchAsync("https://example.com/", CancellationToken.None)).Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Cancelled_snapshot_replacement_keeps_previous_content_and_append_repairs_torn_record()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sharpclaw-durability-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "session.json");
            var files = new LocalFileSystem();
            await files.WriteAllTextAsync(path, "previous", CancellationToken.None);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await FluentActions.Awaiting(() => files.WriteAllTextAsync(path, "replacement", cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
            (await File.ReadAllTextAsync(path)).Should().Be("previous");
            Directory.GetFiles(directory, "*.tmp").Should().BeEmpty();
            var log = Path.Combine(directory, "events.ndjson");
            await File.WriteAllTextAsync(log, "{\"event\":1}\n{\"torn\"");
            await files.AppendLineAsync(log, "{\"event\":2}", CancellationToken.None);
            (await File.ReadAllLinesAsync(log)).Should().Equal("{\"event\":1}", "{\"event\":2}");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Excessive_subprocess_output_is_bounded_and_keeps_head_and_tail()
    {
        if (OperatingSystem.IsWindows()) return;
        var runner = new ProcessRunner(new SystemClock());
        var request = new SharpClaw.Code.Infrastructure.Models.ProcessRunRequest("/bin/sh",
            ["-c", "echo FIRST; yes x | head -c 3000000; echo; echo LAST"], Path.GetTempPath(), null);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var result = await runner.RunAsync(request, deadline.Token);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Length.Should().BeLessThan(ProcessRunner.MaxRetainedCharacters + 200);
        result.StandardOutput.Should().StartWith("FIRST").And.Contain("characters of output omitted");
        result.StandardOutput.TrimEnd().Should().EndWith("LAST");
    }

    [Fact]
    public async Task Atomic_rewrite_keeps_the_executable_bit()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), $"sharpclaw-mode-{Guid.NewGuid():N}.sh");
        try
        {
            await File.WriteAllTextAsync(path, "#!/bin/sh\necho one\n");
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
            File.SetUnixFileMode(path, mode);

            await new LocalFileSystem().WriteAllTextAsync(path, "#!/bin/sh\necho two\n", CancellationToken.None);

            File.GetUnixFileMode(path).Should().Be(mode);
            (await File.ReadAllTextAsync(path)).Should().Contain("two");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Background_child_holding_the_pipe_does_not_block_completion()
    {
        if (OperatingSystem.IsWindows()) return;
        var runner = new ProcessRunner(new SystemClock());
        var request = new SharpClaw.Code.Infrastructure.Models.ProcessRunRequest("/bin/sh",
            ["-c", "echo started; sleep 30 &"], Path.GetTempPath(), null, TimeSpan.FromSeconds(20));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = await runner.RunAsync(request, CancellationToken.None);

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("started");
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Private destinations must be rejected before HTTP.");
    }

    private sealed class AcceptNetwork(string body = "ok") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    private sealed class RedirectToMetadata : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri!.Host.Should().Be("example.com");
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data");
            return Task.FromResult(response);
        }
    }
}

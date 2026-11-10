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
    [InlineData("file:///etc/passwd")]
    public async Task Web_fetch_rejects_private_destinations_before_sending(string url)
    {
        using var client = new HttpClient(new RejectNetwork());
        var service = new WebFetchService(client);
        await FluentActions.Awaiting(() => service.FetchAsync(url, CancellationToken.None)).Should().ThrowAsync<HttpRequestException>();
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
    public async Task Excessive_subprocess_output_is_stopped_and_reported()
    {
        if (OperatingSystem.IsWindows()) return;
        var runner = new ProcessRunner(new SystemClock());
        var request = new SharpClaw.Code.Infrastructure.Models.ProcessRunRequest("/bin/sh",
            ["-c", "yes x | head -c 1200000"], Path.GetTempPath(), null);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await FluentActions.Awaiting(() => runner.RunAsync(request, deadline.Token)).Should().ThrowAsync<IOException>().WithMessage("*output exceeded*");
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Private destinations must be rejected before HTTP.");
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

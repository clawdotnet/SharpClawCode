using Microsoft.Extensions.Hosting;
using SharpClaw.Code;
using WorkerServiceHost;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(_ => new SharpClawRuntimeHostBuilder(args).Build());
builder.Services.AddHostedService<EmbeddedRuntimeLifecycleService>();
builder.Services.AddHostedService<PromptWorker>();

await builder.Build().RunAsync();

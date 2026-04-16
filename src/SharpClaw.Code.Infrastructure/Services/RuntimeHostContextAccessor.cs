using System.Threading;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Infrastructure.Services;

/// <inheritdoc />
public sealed class RuntimeHostContextAccessor : IRuntimeHostContextAccessor
{
    private readonly AsyncLocal<Holder?> current = new();

    /// <inheritdoc />
    public RuntimeHostContext? Current => current.Value?.Context;

    /// <inheritdoc />
    public IDisposable BeginScope(RuntimeHostContext? context)
    {
        var previous = current.Value;
        current.Value = new Holder(context);
        return new Scope(current, previous);
    }

    private sealed record Holder(RuntimeHostContext? Context);

    private sealed class Scope(AsyncLocal<Holder?> current, Holder? previous) : IDisposable
    {
        public void Dispose()
        {
            current.Value = previous;
        }
    }
}

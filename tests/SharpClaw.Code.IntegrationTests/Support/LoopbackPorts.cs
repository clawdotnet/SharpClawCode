using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace SharpClaw.Code.IntegrationTests.Support;

/// <summary>
/// Hands out loopback TCP ports that no other test in this process has been given.
/// </summary>
/// <remarks>
/// Probing with a port-0 listener and releasing it is inherently racy: tests running in parallel could
/// receive the same "free" port, so one server failed to bind and its test talked to another test's listener.
/// Tracking every issued port removes the in-process collisions; callers should still retry on a bind failure.
/// </remarks>
internal static class LoopbackPorts
{
    private static readonly ConcurrentDictionary<int, byte> Issued = new();

    /// <summary>Reserves a loopback port not previously issued in this process.</summary>
    /// <returns>The port number.</returns>
    public static int Reserve()
    {
        while (true)
        {
            int port;
            using (var probe = new TcpListener(IPAddress.Loopback, 0))
            {
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
            }

            if (Issued.TryAdd(port, 0))
            {
                return port;
            }
        }
    }
}

using System.Net;
using System.Net.Sockets;

namespace SharpClaw.Code.Web.Fetch;

/// <summary>
/// Server-side request forgery guard for web fetches: only public unicast addresses may be contacted.
/// </summary>
public static class WebFetchNetworkPolicy
{
    /// <summary>
    /// Determines whether an address is a public, routable unicast address.
    /// </summary>
    /// <param name="address">The resolved address.</param>
    /// <returns><see langword="true"/> when the address may be contacted.</returns>
    public static bool IsPublicAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.Broadcast))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0                                   // 0.0.0.0/8 "this network"
                || b[0] == 10                                    // 10.0.0.0/8 private
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)    // 100.64.0.0/10 carrier-grade NAT
                || b[0] == 127                                   // loopback
                || (b[0] == 169 && b[1] == 254)                  // link-local, incl. cloud metadata 169.254.169.254
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)     // 172.16.0.0/12 private
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)       // 192.0.0.0/24 IETF protocol assignments
                || (b[0] == 192 && b[1] == 168)                  // 192.168.0.0/16 private
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))   // 198.18.0.0/15 benchmarking
                || b[0] >= 224);                                 // multicast and reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC                         // fc00::/7 unique local
                || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B)); // 64:ff9b::/96 NAT64 can target private IPv4
        }

        return false;
    }

    /// <summary>
    /// Creates a primary handler whose connections are only made to vetted public addresses. Because the check
    /// runs when each connection is opened (and connects to the exact address checked), it also covers redirects
    /// and DNS rebinding.
    /// </summary>
    /// <param name="allowPrivateNetworkAccess">Disables the guard when <see langword="true"/>.</param>
    /// <returns>The configured handler.</returns>
    public static SocketsHttpHandler CreateHandler(bool allowPrivateNetworkAccess)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All,
            // A proxy would make the connection target the proxy instead of the vetted host.
            UseProxy = allowPrivateNetworkAccess,
        };

        if (allowPrivateNetworkAccess)
        {
            return handler;
        }

        handler.ConnectCallback = async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            var target = addresses.FirstOrDefault(IsPublicAddress)
                ?? throw new HttpRequestException(
                    $"Refusing to fetch '{host}': it resolves only to loopback, private, link-local, or otherwise non-public addresses.");

            var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };

        return handler;
    }
}

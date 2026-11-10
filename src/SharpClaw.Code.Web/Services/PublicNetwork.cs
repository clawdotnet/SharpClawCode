using System.Net;
using System.Net.Sockets;

namespace SharpClaw.Code.Web.Services;

/// <summary>Restricts web fetch connections to public HTTP destinations, including after DNS resolution.</summary>
internal static class PublicNetwork
{
    internal static void ValidateUri(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0
            || uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(uri.DnsSafeHost, out var address) && !IsPublic(address)))
            throw new HttpRequestException("Web fetch requires a public HTTP(S) destination.");
    }

    internal static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return (bytes[0] & 0xe0) == 0x20 && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
        return bytes[0] is not (0 or 10 or 127) && bytes[0] < 224
            && !(bytes[0] == 169 && bytes[1] == 254)
            && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            && !(bytes[0] == 192 && (bytes[1] == 168 || bytes[1] == 0))
            && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            && !(bytes[0] == 198 && bytes[1] is 18 or 19);
    }

    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
            if (addresses.Length == 0 || addresses.Any(address => !IsPublic(address)))
                throw new HttpRequestException("Web fetch cannot connect to private or reserved addresses.");
            // Connect to the validated address itself so DNS rebinding cannot change the destination.
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        },
    };
}

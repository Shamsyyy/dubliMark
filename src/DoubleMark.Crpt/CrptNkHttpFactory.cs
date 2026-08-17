using System.Net;
using System.Net.Sockets;

namespace DoubleMark.Crpt;

/// <summary>Creates <see cref="HttpClient"/> instances tuned for NK API (connect timeout, proxy, IPv4 preference).</summary>
public static class CrptNkHttpFactory
{
    public static HttpClient CreateClient(string baseUrl, int timeoutSeconds, int? connectTimeoutSeconds = null)
    {
        var connectTimeout = connectTimeoutSeconds ?? Math.Clamp(timeoutSeconds / 3, 15, 60);
        var handler = new SocketsHttpHandler
        {
            UseProxy = true,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(connectTimeout),
            ConnectCallback = ConnectPreferIpv4Async,
        };

        return new HttpClient(handler)
        {
            BaseAddress = CrptHttp.CreateBaseAddress(baseUrl),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };
    }

    internal static async ValueTask<Stream> ConnectPreferIpv4Async(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken)
            .ConfigureAwait(false);

        var ordered = addresses
            .OrderBy(static a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .ToArray();

        Exception? lastError = null;
        foreach (var address in ordered)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken)
                    .ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex)
            {
                lastError = ex;
                socket.Dispose();
            }
        }

        throw lastError ?? new SocketException((int)SocketError.HostNotFound);
    }
}

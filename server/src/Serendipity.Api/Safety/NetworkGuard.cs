using System.Net;
using System.Net.Sockets;

namespace Serendipity.Api.Safety;

/// <summary>
/// プレビュー取得でサーバー内部のネットワークへ届かないようにする(SSRF 対策)。
/// 接続の直前に解決済みのIPを確かめるので、DNS の差し替えでもすり抜けられない。
/// </summary>
public static class NetworkGuard
{
    public static bool IsPublicAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return false;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 0
                || b[0] == 10
                || b[0] == 127
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // CGNAT
                || (b[0] == 169 && b[1] == 254)                  // リンクローカル・メタデータ
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 192 && b[1] == 0 && b[2] == 2)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                || (b[0] == 203 && b[1] == 0 && b[2] == 113)
                || b[0] >= 224);
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6None) || ip.Equals(IPAddress.IPv6Any)) return false;
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return false;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xfe) == 0xfc) return false;             // ユニークローカル fc00::/7
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8) return false; // 文書用
            return true;
        }
        return false;
    }

    /// <summary>公開アドレスにだけ接続する HttpClient 用ハンドラ。</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(5),
        AutomaticDecompression = DecompressionMethods.All,
        ConnectCallback = async (ctx, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
            var target = addresses.FirstOrDefault(IsPublicAddress)
                ?? throw new HttpRequestException($"公開されていないアドレスには接続しません: {ctx.DnsEndPoint.Host}");
            if (addresses.Any(a => !IsPublicAddress(a)))
                throw new HttpRequestException($"内部アドレスに解決されるホストには接続しません: {ctx.DnsEndPoint.Host}");
            var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(target, ctx.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };
}

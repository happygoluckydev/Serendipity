using System.Text.RegularExpressions;

namespace Serendipity.Api.Safety;

/// <summary>
/// 収益目的のリンク(アフィリエイト)を検出して PR 表示を付ける。
/// 見落としより付けすぎの方を選ぶ(ステマ規制への対応)。
/// </summary>
public static partial class AffiliateDetector
{
    /// <summary>それ自体がアフィリエイトの計測・転送に使われるホスト。</summary>
    private static readonly string[] AffiliateHosts =
    [
        "px.a8.net",
        "a8.net",
        "af.moshimo.com",
        "hb.afl.rakuten.co.jp",
        "afl.rakuten.co.jp",
        "ck.jp.ap.valuecommerce.com",
        "mvp.jp.ap.valuecommerce.com",
        "h.accesstrade.net",
        "t.afi-b.com",
        "t.felmat.net",
        "click.linksynergy.com",
        "linksynergy.jrs5.com",
        "amzn.to",
        "amzn.asia",
        "tr.affiliate-b.com",
        "track.affiliate-b.com",
    ];

    /// <summary>アフィリエイトIDを運ぶ代表的なクエリ。ホストとの組で判定する。</summary>
    private static readonly (string HostSuffix, string Param)[] AffiliateParams =
    [
        ("amazon.co.jp", "tag"),
        ("amazon.com", "tag"),
        ("rakuten.co.jp", "scid"),
        ("mercari.com", "afid"),
    ];

    private static readonly string[] GenericParams = ["affiliate", "affiliate_id", "aff_id", "affid", "aff"];

    public static bool IsAffiliateUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        foreach (var h in AffiliateHosts)
        {
            if (host == h || host.EndsWith("." + h, StringComparison.Ordinal)) return true;
        }

        var query = ParseQuery(uri.Query);
        foreach (var (suffix, param) in AffiliateParams)
        {
            if ((host == suffix || host.EndsWith("." + suffix, StringComparison.Ordinal)) && query.ContainsKey(param))
                return true;
        }
        foreach (var p in GenericParams)
        {
            if (query.ContainsKey(p)) return true;
        }
        if (query.TryGetValue("utm_medium", out var medium) && medium.Contains("affiliate", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>文中のURLと、自己申告の表示(#PR、#広告 など)を調べる。</summary>
    public static bool TextHasPr(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (DisclosureRegex().IsMatch(text)) return true;
        foreach (Match m in UrlRegex().Matches(text))
        {
            if (IsAffiliateUrl(m.Value)) return true;
        }
        return false;
    }

    public static bool AnyAffiliate(IEnumerable<string> urls) => urls.Any(IsAffiliateUrl);

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = Uri.UnescapeDataString(eq < 0 ? part : part[..eq]);
            var value = eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..]);
            result.TryAdd(key, value);
        }
        return result;
    }

    [GeneratedRegex(@"https?://[^\s<>""'）)]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(#|＃)\s*(PR|広告|プロモーション|ad)\b|【\s*(PR|広告)\s*】|\[\s*(PR|広告)\s*\]|アフィリエイト", RegexOptions.IgnoreCase)]
    private static partial Regex DisclosureRegex();
}

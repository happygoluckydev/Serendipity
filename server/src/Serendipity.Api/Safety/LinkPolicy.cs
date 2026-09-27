using System.Net;
using Serendipity.Api.Domain;

namespace Serendipity.Api.Safety;

public static class LinkPolicy
{
    /// <summary>
    /// 設計書の許可リスト初期案。EC・出店サービスとブログサービス。
    /// 自前ドメインの個人ブログや個人店は確認待ちにする(閉じすぎない)。
    /// </summary>
    public static readonly IReadOnlyList<(string Domain, string Note)> InitialAllowList =
    [
        ("amazon.co.jp", "EC"),
        ("amazon.com", "EC"),
        ("amzn.asia", "EC(短縮URL)"),
        ("amzn.to", "EC(短縮URL)"),
        ("rakuten.co.jp", "EC"),
        ("thebase.in", "出店サービス BASE"),
        ("base.shop", "出店サービス BASE"),
        ("base.ec", "出店サービス BASE"),
        ("stores.jp", "出店サービス STORES"),
        ("minne.com", "出店サービス minne"),
        ("creema.jp", "出店サービス Creema"),
        ("mercari.com", "メルカリ"),
        ("mercari-shops.com", "メルカリShops"),
        ("note.com", "ブログ note"),
        ("hatenablog.com", "ブログ はてなブログ"),
        ("hatenablog.jp", "ブログ はてなブログ"),
        ("hateblo.jp", "ブログ はてなブログ"),
        ("hatenadiary.com", "ブログ はてなブログ"),
        ("hatenadiary.jp", "ブログ はてなブログ"),
    ];

    public const int MaxUrlLength = 2048;

    /// <summary>リンクURLを検証し、正規化したURLとドメインを返す。不正なら null と理由。</summary>
    public static (Uri? Uri, string? Domain, string? Error) Normalize(string raw)
    {
        raw = raw.Trim();
        if (raw.Length == 0) return (null, null, "URLが空です");
        if (raw.Length > MaxUrlLength) return (null, null, "URLが長すぎます");
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return (null, null, "URLの形式が正しくありません");
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return (null, null, "http または https のURLだけ置けます");
        if (!string.IsNullOrEmpty(uri.UserInfo)) return (null, null, "ユーザー情報を含むURLは置けません");
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 || IPAddress.TryParse(uri.Host, out _))
            return (null, null, "IPアドレスのURLは置けません");
        if (!uri.IsDefaultPort) return (null, null, "ポート番号を含むURLは置けません");
        var host = uri.IdnHost.ToLowerInvariant().TrimEnd('.');
        if (!host.Contains('.')) return (null, null, "ドメインが正しくありません");
        return (uri, HostKey(host), null);
    }

    /// <summary>"www." を外したホスト名。ドメインルールの照合に使う。</summary>
    public static string HostKey(string host)
    {
        host = host.ToLowerInvariant().TrimEnd('.');
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>
    /// ホストに当たるルールを探す。完全一致のあと親ドメインへ遡る(jp.mercari.com → mercari.com)。
    /// ルールがなければ null(未知ドメイン)。
    /// </summary>
    public static DomainRuleKind? Resolve(string host, IReadOnlyDictionary<string, DomainRuleKind> rules)
    {
        var h = HostKey(host);
        while (true)
        {
            if (rules.TryGetValue(h, out var kind)) return kind;
            var dot = h.IndexOf('.');
            if (dot < 0 || h.IndexOf('.', dot + 1) < 0) return null;
            h = h[(dot + 1)..];
        }
    }
}

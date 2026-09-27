using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using AngleSharp.Html.Parser;
using Serendipity.Api.Storage;

namespace Serendipity.Api.Safety;

public sealed record FetchedPreview(
    string? Title,
    string? SiteName,
    byte[]? Image,
    string? ImageExtension,
    string BodyExcerpt,
    string ContentHash,
    IReadOnlyList<string> Links,
    string FinalUrl);

public interface IPreviewFetcher
{
    /// <summary>リンク先を取得する。取得できなければ例外。</summary>
    Task<FetchedPreview> FetchAsync(Uri url, CancellationToken ct);
}

/// <summary>
/// リンク先のタイトル・OGP・本文の抜粋を取り込む。
/// 街に出すのは取り込んだ写しだけにし、リンク先の変更が街に直接出ないようにする。
/// </summary>
public class HttpPreviewFetcher(HttpClient http) : IPreviewFetcher
{
    public const string HttpClientName = "preview";
    private const int MaxHtmlBytes = 2 * 1024 * 1024;
    private const int MaxExcerptChars = 6000;

    public async Task<FetchedPreview> FetchAsync(Uri url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        var finalUrl = res.RequestMessage?.RequestUri ?? url;

        var mediaType = res.Content.Headers.ContentType?.MediaType ?? "";
        if (!mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            var bodyHash = Hash($"{mediaType}|{res.Content.Headers.ContentLength}");
            return new FetchedPreview(null, null, null, null, "", bodyHash, [], finalUrl.ToString());
        }

        var bytes = await ReadLimitedAsync(res.Content, MaxHtmlBytes, ct);
        var charset = res.Content.Headers.ContentType?.CharSet;
        var encoding = TryEncoding(charset) ?? Encoding.UTF8;
        var html = encoding.GetString(bytes);

        var doc = await new HtmlParser().ParseDocumentAsync(html, ct);
        string? Meta(string key) =>
            doc.QuerySelector($"meta[property='{key}']")?.GetAttribute("content")
            ?? doc.QuerySelector($"meta[name='{key}']")?.GetAttribute("content");

        var title = Clean(Meta("og:title") ?? doc.Title, 300);
        var siteName = Clean(Meta("og:site_name"), 200);
        foreach (var junk in doc.QuerySelectorAll("script,style,noscript,svg,template")) junk.Remove();
        var root = doc.QuerySelector("article") ?? doc.QuerySelector("main") ?? doc.Body;
        var text = Collapse(root?.TextContent ?? "");
        var excerpt = text.Length > MaxExcerptChars ? text[..MaxExcerptChars] : text;

        var links = doc.QuerySelectorAll("a[href]")
            .Select(a => a.GetAttribute("href"))
            .Where(h => h is not null && h.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .Select(h => h!)
            .Distinct()
            .Take(500)
            .ToList();

        byte[]? image = null;
        string? ext = null;
        var og = Meta("og:image");
        if (og is not null && Uri.TryCreate(finalUrl, og, out var imageUrl)
            && (imageUrl.Scheme == Uri.UriSchemeHttps || imageUrl.Scheme == Uri.UriSchemeHttp))
        {
            try
            {
                using var imgRes = await http.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                if (imgRes.IsSuccessStatusCode)
                {
                    var data = await ReadLimitedAsync(imgRes.Content, ImageFiles.MaxBytes, ct);
                    ext = ImageFiles.DetectExtension(data);
                    if (ext is not null) image = data;
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidDataException)
            {
                // 画像が取れなくてもプレビューは成立する。
            }
        }

        var hash = Hash($"{title}\n{excerpt}");
        return new FetchedPreview(title, siteName, image, ext, excerpt, hash, links, finalUrl.ToString());
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken ct)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("リンク先が大きすぎます");
        await using var s = await content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int read;
        while ((read = await s.ReadAsync(buf, ct)) > 0)
        {
            if (ms.Length + read > limit)
            {
                ms.Write(buf, 0, (int)(limit - ms.Length));
                break;
            }
            ms.Write(buf, 0, read);
        }
        return ms.ToArray();
    }

    private static Encoding? TryEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) return null;
        try { return Encoding.GetEncoding(charset.Trim('"')); }
        catch (ArgumentException) { return null; }
    }

    internal static string Hash(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static string Collapse(string s) =>
        string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = Collapse(s);
        return s.Length > max ? s[..max] : s;
    }
}

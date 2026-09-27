using System.Text.RegularExpressions;

namespace Serendipity.Api.Storage;

/// <summary>ポップ写真とプレビュー画像の置き場。本番ではオブジェクトストレージに差し替える。</summary>
public interface IObjectStorage
{
    Task<string> PutAsync(byte[] data, string extension, CancellationToken ct = default);
    Task<(Stream Stream, string ContentType)?> OpenAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}

public static partial class ImageFiles
{
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>先頭のバイト列から画像形式を判定する。拡張子や Content-Type は信用しない。</summary>
    public static string? DetectExtension(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return "jpg";
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47
            && d[4] == 0x0D && d[5] == 0x0A && d[6] == 0x1A && d[7] == 0x0A) return "png";
        if (d.Length >= 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F'
            && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P') return "webp";
        if (d.Length >= 6 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F' && d[3] == '8') return "gif";
        return null;
    }

    public static string ContentType(string extension) => extension switch
    {
        "jpg" => "image/jpeg",
        "png" => "image/png",
        "webp" => "image/webp",
        "gif" => "image/gif",
        _ => "application/octet-stream",
    };

    public static bool IsValidKey(string key) => KeyRegex().IsMatch(key);

    public static string UrlFor(string? key) => key is null ? "" : $"/files/{key}";

    [GeneratedRegex("^[0-9a-f]{32}\\.(jpg|png|webp|gif)$")]
    private static partial Regex KeyRegex();
}

public class LocalDiskStorage(string root) : IObjectStorage
{
    public async Task<string> PutAsync(byte[] data, string extension, CancellationToken ct = default)
    {
        Directory.CreateDirectory(root);
        var key = $"{Guid.NewGuid():N}.{extension}";
        await File.WriteAllBytesAsync(Path.Combine(root, key), data, ct);
        return key;
    }

    public Task<(Stream Stream, string ContentType)?> OpenAsync(string key, CancellationToken ct = default)
    {
        if (!ImageFiles.IsValidKey(key)) return Task.FromResult<(Stream, string)?>(null);
        var path = Path.Combine(root, key);
        if (!File.Exists(path)) return Task.FromResult<(Stream, string)?>(null);
        var ext = Path.GetExtension(key).TrimStart('.');
        Stream s = File.OpenRead(path);
        return Task.FromResult<(Stream, string)?>((s, ImageFiles.ContentType(ext)));
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(ImageFiles.IsValidKey(key) && File.Exists(Path.Combine(root, key)));
}

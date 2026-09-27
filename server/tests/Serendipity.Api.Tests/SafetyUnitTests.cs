using System.Net;
using Serendipity.Api.Domain;
using Serendipity.Api.Safety;
using Serendipity.Api.Storage;

namespace Serendipity.Api.Tests;

public class AffiliateDetectorTests
{
    [Theory]
    [InlineData("https://www.amazon.co.jp/dp/B000000000?tag=someone-22")]
    [InlineData("https://amzn.to/3abcdef")]
    [InlineData("https://hb.afl.rakuten.co.jp/hgc/123/?pc=https%3A%2F%2Fitem.rakuten.co.jp")]
    [InlineData("https://px.a8.net/svt/ejp?a8mat=ABC")]
    [InlineData("https://af.moshimo.com/af/c/click?a_id=1")]
    [InlineData("https://ck.jp.ap.valuecommerce.com/servlet/referral?sid=1")]
    [InlineData("https://jp.mercari.com/item/m123?afid=999")]
    [InlineData("https://example.com/item?utm_medium=affiliate")]
    public void DetectsAffiliateLinks(string url) => Assert.True(AffiliateDetector.IsAffiliateUrl(url));

    [Theory]
    [InlineData("https://www.amazon.co.jp/dp/B000000000")]
    [InlineData("https://note.com/someone/n/n123")]
    [InlineData("https://example.com/?tag=blue")]
    [InlineData("not a url")]
    public void LeavesOrdinaryLinksAlone(string url) => Assert.False(AffiliateDetector.IsAffiliateUrl(url));

    [Theory]
    [InlineData("最高のざる #PR")]
    [InlineData("【広告】新しい靴")]
    [InlineData("ここで買えます https://amzn.to/xyz")]
    public void DetectsDisclosureAndLinksInText(string text) => Assert.True(AffiliateDetector.TextHasPr(text));

    [Fact]
    public void PlainTextHasNoPr() => Assert.False(AffiliateDetector.TextHasPr("毎日使う竹のざる。PRONTO で買った"));
}

public class LinkPolicyTests
{
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/file")]
    [InlineData("https://192.168.0.1/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://user:pass@example.com/")]
    [InlineData("https://example.com:8443/")]
    [InlineData("https://localhost/")]
    [InlineData("")]
    public void RejectsUnsafeUrls(string raw) => Assert.Null(LinkPolicy.Normalize(raw).Uri);

    [Fact]
    public void NormalizesHostForRules()
    {
        var (uri, domain, error) = LinkPolicy.Normalize("  https://WWW.Note.com/a/b?x=1 ");
        Assert.Null(error);
        Assert.NotNull(uri);
        Assert.Equal("note.com", domain);
    }

    [Fact]
    public void ResolvesRulesThroughParentDomains()
    {
        var rules = new Dictionary<string, DomainRuleKind>
        {
            ["mercari.com"] = DomainRuleKind.Allow,
            ["bad.example"] = DomainRuleKind.Deny,
        };
        Assert.Equal(DomainRuleKind.Allow, LinkPolicy.Resolve("jp.mercari.com", rules));
        Assert.Equal(DomainRuleKind.Deny, LinkPolicy.Resolve("www.shop.bad.example", rules));
        Assert.Null(LinkPolicy.Resolve("someones-blog.jp", rules));
        // 末尾だけが一致する別ドメインは当たらない。
        Assert.Null(LinkPolicy.Resolve("notmercari.com", rules));
    }
}

public class NetworkGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.5.4")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:10.0.0.1")]
    public void BlocksInternalAddresses(string ip) => Assert.False(NetworkGuard.IsPublicAddress(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    public void AllowsPublicAddresses(string ip) => Assert.True(NetworkGuard.IsPublicAddress(IPAddress.Parse(ip)));
}

public class ImageFilesTests
{
    [Fact]
    public void DetectsFormatsFromMagicBytes()
    {
        Assert.Equal("jpg", ImageFiles.DetectExtension([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal("png", ImageFiles.DetectExtension([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.Equal("webp", ImageFiles.DetectExtension("RIFF\0\0\0\0WEBP"u8.ToArray()));
        Assert.Null(ImageFiles.DetectExtension("<svg onload=alert(1)>"u8.ToArray()));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.jpg", true)]
    [InlineData("../etc/passwd", false)]
    [InlineData("0123456789abcdef0123456789abcdef.svg", false)]
    public void ValidatesStorageKeys(string key, bool ok) => Assert.Equal(ok, ImageFiles.IsValidKey(key));
}

public class RuleBasedModeratorTests
{
    [Fact]
    public async Task RejectsAdultAndFlagsAgeRestricted()
    {
        var m = new RuleBasedModerator();
        Assert.Equal(ModerationVerdict.Reject, (await m.ModerateAsync(new("R18の本"), default)).Verdict);
        Assert.Equal(ModerationVerdict.Review, (await m.ModerateAsync(new("旅先で買った日本酒"), default)).Verdict);
        Assert.Equal(ModerationVerdict.Ok, (await m.ModerateAsync(new("毎朝使う木のバターナイフ"), default)).Verdict);
    }
}

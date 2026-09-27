namespace Serendipity.Api.Domain;

/// <summary>
/// 街の無常に関わる値。設計書の未決事項(重みの式・減衰の速さ・消えるまでの期間)の暫定値で、
/// appsettings の "City" から変えられる。
/// </summary>
public class CityOptions
{
    /// <summary>反応の重みが半分になるまでの日数。</summary>
    public double WeightHalfLifeDays { get; set; } = 3;
    /// <summary>重みの上限。小さい新しいマークを見失わせないため。</summary>
    public double WeightCap { get; set; } = 6;
    public double ViewWeight { get; set; } = 0.1;
    public double TapWeight { get; set; } = 0.5;
    public double SaveWeight { get; set; } = 2;
    public double PlacedNextWeight { get; set; } = 3;
    /// <summary>反応がないまま、この日数を過ぎると薄れ始める。</summary>
    public double FadeStartDays { get; set; } = 21;
    /// <summary>反応がないまま、この日数を過ぎると街から消える。</summary>
    public double GoneAfterDays { get; set; } = 45;
    /// <summary>新しく置かれたマークを控えめに光らせる期間。</summary>
    public double NewGlowDays { get; set; } = 3;
    /// <summary>同じ端末からの閲覧を重複して数えない時間。</summary>
    public double ViewDedupHours { get; set; } = 6;
    public double TapDedupHours { get; set; } = 1;
}

public class SafetyOptions
{
    /// <summary>ポップの文字数の上限(暫定)。腕一本分離れても読める短さ。</summary>
    public int PopMaxLength { get; set; } = 60;
    /// <summary>通報の重みの合計がこれを超えると自動で非表示。</summary>
    public double ReportHideThreshold { get; set; } = 3;
    public int DailyMarkLimitNew { get; set; } = 5;
    public int DailyMarkLimitNormal { get; set; } = 20;
    public int DailyMarkLimitTrusted { get; set; } = 100;
    /// <summary>リンク先を再チェックする間隔(時間)。</summary>
    public double RecheckIntervalHours { get; set; } = 24;
    /// <summary>取得失敗がこの回数続いたらリンク切れとする。</summary>
    public int LinkBrokenAfterFailures { get; set; } = 3;
}

public class JobOptions
{
    public bool Enabled { get; set; } = true;
    public int ReviewIntervalSeconds { get; set; } = 5;
    public int MaintenanceIntervalSeconds { get; set; } = 300;
    public int RecheckIntervalSeconds { get; set; } = 3600;
}

public class AdminOptions
{
    /// <summary>運営用の鍵。未設定なら運営APIは使えない。</summary>
    public string? Key { get; set; }
}

public class ModerationOptions
{
    /// <summary>"claude" または "rules"。未指定なら ANTHROPIC_API_KEY があれば claude。</summary>
    public string? Provider { get; set; }
    /// <summary>設計書の「軽量モデル中心」に合わせた既定値。</summary>
    public string Model { get; set; } = "claude-haiku-4-5";
}

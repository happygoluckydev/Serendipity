using Serendipity.Api.Domain;

namespace Serendipity.Api.City;

public readonly record struct EngagementSample(EngagementKind Kind, DateTimeOffset At);

/// <summary>重みと薄れ具合の計算。累計ではなく最近の反応で決め、時間とともに縮む。</summary>
public static class Vitality
{
    public static double KindWeight(EngagementKind kind, CityOptions o) => kind switch
    {
        EngagementKind.View => o.ViewWeight,
        EngagementKind.Tap => o.TapWeight,
        EngagementKind.Save => o.SaveWeight,
        EngagementKind.PlacedNext => o.PlacedNextWeight,
        _ => 0,
    };

    /// <summary>1 + Σ 種別の重み × 0.5^(経過日数/半減期)。上限で打ち切る。</summary>
    public static double Weight(IEnumerable<EngagementSample> samples, DateTimeOffset now, CityOptions o)
    {
        var sum = 0.0;
        foreach (var s in samples)
        {
            var ageDays = Math.Max(0, (now - s.At).TotalDays);
            sum += KindWeight(s.Kind, o) * Math.Pow(0.5, ageDays / o.WeightHalfLifeDays);
        }
        return Math.Min(o.WeightCap, 1 + sum);
    }

    /// <summary>見た目の濃さ(0.35〜1)。反応がない期間が長いほど薄くなる。</summary>
    public static double Opacity(DateTimeOffset lastEngagedAt, DateTimeOffset now, CityOptions o)
    {
        var idle = (now - lastEngagedAt).TotalDays;
        if (idle <= o.FadeStartDays) return 1;
        var t = (idle - o.FadeStartDays) / Math.Max(1e-6, o.GoneAfterDays - o.FadeStartDays);
        return Math.Clamp(1 - 0.65 * t, 0.35, 1);
    }

    public static bool ShouldBeGone(DateTimeOffset lastEngagedAt, DateTimeOffset now, CityOptions o) =>
        (now - lastEngagedAt).TotalDays > o.GoneAfterDays;

    /// <summary>反応として数え、薄れを止めるもの。閲覧は受け身なので含めない。</summary>
    public static bool RefreshesLife(EngagementKind kind) => kind != EngagementKind.View;
}

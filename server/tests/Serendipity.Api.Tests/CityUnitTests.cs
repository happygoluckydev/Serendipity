using Serendipity.Api.City;
using Serendipity.Api.Domain;

namespace Serendipity.Api.Tests;

public class VitalityTests
{
    private static readonly CityOptions O = new();
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecentReactionsCountMoreThanOldOnes()
    {
        var fresh = Vitality.Weight([new(EngagementKind.Save, Now)], Now, O);
        var halfLifeAgo = Vitality.Weight([new(EngagementKind.Save, Now.AddDays(-O.WeightHalfLifeDays))], Now, O);
        Assert.Equal(1 + O.SaveWeight, fresh, 6);
        Assert.Equal(1 + O.SaveWeight / 2, halfLifeAgo, 6);
    }

    [Fact]
    public void WeightIsCapped()
    {
        var many = Enumerable.Repeat(new EngagementSample(EngagementKind.PlacedNext, Now), 100);
        Assert.Equal(O.WeightCap, Vitality.Weight(many, Now, O));
    }

    [Fact]
    public void MarksFadeThenDisappear()
    {
        Assert.Equal(1, Vitality.Opacity(Now.AddDays(-5), Now, O));
        var fading = Vitality.Opacity(Now.AddDays(-(O.FadeStartDays + O.GoneAfterDays) / 2), Now, O);
        Assert.InRange(fading, 0.35, 0.99);
        Assert.False(Vitality.ShouldBeGone(Now.AddDays(-O.GoneAfterDays + 1), Now, O));
        Assert.True(Vitality.ShouldBeGone(Now.AddDays(-O.GoneAfterDays - 1), Now, O));
    }

    [Fact]
    public void ViewsDoNotKeepAMarkAlive()
    {
        Assert.False(Vitality.RefreshesLife(EngagementKind.View));
        Assert.True(Vitality.RefreshesLife(EngagementKind.Tap));
    }
}

public class CityLayoutTests
{
    [Fact]
    public void PlacesNextToAnchorWithoutOverlap()
    {
        var rng = new Random(1);
        var nodes = new List<LayoutNode> { new(Guid.NewGuid(), 0, 0, CityLayout.Radius(4)) };
        for (var i = 0; i < 60; i++)
        {
            var anchor = nodes[rng.Next(nodes.Count)];
            var r = CityLayout.Radius(1 + rng.NextDouble() * 3);
            var (x, y) = CityLayout.PlaceNear(anchor, r, nodes, rng.Next());
            foreach (var o in nodes)
            {
                var d = Math.Sqrt((o.X - x) * (o.X - x) + (o.Y - y) * (o.Y - y));
                Assert.True(d >= o.R + r - 1e-6, $"overlap with {o.Id}: d={d}, need {o.R + r}");
            }
            nodes.Add(new LayoutNode(Guid.NewGuid(), x, y, r));
        }
    }

    [Fact]
    public void RelaxSeparatesOverlapsAndKeepsNeighboursClose()
    {
        var nodes = new List<LayoutNode>
        {
            new(Guid.NewGuid(), 0, 0, 10),
            new(Guid.NewGuid(), 1, 0, 10),
            new(Guid.NewGuid(), 300, 0, 10),
        };
        CityLayout.Relax(nodes, [(0, 2)], 400);
        double Dist(int a, int b) => Math.Sqrt(Math.Pow(nodes[a].X - nodes[b].X, 2) + Math.Pow(nodes[a].Y - nodes[b].Y, 2));
        Assert.True(Dist(0, 1) >= 19.5, $"still overlapping: {Dist(0, 1)}");
        Assert.True(Dist(0, 2) < 60, $"neighbours drifted apart: {Dist(0, 2)}");
    }
}

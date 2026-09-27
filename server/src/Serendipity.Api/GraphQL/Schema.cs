using Serendipity.Api.City;
using Serendipity.Api.Domain;
using Serendipity.Api.Identity;
using Serendipity.Api.Safety;

namespace Serendipity.Api.GraphQL;

public class Query
{
    /// <summary>街。範囲を渡せばその周りだけ、渡さなければ全体。</summary>
    public async Task<CityView> GetCity(
        double? minX, double? minY, double? maxX, double? maxY,
        [Service] ViewerAccessor viewer, [Service] CityService city, CancellationToken ct)
    {
        Bounds? bounds = minX is not null && minY is not null && maxX is not null && maxY is not null
            ? new Bounds(minX.Value, minY.Value, maxX.Value, maxY.Value)
            : null;
        return await city.GetCityAsync(await viewer.GetAsync(), bounds, ct);
    }

    public async Task<MarkDetail> GetMark(Guid id, [Service] ViewerAccessor viewer, [Service] CityService city, CancellationToken ct) =>
        await city.GetMarkAsync(await viewer.GetAsync(), id, ct);

    public async Task<MeView> GetMe([Service] ViewerAccessor viewer, [Service] AccountService accounts, CancellationToken ct) =>
        await accounts.MeAsync(await viewer.GetAsync(), ct);

    public async Task<IReadOnlyList<SaveView>> GetSaves([Service] ViewerAccessor viewer, [Service] SaveService saves, CancellationToken ct) =>
        await saves.ListAsync(await viewer.GetAsync(), ct);

    public async Task<ShopView?> GetMyShop([Service] ViewerAccessor viewer, [Service] ShopService shops, CancellationToken ct) =>
        await shops.MyShopAsync(await viewer.GetAsync(), ct);

    public async Task<ShopView> GetShop(Guid id, [Service] ViewerAccessor viewer, [Service] ShopService shops, CancellationToken ct) =>
        await shops.GetShopAsync(await viewer.GetAsync(), id, ct);

    public async Task<AdminQueue> GetAdminQueue([Service] ViewerAccessor viewer, [Service] SafetyService safety, CancellationToken ct) =>
        await safety.QueueAsync(await viewer.GetAsync(), ct);
}

public class Mutation
{
    public async Task<MeView> Register(string displayName, [Service] ViewerAccessor viewer, [Service] AccountService accounts, CancellationToken ct) =>
        await accounts.RegisterAsync(await viewer.GetAsync(), displayName, ct);

    public async Task<MeView> UpdateSettings(bool isKidsMode, [Service] ViewerAccessor viewer, [Service] AccountService accounts, CancellationToken ct) =>
        await accounts.UpdateSettingsAsync(await viewer.GetAsync(), isKidsMode, ct);

    public async Task<PlaceResult> PlaceMark(PlaceMarkInput input, [Service] ViewerAccessor viewer, [Service] CityService city, CancellationToken ct) =>
        await city.PlaceMarkAsync(await viewer.GetAsync(), input, ct);

    public async Task<PopView> AddPop(AddPopInput input, [Service] ViewerAccessor viewer, [Service] CityService city, CancellationToken ct) =>
        await city.AddPopAsync(await viewer.GetAsync(), input, ct);

    public async Task<int> RecordEngagements(List<Guid> markIds, EngagementKind kind,
        [Service] ViewerAccessor viewer, [Service] CityService city, CancellationToken ct) =>
        await city.RecordEngagementsAsync(await viewer.GetAsync(), markIds, kind, ct);

    public async Task<SaveView> SaveMark(Guid markId, [Service] ViewerAccessor viewer, [Service] SaveService saves, CancellationToken ct) =>
        await saves.SaveAsync(await viewer.GetAsync(), markId, ct);

    public async Task<bool> Unsave(Guid saveId, [Service] ViewerAccessor viewer, [Service] SaveService saves, CancellationToken ct) =>
        await saves.UnsaveAsync(await viewer.GetAsync(), saveId, ct);

    public async Task<ShopView> OpenShop(string name, [Service] ViewerAccessor viewer, [Service] ShopService shops, CancellationToken ct) =>
        await shops.OpenShopAsync(await viewer.GetAsync(), name, ct);

    public async Task<ShopView> RenameShop(string name, [Service] ViewerAccessor viewer, [Service] ShopService shops, CancellationToken ct) =>
        await shops.RenameShopAsync(await viewer.GetAsync(), name, ct);

    public async Task<ShopView> AddShopItem(Guid saveId, ExperienceStage stage, [Service] ViewerAccessor viewer, [Service] ShopService shops, CancellationToken ct) =>
        await shops.AddItemAsync(await viewer.GetAsync(), saveId, stage, ct);

    public async Task<ShopView> UpdateShopItem(Guid itemId, ExperienceStage stage, [Service] ViewerAccessor viewer, [Service] ShopService shops, CancellationToken ct) =>
        await shops.UpdateItemAsync(await viewer.GetAsync(), itemId, stage, ct);

    public async Task<ShopView> RemoveShopItem(Guid itemId, [Service] ViewerAccessor viewer, [Service] ShopService shops, CancellationToken ct) =>
        await shops.RemoveItemAsync(await viewer.GetAsync(), itemId, ct);

    public async Task<ShopView> SetShopPublished(bool published, Guid? nextToMarkId,
        [Service] ViewerAccessor viewer, [Service] ShopService shops, CancellationToken ct) =>
        await shops.SetPublishedAsync(await viewer.GetAsync(), published, nextToMarkId, ct);

    public async Task<ReportResult> Report(Guid? markId, Guid? popId, string reason,
        [Service] ViewerAccessor viewer, [Service] SafetyService safety, CancellationToken ct) =>
        await safety.ReportAsync(await viewer.GetAsync(), markId, popId, reason, ct);

    public async Task<ReportResult> RequestTakedown(Guid markId, string contact, string reason,
        [Service] ViewerAccessor viewer, [Service] SafetyService safety, CancellationToken ct) =>
        await safety.RequestTakedownAsync(await viewer.GetAsync(), markId, contact, reason, ct);

    // ---- 運営 ----

    public async Task<bool> AdminApproveMark(Guid markId, [Service] ViewerAccessor viewer, [Service] SafetyService safety, CancellationToken ct) =>
        await safety.ApproveMarkAsync(await viewer.GetAsync(), markId, ct);

    public async Task<bool> AdminHideMark(Guid markId, string? note, [Service] ViewerAccessor viewer, [Service] SafetyService safety, CancellationToken ct) =>
        await safety.HideMarkAsync(await viewer.GetAsync(), markId, note, ct);

    public async Task<bool> AdminApprovePop(Guid popId, [Service] ViewerAccessor viewer, [Service] SafetyService safety, CancellationToken ct) =>
        await safety.ApprovePopAsync(await viewer.GetAsync(), popId, ct);

    public async Task<bool> AdminHidePop(Guid popId, [Service] ViewerAccessor viewer, [Service] SafetyService safety, CancellationToken ct) =>
        await safety.HidePopAsync(await viewer.GetAsync(), popId, ct);

    public async Task<bool> AdminResolveReport(Guid reportId, bool dismiss, [Service] ViewerAccessor viewer, [Service] SafetyService safety, CancellationToken ct) =>
        await safety.ResolveReportAsync(await viewer.GetAsync(), reportId, dismiss, ct);

    public async Task<AdminDomainView> AdminSetDomainRule(string domain, DomainRuleKind kind, string? note,
        [Service] ViewerAccessor viewer, [Service] SafetyService safety, CancellationToken ct) =>
        await safety.SetDomainRuleAsync(await viewer.GetAsync(), domain, kind, note, ct);

    public async Task<bool> AdminBlockUser(Guid userId, bool blocked, [Service] ViewerAccessor viewer, [Service] SafetyService safety, CancellationToken ct) =>
        await safety.BlockUserAsync(await viewer.GetAsync(), userId, blocked, ct);

    public async Task<bool> AdminSetTrust(Guid userId, TrustLevel trust, [Service] ViewerAccessor viewer, [Service] SafetyService safety, CancellationToken ct) =>
        await safety.SetTrustAsync(await viewer.GetAsync(), userId, trust, ct);
}

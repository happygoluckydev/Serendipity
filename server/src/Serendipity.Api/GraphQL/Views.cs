using Serendipity.Api.Domain;

namespace Serendipity.Api.GraphQL;

/// <summary>街ビューに並べる一つのマーク。描画に要るものだけを持つ。</summary>
public sealed record CityMark(
    Guid Id,
    double X,
    double Y,
    double Weight,
    MarkShape Shape,
    Genre Genre,
    ExperienceStage Stage,
    MarkStatus Status,
    bool IsMine,
    bool IsNew,
    double Opacity,
    string PopText,
    string? PopPhotoUrl,
    int PopCount,
    bool HasPr,
    bool IsShop,
    Guid? ShopId,
    IReadOnlyList<Guid> NeighborIds,
    DateTimeOffset CreatedAt);

public sealed record CityView(
    IReadOnlyList<CityMark> Marks,
    double BaseRadius,
    DateTimeOffset GeneratedAt);

public sealed record PopView(
    Guid Id,
    string AuthorName,
    string Text,
    string? PhotoUrl,
    ExperienceStage Stage,
    bool HasPr,
    bool IsOriginal,
    PopStatus Status,
    DateTimeOffset CreatedAt);

public sealed record PreviewView(string? Title, string? SiteName, string? ImageUrl);

public sealed record NeighborView(Guid Id, string PopText, MarkShape Shape, Genre Genre);

public sealed record MarkDetail(
    Guid Id,
    MarkShape Shape,
    Genre Genre,
    ExperienceStage Stage,
    MarkStatus Status,
    PendingReason? PendingReason,
    bool IsMine,
    string OwnerName,
    /// <summary>子ども向けの状態では null(外部リンクは開けない)。</summary>
    string? LinkUrl,
    string? Domain,
    bool HasLink,
    bool HasPr,
    bool LinkBroken,
    IReadOnlyList<PopView> Pops,
    PreviewView? Preview,
    IReadOnlyList<NeighborView> Neighbors,
    Guid? SaveId,
    bool CanAddPop,
    Guid? ShopId,
    DateTimeOffset CreatedAt);

/// <summary>保存時点の写し。元のマークが消えても、これだけで表示できる。</summary>
public sealed record SnapshotNeighbor(Guid MarkId, string PopText, MarkShape Shape, Genre Genre);

public sealed record SaveSnapshot(
    Guid MarkId,
    MarkShape Shape,
    Genre Genre,
    ExperienceStage Stage,
    string? LinkUrl,
    string? Domain,
    bool HasPr,
    string? Title,
    string? ImageUrl,
    string PopText,
    string? PopPhotoUrl,
    string PopAuthorName,
    Guid PopAuthorId,
    IReadOnlyList<SnapshotNeighbor> Neighbors,
    DateTimeOffset CapturedAt);

public enum MarkPresence
{
    /// <summary>今も街にある。</summary>
    InCity,
    /// <summary>もう街にはない(消滅・非表示・売り切れ・閉店を同じ扱いにする)。</summary>
    Gone,
}

public sealed record SaveView(
    Guid Id,
    DateTimeOffset SavedAt,
    SaveSnapshot Snapshot,
    MarkPresence Presence,
    double? CurrentX,
    double? CurrentY,
    bool InMyShop);

public sealed record MeView(
    bool HasDevice,
    bool IsRegistered,
    Guid? UserId,
    string? DisplayName,
    TrustLevel Trust,
    bool IsKidsMode,
    bool IsAdmin,
    Guid? ShopId,
    int SaveCount);

public sealed record ShopItemView(
    Guid Id,
    ExperienceStage Stage,
    SaveSnapshot Snapshot,
    string OriginalAuthorName,
    Guid OriginalMarkId,
    bool OriginalInCity,
    int Position);

public sealed record ShopView(
    Guid Id,
    string Name,
    string OwnerName,
    bool IsPublished,
    bool IsMine,
    Guid? MarkId,
    bool MarkInCity,
    IReadOnlyList<ShopItemView> Items);

public sealed record PlaceResult(Guid MarkId, MarkStatus Status, PendingReason? PendingReason, string Message);

public sealed record ReportResult(bool Accepted, string Message);

public sealed record AdminMarkView(
    Guid Id,
    MarkStatus Status,
    PendingReason? PendingReason,
    string? ReviewNote,
    string? LinkUrl,
    string? Domain,
    string OwnerName,
    Guid OwnerId,
    string PopText,
    double ReportWeight,
    DateTimeOffset CreatedAt);

public sealed record AdminPopView(Guid Id, Guid MarkId, string AuthorName, string Text, string? ReviewNote, DateTimeOffset CreatedAt);

public sealed record AdminReportView(
    Guid Id,
    ReportKind Kind,
    Guid? MarkId,
    Guid? PopId,
    string Reason,
    string? Contact,
    double Weight,
    DateTimeOffset CreatedAt);

public sealed record AdminDomainView(string Domain, DomainRuleKind Kind, string? Note);

public sealed record AdminQueue(
    IReadOnlyList<AdminMarkView> Marks,
    IReadOnlyList<AdminPopView> Pops,
    IReadOnlyList<AdminReportView> Reports,
    IReadOnlyList<AdminDomainView> Domains);

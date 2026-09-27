namespace Serendipity.Api.Domain;

public class User
{
    public Guid Id { get; set; }
    public required string DisplayName { get; set; }
    public TrustLevel Trust { get; set; } = TrustLevel.New;
    public bool IsKidsMode { get; set; }
    /// <summary>ブラックリスト。置いたマークはすべて非表示になる。</summary>
    public bool IsBlocked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// 未登録の閲覧・保存を扱う端末。クライアントが生成した Device ID は資格情報でもあるので、
/// ハッシュだけを保存する。
/// </summary>
public class Device
{
    public required string KeyHash { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public bool IsKidsMode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public class Mark
{
    public Guid Id { get; set; }
    public MarkShape Shape { get; set; }
    public Genre Genre { get; set; }
    public string? LinkUrl { get; set; }
    public string? Domain { get; set; }
    public Guid OwnerUserId { get; set; }
    public User? Owner { get; set; }
    public MarkStatus Status { get; set; }
    public PendingReason? PendingReason { get; set; }
    /// <summary>審査メモ(AI判定の理由など)。運営だけが見る。</summary>
    public string? ReviewNote { get; set; }
    /// <summary>最近の反応から計算した重み。1が基準で、上限あり。</summary>
    public double Weight { get; set; } = 1;
    /// <summary>種の座標。隣接関係から計算される派生値。</summary>
    public double SeedX { get; set; }
    public double SeedY { get; set; }
    public bool HasPr { get; set; }
    public bool LinkBroken { get; set; }
    /// <summary>自分の店を表すマークなら、その店。</summary>
    public Guid? ShopId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastEngagedAt { get; set; }
    public DateTimeOffset? GoneAt { get; set; }

    public List<Pop> Pops { get; set; } = [];
    public Preview? Preview { get; set; }
}

/// <summary>置いたときの「隣」。街の記憶の本体。A が後から置かれた側。</summary>
public class Adjacency
{
    public long Id { get; set; }
    public Guid MarkAId { get; set; }
    public Guid MarkBId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Pop
{
    public Guid Id { get; set; }
    public Guid MarkId { get; set; }
    public Mark? Mark { get; set; }
    public Guid AuthorUserId { get; set; }
    public User? Author { get; set; }
    public required string Text { get; set; }
    public string? PhotoKey { get; set; }
    public ExperienceStage Stage { get; set; }
    public bool HasPr { get; set; }
    public PopStatus Status { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Preview
{
    public Guid Id { get; set; }
    public Guid MarkId { get; set; }
    public string? Title { get; set; }
    public string? SiteName { get; set; }
    public string? ImageKey { get; set; }
    public string? ContentHash { get; set; }
    public bool HasPr { get; set; }
    public string? FetchError { get; set; }
    public int FailureCount { get; set; }
    public DateTimeOffset? FetchedAt { get; set; }
}

public class Engagement
{
    public long Id { get; set; }
    public Guid MarkId { get; set; }
    public EngagementKind Kind { get; set; }
    public required string ActorKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Save
{
    public Guid Id { get; set; }
    /// <summary>未登録のときは端末に紐づく。登録するとユーザーへ移る。</summary>
    public string? DeviceKeyHash { get; set; }
    public Guid? UserId { get; set; }
    public Guid MarkId { get; set; }
    /// <summary>保存時点のスナップショット(JSON)。元のマークが消えても保存は成立する。</summary>
    public required string SnapshotJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Shop
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public User? Owner { get; set; }
    public required string Name { get; set; }
    public bool IsPublished { get; set; }
    public Guid? MarkId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<ShopItem> Items { get; set; } = [];
}

public class ShopItem
{
    public Guid Id { get; set; }
    public Guid ShopId { get; set; }
    public Guid SaveId { get; set; }
    public Save? Save { get; set; }
    public ExperienceStage Stage { get; set; }
    public int Position { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Report
{
    public Guid Id { get; set; }
    public ReportKind Kind { get; set; }
    public Guid? MarkId { get; set; }
    public Guid? PopId { get; set; }
    public required string ReporterKey { get; set; }
    public Guid? ReporterUserId { get; set; }
    public required string Reason { get; set; }
    /// <summary>削除依頼の連絡先。</summary>
    public string? Contact { get; set; }
    public double Weight { get; set; }
    public ReportStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class DomainRule
{
    public required string Domain { get; set; }
    public DomainRuleKind Kind { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

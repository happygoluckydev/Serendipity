namespace Serendipity.Api.Domain;

/// <summary>マークの形。既製品・古着・雑貨・お店・記事の5種。</summary>
public enum MarkShape
{
    Product,
    Vintage,
    Goods,
    Store,
    Article,
}

/// <summary>
/// マークのジャンル(色)。設計書の「6〜8色」に合わせた暫定の8分類。
/// 色だけに意味を頼らないよう、クライアントはラベルも併記する。
/// </summary>
public enum Genre
{
    Fashion,
    Living,
    Food,
    Books,
    Hobby,
    Tech,
    Beauty,
    Place,
}

/// <summary>置いた人とそのモノの関係。</summary>
public enum ExperienceStage
{
    /// <summary>使って良かった</summary>
    Used,
    /// <summary>気になる</summary>
    Curious,
    /// <summary>欲しい</summary>
    Want,
}

/// <summary>マークの状態。確認待ち・公開・非表示・消滅。</summary>
public enum MarkStatus
{
    Pending,
    Public,
    Hidden,
    Gone,
}

/// <summary>確認待ちになっている理由。</summary>
public enum PendingReason
{
    /// <summary>自動審査(プレビュー取得・AI判定)の順番待ち。</summary>
    AutoReview,
    /// <summary>許可リスト外のドメイン。本人のみ可視で、運営の確認後に公開。</summary>
    UnknownDomain,
    /// <summary>AI判定が人の確認を求めた。</summary>
    AiFlagged,
}

public enum PopStatus
{
    Pending,
    Visible,
    Hidden,
}

public enum EngagementKind
{
    View,
    Tap,
    Save,
    PlacedNext,
}

public enum DomainRuleKind
{
    Allow,
    Review,
    Deny,
}

public enum ReportKind
{
    /// <summary>利用者からの通報。</summary>
    Abuse,
    /// <summary>記事の書き手からの削除依頼。</summary>
    Takedown,
}

public enum ReportStatus
{
    Open,
    Resolved,
    Dismissed,
}

/// <summary>利用者の信頼度の段階。通報の重みと、置ける数の上限に効く。</summary>
public enum TrustLevel
{
    New = 0,
    Normal = 1,
    Trusted = 2,
}

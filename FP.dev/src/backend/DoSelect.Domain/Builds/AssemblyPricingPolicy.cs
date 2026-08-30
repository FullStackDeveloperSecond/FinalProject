namespace DoSelect.Domain.Builds;

/// <summary>
/// 組長 PR #29 round 7 review (P1): the NT$300／台組裝服務費規則之前由
/// <c>DoSelect.Infrastructure.Builds.EfBuildListService</c> 與
/// <c>DoSelect.Infrastructure.Shopping.EfCartService</c> 各自硬編碼一份（Cart 那份甚至寫死
/// 0m，完全沒有計費），兩處會各自漂移。單一定義放在 Domain 層，因為 Cart 裡的一個
/// AssemblyGroupKey 本質上就是一台已加入購物車的組裝機——跟 BuildList 代表的是同一個「組裝服務費」
/// 概念，只是materialize 的位置不同（見 商品、組裝與相容性.md）。
/// </summary>
public static class AssemblyPricingPolicy
{
    public const decimal FeePerUnit = 300m;
}

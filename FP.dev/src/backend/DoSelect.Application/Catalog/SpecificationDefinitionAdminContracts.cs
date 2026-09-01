using DoSelect.Application.Common;

namespace DoSelect.Application.Catalog;

/// <summary>
/// A-09 `/admin/catalog/specifications`：分類規格範本、Option、排序與受保護 Semantic Key
/// (M功能桌面UI與Route規格)。端點形狀依 API Endpoint 目錄「M 規格範本」列。
/// </summary>
public sealed record SpecificationDefinitionQuery(
    Guid? CategoryPublicId,
    string? Q,
    bool? IsActive,
    int PageNumber,
    int PageSize);

public sealed record SpecificationOptionDto(
    Guid PublicId,
    string Code,
    string DisplayNameZhTw,
    bool IsActive,
    int SortOrder);

/// <summary>
/// API DTO與Schema契約的 <c>SpecificationDefinitionDto</c>。該表列有 <c>isFilterable</c>，但
/// 資料字典-商品庫存與組裝的 <c>SpecificationDefinitions</c> 欄位表沒有這個欄位、資料庫也沒有，
/// 新增欄位屬於 Schema 變更（Migration Gate）。這裡先輸出資料表真實存在的 <c>allowsMultiple</c>，
/// 並把這處文件與 Schema 的落差交組長裁定，不自行加欄位。
/// </summary>
public sealed record SpecificationDefinitionDto(
    Guid PublicId,
    Guid CategoryPublicId,
    string CategoryCode,
    string SemanticKey,
    string DisplayNameZhTw,
    string ValueType,
    string? UnitCode,
    bool IsRequired,
    bool AllowsMultiple,
    bool IsProtected,
    bool IsActive,
    int SortOrder,
    IReadOnlyList<SpecificationOptionDto> Options,
    byte[] RowVersion);

public sealed record SpecificationOptionInput(string Code, string DisplayNameZhTw, int SortOrder, bool IsActive);

public sealed record CreateSpecificationDefinitionRequest(
    Guid CategoryPublicId,
    string SemanticKey,
    string DisplayNameZhTw,
    string ValueType,
    string? UnitCode,
    bool IsRequired,
    bool AllowsMultiple,
    int SortOrder,
    IReadOnlyList<SpecificationOptionInput> Options);

/// <summary>
/// 結構欄位（Category、SemanticKey、ValueType、Unit、AllowsMultiple）不在更新請求裡——
/// 資料字典要求它們被使用後不可變，API Endpoint 目錄也寫明「Semantic Key／型別受保護」。
/// 要換型別的正確做法是新增定義並停用舊的。
/// </summary>
public sealed record UpdateSpecificationDefinitionRequest(
    string DisplayNameZhTw,
    bool IsRequired,
    int SortOrder,
    IReadOnlyList<SpecificationOptionInput> Options,
    byte[] RowVersion);

public sealed record DisableSpecificationDefinitionRequest(byte[] RowVersion);

public interface ISpecificationDefinitionAdminService
{
    Task<PageResult<SpecificationDefinitionDto>> ListAsync(
        SpecificationDefinitionQuery query,
        CancellationToken cancellationToken);

    Task<SpecificationDefinitionDto> CreateAsync(
        CreateSpecificationDefinitionRequest request,
        CancellationToken cancellationToken);

    Task<SpecificationDefinitionDto> UpdateAsync(
        Guid publicId,
        UpdateSpecificationDefinitionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// 以停用代替刪除。受保護（固定相容性引擎依賴）的定義不得停用，回
    /// <c>specification_definition_referenced</c>。
    /// </summary>
    Task<SpecificationDefinitionDto> DisableAsync(
        Guid publicId,
        DisableSpecificationDefinitionRequest request,
        CancellationToken cancellationToken);
}

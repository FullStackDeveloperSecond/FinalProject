using System.ComponentModel.DataAnnotations;
using DoSelect.Api.Common;
using DoSelect.Application.Catalog;
using DoSelect.Application.Common;
using DoSelect.Domain.Members;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Catalog;

[ApiController]
[Route("api/v1/products")]
public sealed class ProductsController : ControllerBase
{
    private readonly IProductSearchService _productSearchService;
    private readonly IProductDetailService _productDetailService;

    public ProductsController(
        IProductSearchService productSearchService,
        IProductDetailService productDetailService)
    {
        _productSearchService = productSearchService;
        _productDetailService = productDetailService;
    }

    [HttpGet]
    public async Task<ActionResult<PageResult<ProductCardDto>>> Search(
        [FromQuery] ProductSearchRequest request,
        CancellationToken cancellationToken)
    {
        var query = new ProductSearchQuery(
            request.Q,
            request.Category,
            request.Brand,
            request.MinPrice,
            request.MaxPrice,
            request.InStock,
            MapSpecs(request.Specs),
            request.Sort,
            request.PageNumber,
            request.PageSize,
            CatalogLocaleParser.Parse(request.Locale));

        try
        {
            var result = await _productSearchService.SearchAsync(query, cancellationToken);
            return Ok(result);
        }
        catch (CatalogSearchException exception)
        {
            var problem = ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                exception.ErrorCode,
                "Search request is invalid",
                exception.Message);
            return BadRequest(problem);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDetailDto>> GetById(
        Guid id,
        [FromQuery] string? locale,
        CancellationToken cancellationToken)
    {
        var detail = await _productDetailService.GetByPublicIdAsync(
            id,
            CatalogLocaleParser.Parse(locale),
            cancellationToken);

        if (detail is null)
        {
            var problem = ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.ResourceNotFound);
            return NotFound(problem);
        }

        return Ok(detail);
    }

    private static IReadOnlyList<SpecFilter> MapSpecs(IReadOnlyList<SpecFilterRequest>? specs)
    {
        if (specs is null || specs.Count == 0)
        {
            return [];
        }

        return specs
            .Select(spec => new SpecFilter(
                spec.SemanticKey,
                ProductSearchQueryValidator.ParseOperator(spec.Operator),
                spec.Value,
                spec.Values))
            .ToList();
    }

}

public sealed class ProductSearchRequest
{
    [StringLength(160)]
    public string? Q { get; init; }

    [StringLength(64)]
    public string? Category { get; init; }

    [StringLength(64)]
    public string? Brand { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? MinPrice { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? MaxPrice { get; init; }

    public bool? InStock { get; init; }

    [MaxLength(12)]
    public IReadOnlyList<SpecFilterRequest>? Specs { get; init; }

    /// <summary>
    /// Raw sort token. Deliberately left unvalidated by data annotations so an
    /// unsupported value maps to the contract's distinct
    /// <c>search_sort_unsupported</c> error instead of the generic
    /// <c>validation_failed</c> error.
    /// </summary>
    public string? Sort { get; init; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 50)]
    public int PageSize { get; init; } = 20;

    [AllowedValues("zh-TW", "ja-JP", "ko-KR", null)]
    public string? Locale { get; init; }
}

public sealed class SpecFilterRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string SemanticKey { get; init; } = string.Empty;

    /// <summary>
    /// Raw operator token. Deliberately left unvalidated by data annotations so an
    /// unsupported value maps to the contract's distinct
    /// <c>search_filter_unsupported</c> error instead of the generic
    /// <c>validation_failed</c> error.
    /// </summary>
    [Required]
    public string Operator { get; init; } = string.Empty;

    [StringLength(100)]
    public string? Value { get; init; }

    [MaxLength(10)]
    public IReadOnlyList<string>? Values { get; init; }
}

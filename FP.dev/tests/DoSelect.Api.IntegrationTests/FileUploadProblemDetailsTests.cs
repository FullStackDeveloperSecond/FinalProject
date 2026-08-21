using DoSelect.Api.Common;
using DoSelect.Application.Files;
using Microsoft.AspNetCore.Http;

namespace DoSelect.Api.IntegrationTests;

public sealed class FileUploadProblemDetailsTests
{
    [Theory]
    [InlineData(PrivateFileStoreStatus.SizeExceeded, 413, ApiErrorCodes.FileSizeExceeded)]
    [InlineData(PrivateFileStoreStatus.FormatInvalid, 415, ApiErrorCodes.FileFormatInvalid)]
    [InlineData(PrivateFileStoreStatus.MalwareDetected, 422, ApiErrorCodes.FileMalwareDetected)]
    [InlineData(PrivateFileStoreStatus.ScanUnavailable, 503, ApiErrorCodes.FileScanUnavailable)]
    public void Create_WhenUploadFails_UsesStableFileErrorContract(
        PrivateFileStoreStatus status,
        int expectedHttpStatus,
        string expectedCode)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/support-tickets/example/attachments";

        var result = FileUploadProblemDetailsFactory.Create(httpContext, status);

        Assert.Equal(expectedHttpStatus, result.Status);
        Assert.Equal(expectedCode, result.Extensions["code"]);
        Assert.Equal(httpContext.Request.Path, result.Instance);
        Assert.DoesNotContain("Defender", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_WhenFileWasStored_RejectsInvalidMapping()
    {
        var httpContext = new DefaultHttpContext();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            FileUploadProblemDetailsFactory.Create(
                httpContext,
                PrivateFileStoreStatus.Stored);
        });
    }

    [Theory]
    [InlineData(ProductImageStoreStatus.SizeExceeded, 413, ApiErrorCodes.FileSizeExceeded)]
    [InlineData(ProductImageStoreStatus.FormatInvalid, 415, ApiErrorCodes.FileFormatInvalid)]
    [InlineData(ProductImageStoreStatus.MalwareDetected, 422, ApiErrorCodes.FileMalwareDetected)]
    [InlineData(ProductImageStoreStatus.ScanUnavailable, 503, ApiErrorCodes.FileScanUnavailable)]
    [InlineData(ProductImageStoreStatus.ProcessingFailed, 422, ApiErrorCodes.ImageProcessingFailed)]
    public void Create_WhenProductImageUploadFails_ReturnsCataloguedProblemDetails(
        ProductImageStoreStatus status,
        int expectedStatus,
        string expectedCode)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "image-request-id";

        var problem = FileUploadProblemDetailsFactory.Create(httpContext, status);

        Assert.Equal(expectedStatus, problem.Status);
        Assert.Equal(expectedCode, problem.Extensions["code"]);
        Assert.Equal("image-request-id", problem.Extensions["traceId"]);
    }
}

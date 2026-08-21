using DoSelect.Application.Files;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Common;

public static class FileUploadProblemDetailsFactory
{
    public static ProblemDetails Create(
        HttpContext httpContext,
        PrivateFileStoreStatus status)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return status switch
        {
            PrivateFileStoreStatus.SizeExceeded => ApiProblemDetailsFactory.Create(
                httpContext,
                StatusCodes.Status413PayloadTooLarge,
                ApiErrorCodes.FileSizeExceeded,
                detail: "The uploaded file exceeds the allowed size."),
            PrivateFileStoreStatus.FormatInvalid => ApiProblemDetailsFactory.Create(
                httpContext,
                StatusCodes.Status415UnsupportedMediaType,
                ApiErrorCodes.FileFormatInvalid,
                detail: "The file extension, media type, or signature is not allowed."),
            PrivateFileStoreStatus.MalwareDetected => ApiProblemDetailsFactory.Create(
                httpContext,
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.FileMalwareDetected,
                detail: "The uploaded file did not pass the security scan."),
            PrivateFileStoreStatus.ScanUnavailable => ApiProblemDetailsFactory.Create(
                httpContext,
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.FileScanUnavailable,
                detail: "The file security scan is temporarily unavailable."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "A stored file does not have an upload error response."),
        };
    }

    public static ProblemDetails Create(
        HttpContext httpContext,
        ProductImageStoreStatus status)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return status switch
        {
            ProductImageStoreStatus.SizeExceeded => ApiProblemDetailsFactory.Create(
                httpContext,
                StatusCodes.Status413PayloadTooLarge,
                ApiErrorCodes.FileSizeExceeded,
                detail: "The uploaded image exceeds the allowed size."),
            ProductImageStoreStatus.FormatInvalid => ApiProblemDetailsFactory.Create(
                httpContext,
                StatusCodes.Status415UnsupportedMediaType,
                ApiErrorCodes.FileFormatInvalid,
                detail: "The image extension, media type, or signature is not allowed."),
            ProductImageStoreStatus.MalwareDetected => ApiProblemDetailsFactory.Create(
                httpContext,
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.FileMalwareDetected,
                detail: "The uploaded image did not pass the security scan."),
            ProductImageStoreStatus.ScanUnavailable => ApiProblemDetailsFactory.Create(
                httpContext,
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.FileScanUnavailable,
                detail: "The file security scan is temporarily unavailable."),
            ProductImageStoreStatus.ProcessingFailed => ApiProblemDetailsFactory.Create(
                httpContext,
                StatusCodes.Status422UnprocessableEntity,
                ApiErrorCodes.ImageProcessingFailed,
                detail: "The uploaded image could not be decoded or safely processed."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "A stored image does not have an upload error response."),
        };
    }
}

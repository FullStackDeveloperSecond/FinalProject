using DoSelect.Domain.Reviews;

namespace DoSelect.Domain.Tests;

public sealed class ProductReviewTests
{
    private static readonly DateTime CreatedAtUtc =
        new(2026, 8, 29, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Edit_Draft_PreservesDraftUntilExplicitSubmission()
    {
        var review = NewReview();

        review.Edit(5, "updated", "updated content", CreatedAtUtc.AddMinutes(1));

        Assert.Equal(ProductReviewStatus.Draft, review.Status);
        review.Submit(CreatedAtUtc.AddMinutes(2));
        Assert.Equal(ProductReviewStatus.PendingReview, review.Status);
    }

    [Fact]
    public void Edit_Approved_ReturnsToPendingReviewAndClearsDecision()
    {
        var review = NewReview();
        review.Submit(CreatedAtUtc.AddMinutes(1));
        review.Review("admin-user", true, null, CreatedAtUtc.AddMinutes(2));

        review.Edit(3, null, "new pending content", CreatedAtUtc.AddMinutes(3));

        Assert.Equal(ProductReviewStatus.PendingReview, review.Status);
        Assert.Null(review.ReviewedByAdminUserId);
        Assert.Null(review.ReviewedAtUtc);
    }

    [Fact]
    public void HideAndRestore_OnlyOperateAcrossApprovedAndHidden()
    {
        var review = NewReview();
        review.Submit(CreatedAtUtc.AddMinutes(1));
        review.Review("admin-user", true, null, CreatedAtUtc.AddMinutes(2));

        review.Hide("moderator-user", CreatedAtUtc.AddMinutes(3));
        Assert.Equal(ProductReviewStatus.Hidden, review.Status);

        review.Restore("moderator-user", CreatedAtUtc.AddMinutes(4));
        Assert.Equal(ProductReviewStatus.Approved, review.Status);
    }

    private static ProductReview NewReview() => new(
        Guid.CreateVersion7(),
        "member-user",
        orderItemId: 1,
        productId: 2,
        rating: 4,
        title: "title",
        content: "content",
        CreatedAtUtc);
}

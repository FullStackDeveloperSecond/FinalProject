using DoSelect.Domain.Common;

namespace DoSelect.Domain.Members;

public sealed class MemberProfile
{
    private MemberProfile()
    {
    }

    public MemberProfile(
        string userId,
        Guid publicId,
        string displayName,
        DateOnly? birthDate,
        DateTime createdAtUtc)
    {
        UserId = RequireText(userId, nameof(userId));
        PublicId = RequirePublicId(publicId);
        DisplayName = RequireText(displayName, nameof(displayName));
        BirthDate = birthDate;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = createdAtUtc;
    }

    public string UserId { get; private set; } = string.Empty;

    public Guid PublicId { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public DateOnly? BirthDate { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public void UpdateProfile(string displayName, DateOnly? birthDate, DateTime updatedAtUtc)
    {
        DisplayName = RequireText(displayName, nameof(displayName));
        BirthDate = birthDate;
        UpdatedAtUtc = RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
    }

    public void Anonymize(DateTime updatedAtUtc)
    {
        DisplayName = "匿名會員";
        BirthDate = null;
        UpdatedAtUtc = RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
    }

    private static Guid RequirePublicId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("PublicId is required.", nameof(value));
        }

        return value;
    }

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value is required.", parameterName);
        }

        return value.Trim();
    }

    private static DateTime RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", parameterName);
        }

        return value;
    }
}

public sealed class AdminProfile
{
    private AdminProfile()
    {
    }

    public AdminProfile(
        string userId,
        Guid publicId,
        string employeeCode,
        string displayName,
        DateTime createdAtUtc)
    {
        UserId = RequireText(userId, nameof(userId));
        PublicId = publicId != Guid.Empty
            ? publicId
            : throw new ArgumentException("PublicId is required.", nameof(publicId));
        EmployeeCode = RequireText(employeeCode, nameof(employeeCode));
        DisplayName = RequireText(displayName, nameof(displayName));
        IsActive = true;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = createdAtUtc;
    }

    public string UserId { get; private set; } = string.Empty;

    public Guid PublicId { get; private set; }

    public string EmployeeCode { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public void UpdateDisplayName(string displayName, DateTime updatedAtUtc)
    {
        DisplayName = RequireText(displayName, nameof(displayName));
        UpdatedAtUtc = RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
    }

    public void SetActive(bool isActive, DateTime updatedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
    }

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value is required.", parameterName);
        }

        return value.Trim();
    }

    private static DateTime RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", parameterName);
        }

        return value;
    }
}

public sealed class MemberAddress : MutablePublicEntity
{
    private MemberAddress()
    {
    }

    public MemberAddress(
        Guid publicId,
        string memberUserId,
        string label,
        string recipientName,
        string phone,
        string postalCode,
        string city,
        string district,
        string addressLine1,
        string? addressLine2,
        DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        MemberUserId = RequireText(memberUserId, nameof(memberUserId));
        ApplyAddress(
            label,
            recipientName,
            phone,
            postalCode,
            city,
            district,
            addressLine1,
            addressLine2);
    }

    public string MemberUserId { get; private set; } = string.Empty;

    public string Label { get; private set; } = string.Empty;

    public string RecipientName { get; private set; } = string.Empty;

    public string Phone { get; private set; } = string.Empty;

    public string PostalCode { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string District { get; private set; } = string.Empty;

    public string AddressLine1 { get; private set; } = string.Empty;

    public string? AddressLine2 { get; private set; }

    public bool IsDefault { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    public void Update(
        string label,
        string recipientName,
        string phone,
        string postalCode,
        string city,
        string district,
        string addressLine1,
        string? addressLine2,
        DateTime updatedAtUtc)
    {
        EnsureNotDeleted();
        ApplyAddress(
            label,
            recipientName,
            phone,
            postalCode,
            city,
            district,
            addressLine1,
            addressLine2);
        MarkUpdated(updatedAtUtc);
    }

    public void SetAsDefault(DateTime updatedAtUtc)
    {
        EnsureNotDeleted();
        IsDefault = true;
        MarkUpdated(updatedAtUtc);
    }

    public void ClearDefault(DateTime updatedAtUtc)
    {
        EnsureNotDeleted();
        IsDefault = false;
        MarkUpdated(updatedAtUtc);
    }

    public void Delete(DateTime deletedAtUtc)
    {
        EnsureNotDeleted();
        DeletedAtUtc = RequireUtc(deletedAtUtc, nameof(deletedAtUtc));
        IsDefault = false;
        MarkUpdated(deletedAtUtc);
    }

    private void ApplyAddress(
        string label,
        string recipientName,
        string phone,
        string postalCode,
        string city,
        string district,
        string addressLine1,
        string? addressLine2)
    {
        Label = RequireText(label, nameof(label));
        RecipientName = RequireText(recipientName, nameof(recipientName));
        Phone = RequireText(phone, nameof(phone));
        PostalCode = RequireText(postalCode, nameof(postalCode));
        City = RequireText(city, nameof(city));
        District = RequireText(district, nameof(district));
        AddressLine1 = RequireText(addressLine1, nameof(addressLine1));
        AddressLine2 = string.IsNullOrWhiteSpace(addressLine2) ? null : addressLine2.Trim();
    }

    private void EnsureNotDeleted()
    {
        if (DeletedAtUtc.HasValue)
        {
            throw new InvalidOperationException("A deleted address cannot be changed.");
        }
    }
}

public sealed class Favorite
{
    private Favorite()
    {
    }

    public Favorite(string memberUserId, long productId, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(memberUserId))
        {
            throw new ArgumentException("MemberUserId is required.", nameof(memberUserId));
        }

        if (productId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productId));
        }

        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", nameof(createdAtUtc));
        }

        MemberUserId = memberUserId;
        ProductId = productId;
        CreatedAtUtc = createdAtUtc;
    }

    public string MemberUserId { get; private set; } = string.Empty;

    public long ProductId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}

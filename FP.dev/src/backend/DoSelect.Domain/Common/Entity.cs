namespace DoSelect.Domain.Common;

public abstract class Entity
{
    protected Entity()
    {
    }

    protected Entity(DateTime createdAtUtc)
    {
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
    }

    public long Id { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    protected static DateTime RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The value must use UTC.", parameterName);
        }

        return value;
    }

    protected static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value is required.", parameterName);
        }

        return value.Trim();
    }
}

public abstract class PublicEntity : Entity
{
    protected PublicEntity()
    {
    }

    protected PublicEntity(Guid publicId, DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        if (publicId == Guid.Empty)
        {
            throw new ArgumentException("PublicId is required.", nameof(publicId));
        }

        PublicId = publicId;
    }

    public Guid PublicId { get; private set; }
}

public abstract class MutableEntity : Entity
{
    protected MutableEntity()
    {
    }

    protected MutableEntity(DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        UpdatedAtUtc = createdAtUtc;
    }

    public DateTime UpdatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    protected void MarkUpdated(DateTime updatedAtUtc)
    {
        UpdatedAtUtc = RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
    }
}

public abstract class MutablePublicEntity : PublicEntity
{
    protected MutablePublicEntity()
    {
    }

    protected MutablePublicEntity(Guid publicId, DateTime createdAtUtc)
        : base(publicId, createdAtUtc)
    {
        UpdatedAtUtc = createdAtUtc;
    }

    public DateTime UpdatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    protected void MarkUpdated(DateTime updatedAtUtc)
    {
        UpdatedAtUtc = RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
    }
}

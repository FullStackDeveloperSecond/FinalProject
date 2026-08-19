namespace DoSelect.Domain.Imports;

public enum ImportType
{
    Product,
    InventoryAdjustment,
}

public enum ImportBatchStatus
{
    Uploaded,
    Validating,
    Ready,
    Invalid,
    Committing,
    Committed,
    Failed,
    Expired,
}

public enum ImportDataset
{
    Products,
    Skus,
    Specifications,
    InventoryAdjustments,
}

public enum ImportRowAction
{
    Insert,
    Update,
    NoChange,
    Error,
}

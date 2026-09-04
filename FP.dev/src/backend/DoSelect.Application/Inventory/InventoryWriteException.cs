namespace DoSelect.Application.Inventory;

public sealed class InventoryWriteException : Exception
{
    public InventoryWriteException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }

    public static class ErrorCodes
    {
        public const string InsufficientStock = "inventory_insufficient";
        public const string ReservationNotActive = "inventory_reservation_not_active";
        public const string ReservationAlreadyProcessed = "inventory_reservation_already_processed";
        public const string ReconciliationCaseNotOpen = "inventory_reconciliation_case_not_open";
        /// <summary>
        /// 對帳 resolve 重算帳本後 Reserved &gt; OnHand：不是「重新整理再送」能修好的過期，而是帳本本身不一致，
        /// 所以不共用 <see cref="ConcurrencyConflict"/>（組長對帳裁定 G1）。案件維持 Open／Acknowledged 等人工調查。
        /// </summary>
        public const string ReconciliationLedgerInconsistent = "inventory_reconciliation_ledger_inconsistent";
        public const string ConcurrencyConflict = "concurrency_conflict";
        public const string ResourceNotFound = "resource_not_found";
        public const string ValidationFailed = "validation_failed";
    }
}

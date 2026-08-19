SET NOCOUNT ON;

IF DB_NAME() <> N'DoSelectDb'
    THROW 51000, 'Verification must run against DoSelectDb.', 1;

DECLARE @ApplicationTableCount int =
(
    SELECT COUNT(*)
    FROM sys.tables
    WHERE is_ms_shipped = 0
      AND name <> N'__EFMigrationsHistory'
);

IF @ApplicationTableCount <> 93
    THROW 51001, 'Expected 93 application and Identity tables.', 1;

DECLARE @ExplicitIndexCount int =
(
    SELECT COUNT(*)
    FROM sys.indexes AS indexes
    INNER JOIN sys.tables AS tables
        ON tables.object_id = indexes.object_id
    WHERE tables.is_ms_shipped = 0
      AND tables.name <> N'__EFMigrationsHistory'
      AND indexes.index_id > 0
      AND indexes.is_primary_key = 0
      AND indexes.is_unique_constraint = 0
);

IF @ExplicitIndexCount <> 315
    THROW 51002, 'Expected 315 explicit EF Core indexes.', 1;

IF OBJECT_ID(N'dbo.vw_CaseWorkbench', N'V') IS NULL
    THROW 51003, 'dbo.vw_CaseWorkbench was not created.', 1;

DECLARE @ExpectedColumns TABLE
(
    Ordinal int NOT NULL PRIMARY KEY,
    ColumnName sysname NOT NULL
);

INSERT INTO @ExpectedColumns (Ordinal, ColumnName)
VALUES
    (1, N'CasePublicId'),
    (2, N'CaseType'),
    (3, N'CaseNumber'),
    (4, N'Title'),
    (5, N'Status'),
    (6, N'Priority'),
    (7, N'RequesterDisplay'),
    (8, N'AssigneePublicId'),
    (9, N'CreatedAtUtc'),
    (10, N'LastActivityAtUtc'),
    (11, N'SlaDueAtUtc'),
    (12, N'IsOverdue');

IF EXISTS
(
    SELECT expected.Ordinal, expected.ColumnName
    FROM @ExpectedColumns AS expected
    FULL OUTER JOIN
    (
        SELECT column_id, name
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.vw_CaseWorkbench')
    ) AS actual
        ON actual.column_id = expected.Ordinal
       AND actual.name = expected.ColumnName
    WHERE expected.Ordinal IS NULL
       OR actual.column_id IS NULL
)
    THROW 51004, 'dbo.vw_CaseWorkbench does not match the fixed 12-column contract.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = N'20260819013357_InitialCreate'
      AND ProductVersion = N'10.0.10'
)
    THROW 51005, 'InitialCreate is missing from EF migration history.', 1;

DECLARE @WorkbenchRows bigint;
SELECT @WorkbenchRows = COUNT_BIG(*) FROM dbo.vw_CaseWorkbench;

SELECT
    DB_NAME() AS DatabaseName,
    @ApplicationTableCount AS ApplicationAndIdentityTables,
    @ExplicitIndexCount AS ExplicitIndexes,
    12 AS WorkbenchColumns,
    @WorkbenchRows AS WorkbenchRows,
    N'20260819013357_InitialCreate' AS AppliedMigration,
    N'PASS' AS VerificationResult;

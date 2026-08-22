BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822041051_AddIdempotencyAndCartMergeConflicts'
)
BEGIN
    CREATE TABLE [CartMergeConflicts] (
        [Id] bigint NOT NULL IDENTITY,
        [MemberCartId] bigint NOT NULL,
        [GuestCartId] bigint NOT NULL,
        [GuestItemPublicId] uniqueidentifier NOT NULL,
        [SkuPublicId] uniqueidentifier NOT NULL,
        [GuestQuantity] int NOT NULL,
        [MemberQuantity] int NOT NULL,
        [AcceptedQuantity] int NOT NULL,
        [Reason] varchar(64) NOT NULL,
        [ResolvedAtUtc] datetime2(3) NULL,
        [ResolutionCode] varchar(64) NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        [PublicId] uniqueidentifier NOT NULL,
        [UpdatedAtUtc] datetime2(3) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_CartMergeConflicts] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CartMergeConflicts_DifferentCarts] CHECK ([MemberCartId] <> [GuestCartId]),
        CONSTRAINT [CK_CartMergeConflicts_Quantities] CHECK ([GuestQuantity] >= 1 AND [GuestQuantity] <= 99 AND [MemberQuantity] >= 0 AND [MemberQuantity] <= 99 AND [AcceptedQuantity] >= 0 AND [AcceptedQuantity] <= 99),
        CONSTRAINT [CK_CartMergeConflicts_Resolution] CHECK (([ResolvedAtUtc] IS NULL AND [ResolutionCode] IS NULL) OR ([ResolvedAtUtc] IS NOT NULL AND [ResolutionCode] IS NOT NULL)),
        CONSTRAINT [FK_CartMergeConflicts_Carts_GuestCartId] FOREIGN KEY ([GuestCartId]) REFERENCES [Carts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CartMergeConflicts_Carts_MemberCartId] FOREIGN KEY ([MemberCartId]) REFERENCES [Carts] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822041051_AddIdempotencyAndCartMergeConflicts'
)
BEGIN
    CREATE TABLE [IdempotencyRecords] (
        [Id] bigint NOT NULL IDENTITY,
        [ActorScopeHash] binary(32) NOT NULL,
        [Operation] varchar(128) NOT NULL,
        [Key] varchar(128) NOT NULL,
        [RequestHash] binary(32) NOT NULL,
        [Status] varchar(16) NOT NULL,
        [ResponseStatusCode] int NULL,
        [ResponseHeadersJson] nvarchar(max) NULL,
        [ResponseSummary] nvarchar(max) NULL,
        [ExpiresAtUtc] datetime2(3) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        [UpdatedAtUtc] datetime2(3) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_IdempotencyRecords_CompletedResponse] CHECK (([Status] = 'Processing' AND [ResponseStatusCode] IS NULL AND [ResponseHeadersJson] IS NULL AND [ResponseSummary] IS NULL) OR ([Status] IN ('Succeeded', 'Failed') AND [ResponseStatusCode] IS NOT NULL AND [ResponseHeadersJson] IS NOT NULL AND [ResponseSummary] IS NOT NULL)),
        CONSTRAINT [CK_IdempotencyRecords_ResponseStatusCode] CHECK ([ResponseStatusCode] IS NULL OR ([ResponseStatusCode] >= 100 AND [ResponseStatusCode] <= 599))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822041051_AddIdempotencyAndCartMergeConflicts'
)
BEGIN
    CREATE INDEX [IX_CartMergeConflicts_GuestCartId] ON [CartMergeConflicts] ([GuestCartId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822041051_AddIdempotencyAndCartMergeConflicts'
)
BEGIN
    CREATE INDEX [IX_CartMergeConflicts_MemberCart_ResolvedAtUtc] ON [CartMergeConflicts] ([MemberCartId], [ResolvedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822041051_AddIdempotencyAndCartMergeConflicts'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_CartMergeConflicts_MemberCart_GuestItem_Unresolved] ON [CartMergeConflicts] ([MemberCartId], [GuestItemPublicId]) WHERE [ResolvedAtUtc] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822041051_AddIdempotencyAndCartMergeConflicts'
)
BEGIN
    CREATE UNIQUE INDEX [UX_CartMergeConflicts_PublicId] ON [CartMergeConflicts] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822041051_AddIdempotencyAndCartMergeConflicts'
)
BEGIN
    CREATE INDEX [IX_IdempotencyRecords_ExpiresAtUtc] ON [IdempotencyRecords] ([ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822041051_AddIdempotencyAndCartMergeConflicts'
)
BEGIN
    CREATE UNIQUE INDEX [UX_IdempotencyRecords_ActorScope_Operation_Key] ON [IdempotencyRecords] ([ActorScopeHash], [Operation], [Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822041051_AddIdempotencyAndCartMergeConflicts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260822041051_AddIdempotencyAndCartMergeConflicts', N'10.0.10');
END;

COMMIT;
GO

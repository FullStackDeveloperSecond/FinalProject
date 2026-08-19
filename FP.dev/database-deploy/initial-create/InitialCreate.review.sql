IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [AspNetRoles] (
    [Id] nvarchar(450) NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetUsers] (
    [Id] nvarchar(450) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [AccountType] varchar(16) NOT NULL,
    [AccountStatus] varchar(24) NOT NULL,
    [PreferredLocale] varchar(10) NOT NULL DEFAULT 'zh-TW',
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [AnonymizedAtUtc] datetime2(3) NULL,
    [RowVersion] rowversion NOT NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_AspNetUsers_AccountStatus] CHECK ([AccountStatus] IN ('PendingEmailVerification','Active','Suspended','Anonymized','Disabled')),
    CONSTRAINT [CK_AspNetUsers_AccountType] CHECK ([AccountType] IN ('Member','Admin')),
    CONSTRAINT [CK_AspNetUsers_PreferredLocale] CHECK ([PreferredLocale] IN ('zh-TW','ja-JP','ko-KR'))
);

CREATE TABLE [Brands] (
    [Id] bigint NOT NULL IDENTITY,
    [Code] nvarchar(64) NOT NULL,
    [NameZhTw] nvarchar(160) NOT NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [SortOrder] int NOT NULL DEFAULT 0,
    [Description] nvarchar(1000) NULL,
    [WebsiteUrl] nvarchar(2048) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Brands] PRIMARY KEY ([Id])
);

CREATE TABLE [Categories] (
    [Id] bigint NOT NULL IDENTITY,
    [Code] nvarchar(64) NOT NULL,
    [NameZhTw] nvarchar(160) NOT NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [SortOrder] int NOT NULL DEFAULT 0,
    [ParentCategoryId] bigint NULL,
    [Slug] nvarchar(120) NOT NULL,
    [Description] nvarchar(1000) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Categories] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Categories_NotSelfParent] CHECK ([ParentCategoryId] IS NULL OR [ParentCategoryId] <> [Id]),
    CONSTRAINT [FK_Categories_Categories_ParentCategoryId] FOREIGN KEY ([ParentCategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ConvenienceStores] (
    [Id] bigint NOT NULL IDENTITY,
    [ProviderCode] nvarchar(64) NOT NULL,
    [StoreCode] nvarchar(64) NOT NULL,
    [StoreName] nvarchar(160) NOT NULL,
    [Address] nvarchar(500) NOT NULL,
    [City] nvarchar(60) NOT NULL,
    [District] nvarchar(60) NOT NULL,
    [IsDemoData] bit NOT NULL DEFAULT CAST(0 AS bit),
    [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ConvenienceStores] PRIMARY KEY ([Id])
);

CREATE TABLE [Coupons] (
    [Id] bigint NOT NULL IDENTITY,
    [Code] nvarchar(64) NOT NULL,
    [NameZhTw] nvarchar(160) NOT NULL,
    [DiscountType] varchar(16) NOT NULL,
    [DiscountValue] decimal(18,2) NULL,
    [MinimumSpend] decimal(18,2) NULL,
    [MaximumDiscount] decimal(18,2) NULL,
    [StartsAtUtc] datetime2(3) NOT NULL,
    [EndsAtUtc] datetime2(3) NOT NULL,
    [TotalUsageLimit] int NULL,
    [PerMemberLimit] int NULL,
    [MemberOnly] bit NOT NULL DEFAULT CAST(0 AS bit),
    [ExcludeSaleItems] bit NOT NULL DEFAULT CAST(0 AS bit),
    [ScopeType] varchar(16) NOT NULL DEFAULT 'All',
    [Status] varchar(16) NOT NULL DEFAULT 'Draft',
    [RuleVersion] int NOT NULL DEFAULT 1,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Coupons] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Coupons_Amounts] CHECK (([DiscountValue] IS NULL OR [DiscountValue] >= 0) AND ([MinimumSpend] IS NULL OR [MinimumSpend] >= 0) AND ([MaximumDiscount] IS NULL OR [MaximumDiscount] >= 0)),
    CONSTRAINT [CK_Coupons_Percentage] CHECK ([DiscountType] <> 'Percentage' OR ([DiscountValue] >= 0 AND [DiscountValue] <= 1)),
    CONSTRAINT [CK_Coupons_Period] CHECK ([EndsAtUtc] > [StartsAtUtc]),
    CONSTRAINT [CK_Coupons_UsageLimits] CHECK (([TotalUsageLimit] IS NULL OR [TotalUsageLimit] > 0) AND ([PerMemberLimit] IS NULL OR [PerMemberLimit] > 0))
);

CREATE TABLE [MeasurementUnits] (
    [Id] bigint NOT NULL IDENTITY,
    [Code] nvarchar(64) NOT NULL,
    [NameZhTw] nvarchar(160) NOT NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [SortOrder] int NOT NULL DEFAULT 0,
    [Symbol] nvarchar(24) NOT NULL,
    [Dimension] varchar(32) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_MeasurementUnits] PRIMARY KEY ([Id])
);

CREATE TABLE [ShippingMethods] (
    [Id] bigint NOT NULL IDENTITY,
    [Code] nvarchar(64) NOT NULL,
    [NameZhTw] nvarchar(160) NOT NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [SortOrder] int NOT NULL DEFAULT 0,
    [Kind] varchar(24) NOT NULL,
    [BaseFee] decimal(18,2) NOT NULL,
    [FreeShippingThreshold] decimal(18,2) NULL,
    [AllowsCod] bit NOT NULL,
    [RequiresPrepayment] bit NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ShippingMethods] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ShippingMethods_CodCapability] CHECK (NOT ([AllowsCod] = 1 AND [RequiresPrepayment] = 1)),
    CONSTRAINT [CK_ShippingMethods_Fees] CHECK ([BaseFee] >= 0 AND ([FreeShippingThreshold] IS NULL OR [FreeShippingThreshold] >= 0))
);

CREATE TABLE [ShippingProviderProfiles] (
    [Id] bigint NOT NULL IDENTITY,
    [ProviderCode] nvarchar(64) NOT NULL,
    [Version] int NOT NULL,
    [Status] varchar(16) NOT NULL,
    [EffectiveFromUtc] datetime2(3) NULL,
    [EffectiveToUtc] datetime2(3) NULL,
    [ConfigurationJson] nvarchar(4000) NOT NULL,
    [SchemaVersion] int NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ShippingProviderProfiles] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ShippingProviderProfiles_Period] CHECK ([EffectiveFromUtc] IS NULL OR [EffectiveToUtc] IS NULL OR [EffectiveToUtc] > [EffectiveFromUtc]),
    CONSTRAINT [CK_ShippingProviderProfiles_SchemaVersion] CHECK ([SchemaVersion] > 0),
    CONSTRAINT [CK_ShippingProviderProfiles_Version] CHECK ([Version] > 0)
);

CREATE TABLE [Tags] (
    [Id] bigint NOT NULL IDENTITY,
    [Code] nvarchar(64) NOT NULL,
    [NameZhTw] nvarchar(160) NOT NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [SortOrder] int NOT NULL DEFAULT 0,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Tags] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AdminProfiles] (
    [UserId] nvarchar(450) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [EmployeeCode] nvarchar(64) NOT NULL,
    [DisplayName] nvarchar(100) NOT NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_AdminProfiles] PRIMARY KEY ([UserId]),
    CONSTRAINT [FK_AdminProfiles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserRoles] (
    [UserId] nvarchar(450) NOT NULL,
    [RoleId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserTokens] (
    [UserId] nvarchar(450) NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [BuildLists] (
    [Id] bigint NOT NULL IDENTITY,
    [OwnerUserId] nvarchar(450) NOT NULL,
    [Name] nvarchar(160) NOT NULL,
    [Status] varchar(16) NOT NULL,
    [LastCheckedAtUtc] datetime2(3) NULL,
    [CompatibilityStatus] varchar(24) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_BuildLists] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_BuildLists_AspNetUsers_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Carts] (
    [Id] bigint NOT NULL IDENTITY,
    [OwnerUserId] nvarchar(450) NULL,
    [GuestCartKeyHash] binary(32) NULL,
    [Status] varchar(16) NOT NULL,
    [ExpiresAtUtc] datetime2(3) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Carts] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Carts_ExactlyOneOwner] CHECK (([OwnerUserId] IS NOT NULL AND [GuestCartKeyHash] IS NULL) OR ([OwnerUserId] IS NULL AND [GuestCartKeyHash] IS NOT NULL)),
    CONSTRAINT [FK_Carts_AspNetUsers_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [CompatibilityRuleSettings] (
    [Id] bigint NOT NULL IDENTITY,
    [RuleCode] nvarchar(64) NOT NULL,
    [SettingCode] nvarchar(64) NOT NULL,
    [DecimalValue] decimal(18,4) NULL,
    [BooleanValue] bit NULL,
    [SettingsVersion] int NOT NULL,
    [Reason] nvarchar(500) NOT NULL,
    [ChangedByAdminUserId] nvarchar(450) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_CompatibilityRuleSettings] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_CompatibilityRuleSettings_ExactlyOneValue] CHECK (([DecimalValue] IS NOT NULL AND [BooleanValue] IS NULL) OR ([DecimalValue] IS NULL AND [BooleanValue] IS NOT NULL)),
    CONSTRAINT [CK_CompatibilityRuleSettings_SettingsVersion] CHECK ([SettingsVersion] > 0),
    CONSTRAINT [FK_CompatibilityRuleSettings_AspNetUsers_ChangedByAdminUserId] FOREIGN KEY ([ChangedByAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ImportBatches] (
    [Id] bigint NOT NULL IDENTITY,
    [ImportType] varchar(24) NOT NULL,
    [TemplateVersion] int NOT NULL,
    [Status] varchar(24) NOT NULL,
    [CreatedByAdminUserId] nvarchar(450) NOT NULL,
    [ExpiresAtUtc] datetime2(3) NOT NULL,
    [SourceFileHash1] binary(32) NULL,
    [SourceFileHash2] binary(32) NULL,
    [SourceFileHash3] binary(32) NULL,
    [SourceFileNameDisplay1] nvarchar(255) NULL,
    [SourceFileNameDisplay2] nvarchar(255) NULL,
    [SourceFileNameDisplay3] nvarchar(255) NULL,
    [RowCount] int NOT NULL DEFAULT 0,
    [NewCount] int NOT NULL DEFAULT 0,
    [UpdatedCount] int NOT NULL DEFAULT 0,
    [UnchangedCount] int NOT NULL DEFAULT 0,
    [ErrorCount] int NOT NULL DEFAULT 0,
    [NormalizedContentVersion] int NOT NULL DEFAULT 0,
    [ConfirmedAtUtc] datetime2(3) NULL,
    [ResultSummaryJson] nvarchar(4000) NULL,
    [CorrelationId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ImportBatches] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ImportBatches_Counts] CHECK ([NewCount] >= 0 AND [UpdatedCount] >= 0 AND [UnchangedCount] >= 0 AND [ErrorCount] >= 0 AND [NewCount] + [UpdatedCount] + [UnchangedCount] + [ErrorCount] = [RowCount]),
    CONSTRAINT [CK_ImportBatches_RowCount] CHECK ([RowCount] >= 0 AND [RowCount] <= 5000),
    CONSTRAINT [FK_ImportBatches_AspNetUsers_CreatedByAdminUserId] FOREIGN KEY ([CreatedByAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [MemberAddresses] (
    [Id] bigint NOT NULL IDENTITY,
    [MemberUserId] nvarchar(450) NOT NULL,
    [Label] nvarchar(50) NOT NULL,
    [RecipientName] nvarchar(100) NOT NULL,
    [Phone] nvarchar(32) NOT NULL,
    [PostalCode] nvarchar(16) NOT NULL,
    [City] nvarchar(50) NOT NULL,
    [District] nvarchar(50) NOT NULL,
    [AddressLine1] nvarchar(300) NOT NULL,
    [AddressLine2] nvarchar(300) NULL,
    [IsDefault] bit NOT NULL DEFAULT CAST(0 AS bit),
    [DeletedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_MemberAddresses] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_MemberAddresses_AspNetUsers_MemberUserId] FOREIGN KEY ([MemberUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [MemberProfiles] (
    [UserId] nvarchar(450) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [DisplayName] nvarchar(100) NOT NULL,
    [BirthDate] date NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_MemberProfiles] PRIMARY KEY ([UserId]),
    CONSTRAINT [FK_MemberProfiles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ReportCases] (
    [Id] bigint NOT NULL IDENTITY,
    [ReportNumber] nvarchar(32) NOT NULL,
    [ReporterUserId] nvarchar(450) NOT NULL,
    [TargetType] varchar(32) NOT NULL,
    [TargetPublicId] uniqueidentifier NOT NULL,
    [ReasonCode] varchar(64) NOT NULL,
    [Description] nvarchar(1000) NOT NULL,
    [Status] varchar(24) NOT NULL,
    [Priority] varchar(16) NOT NULL,
    [AssigneeAdminUserId] nvarchar(450) NULL,
    [ResolutionCode] varchar(64) NULL,
    [DecisionNote] nvarchar(1000) NULL,
    [ResolvedAtUtc] datetime2(3) NULL,
    [ClosedAtUtc] datetime2(3) NULL,
    [LastActivityAtUtc] datetime2(3) NOT NULL,
    [OpenCaseKeyHash] binary(32) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ReportCases] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ReportCases_AspNetUsers_AssigneeAdminUserId] FOREIGN KEY ([AssigneeAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReportCases_AspNetUsers_ReporterUserId] FOREIGN KEY ([ReporterUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SpecificationSources] (
    [Id] bigint NOT NULL IDENTITY,
    [SourceType] varchar(24) NOT NULL,
    [ProviderName] nvarchar(160) NOT NULL,
    [SourceUrl] nvarchar(2048) NOT NULL,
    [OriginalFieldName] nvarchar(160) NULL,
    [RetrievedAtUtc] datetime2(3) NOT NULL,
    [ReviewedAtUtc] datetime2(3) NOT NULL,
    [ReviewedByAdminUserId] nvarchar(450) NOT NULL,
    [Note] nvarchar(1000) NULL,
    [SourceVersion] nvarchar(64) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_SpecificationSources] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SpecificationSources_AspNetUsers_ReviewedByAdminUserId] FOREIGN KEY ([ReviewedByAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [BrandTranslations] (
    [Id] bigint NOT NULL IDENTITY,
    [BrandId] bigint NOT NULL,
    [Locale] varchar(10) NOT NULL,
    [Name] nvarchar(160) NOT NULL,
    [Description] nvarchar(1000) NULL,
    [TranslationStatus] varchar(16) NOT NULL,
    [ReviewedByAdminUserId] nvarchar(450) NULL,
    [ReviewedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_BrandTranslations] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_BrandTranslations_Locale] CHECK ([Locale] IN ('zh-TW','ja-JP','ko-KR')),
    CONSTRAINT [FK_BrandTranslations_AspNetUsers_ReviewedByAdminUserId] FOREIGN KEY ([ReviewedByAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_BrandTranslations_Brands_BrandId] FOREIGN KEY ([BrandId]) REFERENCES [Brands] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [CategoryTranslations] (
    [Id] bigint NOT NULL IDENTITY,
    [CategoryId] bigint NOT NULL,
    [Locale] varchar(10) NOT NULL,
    [Name] nvarchar(160) NOT NULL,
    [Description] nvarchar(1000) NULL,
    [TranslationStatus] varchar(16) NOT NULL,
    [ReviewedByAdminUserId] nvarchar(450) NULL,
    [ReviewedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_CategoryTranslations] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_CategoryTranslations_Locale] CHECK ([Locale] IN ('zh-TW','ja-JP','ko-KR')),
    CONSTRAINT [FK_CategoryTranslations_AspNetUsers_ReviewedByAdminUserId] FOREIGN KEY ([ReviewedByAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CategoryTranslations_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Products] (
    [Id] bigint NOT NULL IDENTITY,
    [ProductCode] nvarchar(64) NOT NULL,
    [BrandId] bigint NOT NULL,
    [CategoryId] bigint NOT NULL,
    [NameZhTw] nvarchar(160) NOT NULL,
    [DescriptionZhTw] nvarchar(4000) NULL,
    [WarrantyMonths] int NULL,
    [Status] varchar(24) NOT NULL,
    [IsFeatured] bit NOT NULL DEFAULT CAST(0 AS bit),
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Products_WarrantyMonths] CHECK ([WarrantyMonths] IS NULL OR ([WarrantyMonths] >= 0 AND [WarrantyMonths] <= 120)),
    CONSTRAINT [FK_Products_Brands_BrandId] FOREIGN KEY ([BrandId]) REFERENCES [Brands] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Products_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [CouponCategories] (
    [CouponId] bigint NOT NULL,
    [CategoryId] bigint NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    CONSTRAINT [PK_CouponCategories] PRIMARY KEY ([CouponId], [CategoryId]),
    CONSTRAINT [FK_CouponCategories_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CouponCategories_Coupons_CouponId] FOREIGN KEY ([CouponId]) REFERENCES [Coupons] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SpecificationDefinitions] (
    [Id] bigint NOT NULL IDENTITY,
    [CategoryId] bigint NOT NULL,
    [SemanticKey] nvarchar(64) NOT NULL,
    [DisplayNameZhTw] nvarchar(160) NOT NULL,
    [ValueType] varchar(16) NOT NULL,
    [MeasurementUnitId] bigint NULL,
    [IsRequired] bit NOT NULL,
    [IsProtected] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [SortOrder] int NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_SpecificationDefinitions] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SpecificationDefinitions_MeasurementUnit] CHECK ([MeasurementUnitId] IS NULL OR [ValueType] = 'Decimal'),
    CONSTRAINT [FK_SpecificationDefinitions_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SpecificationDefinitions_MeasurementUnits_MeasurementUnitId] FOREIGN KEY ([MeasurementUnitId]) REFERENCES [MeasurementUnits] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Orders] (
    [Id] bigint NOT NULL IDENTITY,
    [OrderNumber] nvarchar(32) NOT NULL,
    [MemberUserId] nvarchar(450) NULL,
    [GuestEmailNormalized] nvarchar(320) NULL,
    [OrderStatus] varchar(32) NOT NULL,
    [PaymentStatus] varchar(32) NOT NULL,
    [FulfillmentStatus] varchar(32) NOT NULL,
    [AssemblyStatus] varchar(32) NOT NULL,
    [OrderRefundStatus] varchar(32) NOT NULL,
    [MerchandiseSubtotal] decimal(18,2) NOT NULL,
    [ItemDiscountTotal] decimal(18,2) NOT NULL,
    [ShippingFee] decimal(18,2) NOT NULL,
    [AssemblyFee] decimal(18,2) NOT NULL,
    [GrandTotal] decimal(18,2) NOT NULL,
    [PaidAmount] decimal(18,2) NOT NULL,
    [RefundedAmount] decimal(18,2) NOT NULL,
    [Currency] char(3) NOT NULL,
    [RecipientName] nvarchar(100) NOT NULL,
    [RecipientPhone] nvarchar(32) NOT NULL,
    [RecipientEmail] nvarchar(320) NOT NULL,
    [PostalCode] nvarchar(16) NULL,
    [RecipientCity] nvarchar(50) NULL,
    [RecipientDistrict] nvarchar(50) NULL,
    [AddressLine1] nvarchar(300) NULL,
    [AddressLine2] nvarchar(300) NULL,
    [ShippingMethodCode] nvarchar(64) NOT NULL,
    [ShippingProviderProfileVersionId] bigint NOT NULL,
    [StoreCode] nvarchar(64) NULL,
    [StoreName] nvarchar(160) NULL,
    [StoreAddress] nvarchar(500) NULL,
    [ShippingConstraintPolicyVersion] int NOT NULL,
    [ReturnPolicyVersion] int NOT NULL,
    [CouponPolicyVersion] int NULL,
    [PaymentDueAtUtc] datetime2(3) NULL,
    [ConfirmedAtUtc] datetime2(3) NULL,
    [PaidAtUtc] datetime2(3) NULL,
    [ShippedAtUtc] datetime2(3) NULL,
    [DeliveredAtUtc] datetime2(3) NULL,
    [CompletedAtUtc] datetime2(3) NULL,
    [CancelledAtUtc] datetime2(3) NULL,
    [CheckoutIdempotencyKey] nvarchar(128) NOT NULL,
    [SourceCartPublicId] uniqueidentifier NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Orders] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Orders_Amounts_Nonnegative] CHECK ([MerchandiseSubtotal] >= 0 AND [ItemDiscountTotal] >= 0 AND [ShippingFee] >= 0 AND [AssemblyFee] >= 0 AND [GrandTotal] >= 0 AND [PaidAmount] >= 0 AND [RefundedAmount] >= 0),
    CONSTRAINT [CK_Orders_Currency] CHECK ([Currency] = 'TWD'),
    CONSTRAINT [CK_Orders_GrandTotal] CHECK ([GrandTotal] = [MerchandiseSubtotal] - [ItemDiscountTotal] + [ShippingFee] + [AssemblyFee]),
    CONSTRAINT [CK_Orders_Owner] CHECK ([MemberUserId] IS NOT NULL OR [GuestEmailNormalized] IS NOT NULL),
    CONSTRAINT [CK_Orders_PaidAmount] CHECK ([PaidAmount] <= [GrandTotal]),
    CONSTRAINT [CK_Orders_PolicyVersions] CHECK ([ShippingConstraintPolicyVersion] > 0 AND [ReturnPolicyVersion] > 0 AND ([CouponPolicyVersion] IS NULL OR [CouponPolicyVersion] > 0)),
    CONSTRAINT [CK_Orders_RefundedAmount] CHECK ([RefundedAmount] <= [PaidAmount]),
    CONSTRAINT [FK_Orders_AspNetUsers_MemberUserId] FOREIGN KEY ([MemberUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Orders_ShippingProviderProfiles_ShippingProviderProfileVersionId] FOREIGN KEY ([ShippingProviderProfileVersionId]) REFERENCES [ShippingProviderProfiles] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [PackageLimitVersions] (
    [Id] bigint NOT NULL IDENTITY,
    [ProviderProfileId] bigint NOT NULL,
    [Version] int NOT NULL,
    [MaxWeightKg] decimal(10,3) NOT NULL,
    [MaxLengthCm] decimal(10,2) NOT NULL,
    [MaxWidthCm] decimal(10,2) NOT NULL,
    [MaxHeightCm] decimal(10,2) NOT NULL,
    [MaxTotalCm] decimal(10,2) NOT NULL,
    [MaxDeclaredValue] decimal(18,2) NOT NULL,
    [EffectiveFromUtc] datetime2(3) NULL,
    [EffectiveToUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_PackageLimitVersions] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_PackageLimitVersions_Limits] CHECK ([MaxWeightKg] > 0 AND [MaxLengthCm] > 0 AND [MaxWidthCm] > 0 AND [MaxHeightCm] > 0 AND [MaxTotalCm] > 0 AND [MaxDeclaredValue] > 0),
    CONSTRAINT [CK_PackageLimitVersions_Period] CHECK ([EffectiveFromUtc] IS NULL OR [EffectiveToUtc] IS NULL OR [EffectiveToUtc] > [EffectiveFromUtc]),
    CONSTRAINT [CK_PackageLimitVersions_Version] CHECK ([Version] > 0),
    CONSTRAINT [FK_PackageLimitVersions_ShippingProviderProfiles_ProviderProfileId] FOREIGN KEY ([ProviderProfileId]) REFERENCES [ShippingProviderProfiles] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [BuildShareTokens] (
    [Id] bigint NOT NULL IDENTITY,
    [BuildListId] bigint NOT NULL,
    [TokenHash] binary(32) NOT NULL,
    [ExpiresAtUtc] datetime2(3) NULL,
    [RevokedAtUtc] datetime2(3) NULL,
    [LastAccessedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_BuildShareTokens] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_BuildShareTokens_BuildLists_BuildListId] FOREIGN KEY ([BuildListId]) REFERENCES [BuildLists] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [CompatibilityCheckRuns] (
    [Id] bigint NOT NULL IDENTITY,
    [BuildListId] bigint NULL,
    [RuleSetVersion] int NOT NULL,
    [SettingsVersion] int NOT NULL,
    [Overall] varchar(24) NOT NULL,
    [InputHash] binary(32) NOT NULL,
    [EvaluatedAtUtc] datetime2(3) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_CompatibilityCheckRuns] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_CompatibilityCheckRuns_Overall] CHECK ([Overall] IN ('Compatible','Warning','Blocked','InsufficientData')),
    CONSTRAINT [CK_CompatibilityCheckRuns_Versions] CHECK ([RuleSetVersion] > 0 AND [SettingsVersion] > 0),
    CONSTRAINT [FK_CompatibilityCheckRuns_BuildLists_BuildListId] FOREIGN KEY ([BuildListId]) REFERENCES [BuildLists] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ImportRows] (
    [Id] bigint NOT NULL IDENTITY,
    [ImportBatchId] bigint NOT NULL,
    [Dataset] varchar(32) NOT NULL,
    [SourceRowNumber] int NOT NULL,
    [ImportKey] nvarchar(64) NOT NULL,
    [Action] varchar(16) NOT NULL,
    [NormalizedPayloadJson] nvarchar(max) NOT NULL,
    [ErrorCodes] nvarchar(2000) NULL,
    [RowHash] binary(32) NOT NULL,
    [RawJson] nvarchar(max) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    CONSTRAINT [PK_ImportRows] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ImportRows_SourceRowNumber] CHECK ([SourceRowNumber] > 0),
    CONSTRAINT [FK_ImportRows_ImportBatches_ImportBatchId] FOREIGN KEY ([ImportBatchId]) REFERENCES [ImportBatches] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [ReportAssignmentHistories] (
    [Id] bigint NOT NULL IDENTITY,
    [ReportCaseId] bigint NOT NULL,
    [FromAdminUserId] nvarchar(450) NULL,
    [ToAdminUserId] nvarchar(450) NULL,
    [Action] varchar(24) NOT NULL,
    [Reason] nvarchar(500) NULL,
    [ActorUserId] nvarchar(450) NULL,
    [OccurredAtUtc] datetime2(3) NOT NULL,
    CONSTRAINT [PK_ReportAssignmentHistories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ReportAssignmentHistories_AspNetUsers_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReportAssignmentHistories_AspNetUsers_FromAdminUserId] FOREIGN KEY ([FromAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReportAssignmentHistories_AspNetUsers_ToAdminUserId] FOREIGN KEY ([ToAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReportAssignmentHistories_ReportCases_ReportCaseId] FOREIGN KEY ([ReportCaseId]) REFERENCES [ReportCases] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ReportAttachments] (
    [Id] bigint NOT NULL IDENTITY,
    [ReportCaseId] bigint NOT NULL,
    [UploadedByUserId] nvarchar(450) NOT NULL,
    [OriginalFileName] nvarchar(255) NOT NULL,
    [StorageKey] nvarchar(500) NOT NULL,
    [Extension] varchar(10) NOT NULL,
    [MimeType] varchar(100) NOT NULL,
    [FileSizeBytes] bigint NOT NULL,
    [Sha256] binary(32) NOT NULL,
    [ScanStatus] varchar(20) NOT NULL,
    [ScannedAtUtc] datetime2(3) NULL,
    [RetentionUntilUtc] datetime2(3) NULL,
    [LegalHold] bit NOT NULL DEFAULT CAST(0 AS bit),
    [DeletedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ReportAttachments] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ReportAttachments_FileSize] CHECK ([FileSizeBytes] >= 1 AND [FileSizeBytes] <= 10485760),
    CONSTRAINT [FK_ReportAttachments_AspNetUsers_UploadedByUserId] FOREIGN KEY ([UploadedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReportAttachments_ReportCases_ReportCaseId] FOREIGN KEY ([ReportCaseId]) REFERENCES [ReportCases] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ReportStatusHistories] (
    [Id] bigint NOT NULL IDENTITY,
    [ReportCaseId] bigint NOT NULL,
    [FromStatus] varchar(24) NULL,
    [ToStatus] varchar(24) NOT NULL,
    [ActionCode] varchar(64) NOT NULL,
    [Reason] nvarchar(1000) NULL,
    [ActorUserId] nvarchar(450) NULL,
    [OccurredAtUtc] datetime2(3) NOT NULL,
    CONSTRAINT [PK_ReportStatusHistories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ReportStatusHistories_AspNetUsers_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReportStatusHistories_ReportCases_ReportCaseId] FOREIGN KEY ([ReportCaseId]) REFERENCES [ReportCases] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [CouponExcludedProducts] (
    [CouponId] bigint NOT NULL,
    [ProductId] bigint NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    CONSTRAINT [PK_CouponExcludedProducts] PRIMARY KEY ([CouponId], [ProductId]),
    CONSTRAINT [FK_CouponExcludedProducts_Coupons_CouponId] FOREIGN KEY ([CouponId]) REFERENCES [Coupons] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CouponExcludedProducts_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [CouponProducts] (
    [CouponId] bigint NOT NULL,
    [ProductId] bigint NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    CONSTRAINT [PK_CouponProducts] PRIMARY KEY ([CouponId], [ProductId]),
    CONSTRAINT [FK_CouponProducts_Coupons_CouponId] FOREIGN KEY ([CouponId]) REFERENCES [Coupons] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CouponProducts_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Favorites] (
    [MemberUserId] nvarchar(450) NOT NULL,
    [ProductId] bigint NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    CONSTRAINT [PK_Favorites] PRIMARY KEY ([MemberUserId], [ProductId]),
    CONSTRAINT [FK_Favorites_AspNetUsers_MemberUserId] FOREIGN KEY ([MemberUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Favorites_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ProductTags] (
    [ProductId] bigint NOT NULL,
    [TagId] bigint NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    CONSTRAINT [PK_ProductTags] PRIMARY KEY ([ProductId], [TagId]),
    CONSTRAINT [FK_ProductTags_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductTags_Tags_TagId] FOREIGN KEY ([TagId]) REFERENCES [Tags] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ProductTranslations] (
    [Id] bigint NOT NULL IDENTITY,
    [ProductId] bigint NOT NULL,
    [Locale] varchar(10) NOT NULL,
    [Name] nvarchar(160) NOT NULL,
    [Description] nvarchar(4000) NULL,
    [TranslationStatus] varchar(16) NOT NULL,
    [ReviewedByAdminUserId] nvarchar(450) NULL,
    [ReviewedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ProductTranslations] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ProductTranslations_Locale] CHECK ([Locale] IN ('zh-TW','ja-JP','ko-KR')),
    CONSTRAINT [FK_ProductTranslations_AspNetUsers_ReviewedByAdminUserId] FOREIGN KEY ([ReviewedByAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductTranslations_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Skus] (
    [Id] bigint NOT NULL IDENTITY,
    [SkuCode] nvarchar(64) NOT NULL,
    [ProductId] bigint NOT NULL,
    [NameZhTw] nvarchar(160) NOT NULL,
    [ListPrice] decimal(18,2) NOT NULL,
    [UnitCost] decimal(18,2) NOT NULL,
    [WeightKg] decimal(10,3) NULL,
    [LengthCm] decimal(10,2) NULL,
    [WidthCm] decimal(10,2) NULL,
    [HeightCm] decimal(10,2) NULL,
    [Status] varchar(24) NOT NULL,
    [IsDefault] bit NOT NULL DEFAULT CAST(0 AS bit),
    [RequiresPrepayment] bit NOT NULL DEFAULT CAST(0 AS bit),
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Skus] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Skus_Dimensions] CHECK (([WeightKg] IS NULL OR [WeightKg] > 0) AND ([LengthCm] IS NULL OR [LengthCm] > 0) AND ([WidthCm] IS NULL OR [WidthCm] > 0) AND ([HeightCm] IS NULL OR [HeightCm] > 0)),
    CONSTRAINT [CK_Skus_Prices] CHECK ([ListPrice] >= 0 AND [UnitCost] >= 0),
    CONSTRAINT [FK_Skus_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SpecificationDefinitionTranslations] (
    [Id] bigint NOT NULL IDENTITY,
    [SpecificationDefinitionId] bigint NOT NULL,
    [Locale] varchar(10) NOT NULL,
    [DisplayName] nvarchar(160) NOT NULL,
    [HelpText] nvarchar(500) NULL,
    [TranslationStatus] varchar(16) NOT NULL,
    [ReviewedByAdminUserId] nvarchar(450) NULL,
    [ReviewedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_SpecificationDefinitionTranslations] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SpecificationDefinitionTranslations_Locale] CHECK ([Locale] IN ('zh-TW','ja-JP','ko-KR')),
    CONSTRAINT [FK_SpecificationDefinitionTranslations_AspNetUsers_ReviewedByAdminUserId] FOREIGN KEY ([ReviewedByAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SpecificationDefinitionTranslations_SpecificationDefinitions_SpecificationDefinitionId] FOREIGN KEY ([SpecificationDefinitionId]) REFERENCES [SpecificationDefinitions] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SpecificationOptions] (
    [Id] bigint NOT NULL IDENTITY,
    [SpecificationDefinitionId] bigint NOT NULL,
    [Code] nvarchar(64) NOT NULL,
    [DisplayNameZhTw] nvarchar(160) NOT NULL,
    [IsActive] bit NOT NULL,
    [SortOrder] int NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_SpecificationOptions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SpecificationOptions_SpecificationDefinitions_SpecificationDefinitionId] FOREIGN KEY ([SpecificationDefinitionId]) REFERENCES [SpecificationDefinitions] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [AssemblyJobs] (
    [Id] bigint NOT NULL IDENTITY,
    [OrderId] bigint NOT NULL,
    [AssemblyGroupKey] uniqueidentifier NOT NULL,
    [Status] varchar(24) NOT NULL,
    [StartedAtUtc] datetime2(3) NULL,
    [CompletedAtUtc] datetime2(3) NULL,
    [AssignedAdminUserId] nvarchar(450) NULL,
    [Note] nvarchar(1000) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_AssemblyJobs] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AssemblyJobs_AspNetUsers_AssignedAdminUserId] FOREIGN KEY ([AssignedAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AssemblyJobs_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [CouponRedemptions] (
    [Id] bigint NOT NULL IDENTITY,
    [CouponId] bigint NOT NULL,
    [OrderId] bigint NOT NULL,
    [MemberUserId] nvarchar(450) NULL,
    [GuestUsageKeyHash] binary(32) NULL,
    [Status] varchar(16) NOT NULL DEFAULT 'Reserved',
    [ReservedAtUtc] datetime2(3) NOT NULL,
    [ReleasedAtUtc] datetime2(3) NULL,
    [ConsumedAtUtc] datetime2(3) NULL,
    [ExpiresAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_CouponRedemptions] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_CouponRedemptions_Owner] CHECK (([MemberUserId] IS NOT NULL AND [GuestUsageKeyHash] IS NULL) OR ([MemberUserId] IS NULL AND [GuestUsageKeyHash] IS NOT NULL)),
    CONSTRAINT [FK_CouponRedemptions_AspNetUsers_MemberUserId] FOREIGN KEY ([MemberUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CouponRedemptions_Coupons_CouponId] FOREIGN KEY ([CouponId]) REFERENCES [Coupons] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CouponRedemptions_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [GuestOrderAccessRequests] (
    [Id] bigint NOT NULL IDENTITY,
    [OrderId] bigint NULL,
    [CodeHash] binary(32) NULL,
    [RequesterIpHash] binary(32) NOT NULL,
    [EmailKeyHash] binary(32) NOT NULL,
    [OrderLookupKeyHash] binary(32) NOT NULL,
    [ExpiresAtUtc] datetime2(3) NOT NULL,
    [AttemptCount] int NOT NULL DEFAULT 0,
    [SendCount] int NOT NULL DEFAULT 0,
    [LastSentAtUtc] datetime2(3) NULL,
    [LockedAtUtc] datetime2(3) NULL,
    [ConsumedAtUtc] datetime2(3) NULL,
    [RevokedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_GuestOrderAccessRequests] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_GuestOrderAccessRequests_AttemptCount] CHECK ([AttemptCount] >= 0 AND [AttemptCount] <= 5),
    CONSTRAINT [CK_GuestOrderAccessRequests_SendCount] CHECK ([SendCount] >= 0 AND [SendCount] <= 3),
    CONSTRAINT [FK_GuestOrderAccessRequests_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [OrderStatusHistories] (
    [Id] bigint NOT NULL IDENTITY,
    [OrderId] bigint NOT NULL,
    [StateDimension] varchar(32) NOT NULL,
    [FromStatus] varchar(32) NULL,
    [ToStatus] varchar(32) NOT NULL,
    [ReasonCode] varchar(64) NULL,
    [ActorUserId] nvarchar(450) NULL,
    [OccurredAtUtc] datetime2(3) NOT NULL,
    [TraceId] nvarchar(64) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_OrderStatusHistories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderStatusHistories_AspNetUsers_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderStatusHistories_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [PaymentAttempts] (
    [Id] bigint NOT NULL IDENTITY,
    [OrderId] bigint NOT NULL,
    [Method] varchar(24) NOT NULL,
    [Status] varchar(24) NOT NULL DEFAULT 'Pending',
    [Amount] decimal(18,2) NOT NULL,
    [ProviderCode] nvarchar(64) NULL,
    [ExternalReference] nvarchar(128) NULL,
    [InstructionExpiresAtUtc] datetime2(3) NULL,
    [PaidAtUtc] datetime2(3) NULL,
    [FailedAtUtc] datetime2(3) NULL,
    [FailureCode] nvarchar(64) NULL,
    [IdempotencyKey] nvarchar(128) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_PaymentAttempts] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_PaymentAttempts_Amount] CHECK ([Amount] > 0),
    CONSTRAINT [FK_PaymentAttempts_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ReturnRequests] (
    [Id] bigint NOT NULL IDENTITY,
    [ReturnNumber] nvarchar(32) NOT NULL,
    [OrderId] bigint NOT NULL,
    [RequesterUserId] nvarchar(450) NULL,
    [Status] varchar(24) NOT NULL,
    [Priority] varchar(16) NOT NULL DEFAULT 'Normal',
    [ReasonCode] varchar(64) NOT NULL,
    [Description] nvarchar(1000) NOT NULL,
    [AssigneeAdminUserId] nvarchar(450) NULL,
    [ReviewedByAdminUserId] nvarchar(450) NULL,
    [PolicyVersion] int NOT NULL,
    [RequestedAtUtc] datetime2(3) NULL,
    [ApprovedAtUtc] datetime2(3) NULL,
    [ReceivedAtUtc] datetime2(3) NULL,
    [ClosedAtUtc] datetime2(3) NULL,
    [ReturnShipmentDueAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ReturnRequests] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ReturnRequests_PolicyVersion] CHECK ([PolicyVersion] > 0),
    CONSTRAINT [FK_ReturnRequests_AspNetUsers_AssigneeAdminUserId] FOREIGN KEY ([AssigneeAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReturnRequests_AspNetUsers_RequesterUserId] FOREIGN KEY ([RequesterUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReturnRequests_AspNetUsers_ReviewedByAdminUserId] FOREIGN KEY ([ReviewedByAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReturnRequests_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Shipments] (
    [Id] bigint NOT NULL IDENTITY,
    [OrderId] bigint NOT NULL,
    [ShippingMethodId] bigint NOT NULL,
    [ProviderProfileVersionId] bigint NOT NULL,
    [ConvenienceStoreId] bigint NULL,
    [ShipmentNumber] nvarchar(64) NOT NULL,
    [Status] varchar(24) NOT NULL,
    [TrackingNumber] nvarchar(128) NULL,
    [FeeSnapshot] decimal(18,2) NOT NULL,
    [ShippedAtUtc] datetime2(3) NULL,
    [DeliveredAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Shipments] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Shipments_FeeSnapshot] CHECK ([FeeSnapshot] >= 0),
    CONSTRAINT [FK_Shipments_ConvenienceStores_ConvenienceStoreId] FOREIGN KEY ([ConvenienceStoreId]) REFERENCES [ConvenienceStores] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Shipments_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Shipments_ShippingMethods_ShippingMethodId] FOREIGN KEY ([ShippingMethodId]) REFERENCES [ShippingMethods] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Shipments_ShippingProviderProfiles_ProviderProfileVersionId] FOREIGN KEY ([ProviderProfileVersionId]) REFERENCES [ShippingProviderProfiles] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SimulatedInvoices] (
    [Id] bigint NOT NULL IDENTITY,
    [OrderId] bigint NOT NULL,
    [InvoiceNumber] nvarchar(32) NOT NULL,
    [BuyerType] varchar(20) NOT NULL,
    [BuyerEmail] nvarchar(320) NULL,
    [CarrierType] varchar(30) NULL,
    [CarrierValueMasked] nvarchar(100) NULL,
    [CompanyTaxId] varchar(20) NULL,
    [CompanyName] nvarchar(200) NULL,
    [NetAmount] decimal(18,2) NOT NULL,
    [TaxAmount] decimal(18,2) NOT NULL,
    [IssuedAmount] decimal(18,2) NOT NULL,
    [Currency] char(3) NOT NULL DEFAULT 'TWD',
    [Status] varchar(16) NOT NULL DEFAULT 'Pending',
    [IssuedAtUtc] datetime2(3) NULL,
    [VoidedAtUtc] datetime2(3) NULL,
    [DemoMarker] nvarchar(32) NOT NULL DEFAULT N'DEMO-NOT-A-TAX-INVOICE',
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_SimulatedInvoices] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SimulatedInvoices_Amounts] CHECK ([NetAmount] >= 0 AND [TaxAmount] >= 0 AND [IssuedAmount] = [NetAmount] + [TaxAmount]),
    CONSTRAINT [CK_SimulatedInvoices_CompanyBuyer] CHECK ([BuyerType] <> 'Company' OR ([CompanyTaxId] IS NOT NULL AND [CompanyName] IS NOT NULL)),
    CONSTRAINT [CK_SimulatedInvoices_Currency] CHECK ([Currency] = 'TWD'),
    CONSTRAINT [CK_SimulatedInvoices_DemoMarker] CHECK ([DemoMarker] = 'DEMO-NOT-A-TAX-INVOICE'),
    CONSTRAINT [FK_SimulatedInvoices_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SupportTickets] (
    [Id] bigint NOT NULL IDENTITY,
    [TicketNumber] nvarchar(32) NOT NULL,
    [MemberUserId] nvarchar(450) NOT NULL,
    [OrderId] bigint NULL,
    [Category] varchar(32) NOT NULL,
    [Subject] nvarchar(200) NOT NULL,
    [Status] varchar(32) NOT NULL,
    [Priority] varchar(16) NOT NULL,
    [AssigneeAdminUserId] nvarchar(450) NULL,
    [FirstResponseDueAtUtc] datetime2(3) NOT NULL,
    [ResolutionDueAtUtc] datetime2(3) NOT NULL,
    [FirstHumanResponseAtUtc] datetime2(3) NULL,
    [WaitingForCustomerStartedAtUtc] datetime2(3) NULL,
    [PausedSeconds] int NOT NULL DEFAULT 0,
    [ResolvedAtUtc] datetime2(3) NULL,
    [ClosedAtUtc] datetime2(3) NULL,
    [LastActivityAtUtc] datetime2(3) NOT NULL,
    [ReopenCount] int NOT NULL DEFAULT 0,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_SupportTickets] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SupportTickets_PausedSeconds] CHECK ([PausedSeconds] >= 0 AND [PausedSeconds] <= 259200),
    CONSTRAINT [CK_SupportTickets_ReopenCount] CHECK ([ReopenCount] >= 0),
    CONSTRAINT [FK_SupportTickets_AspNetUsers_AssigneeAdminUserId] FOREIGN KEY ([AssigneeAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SupportTickets_AspNetUsers_MemberUserId] FOREIGN KEY ([MemberUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SupportTickets_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [CompatibilityCheckResults] (
    [Id] bigint NOT NULL IDENTITY,
    [CompatibilityCheckRunId] bigint NOT NULL,
    [RuleCode] nvarchar(64) NOT NULL,
    [Severity] varchar(24) NOT NULL,
    [MessageKey] nvarchar(160) NOT NULL,
    [FactsJson] nvarchar(4000) NULL,
    CONSTRAINT [PK_CompatibilityCheckResults] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CompatibilityCheckResults_CompatibilityCheckRuns_CompatibilityCheckRunId] FOREIGN KEY ([CompatibilityCheckRunId]) REFERENCES [CompatibilityCheckRuns] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [BuildListItems] (
    [Id] bigint NOT NULL IDENTITY,
    [BuildListId] bigint NOT NULL,
    [SkuId] bigint NOT NULL,
    [Quantity] int NOT NULL,
    [SortOrder] int NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_BuildListItems] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_BuildListItems_Quantity] CHECK ([Quantity] >= 1 AND [Quantity] <= 8),
    CONSTRAINT [FK_BuildListItems_BuildLists_BuildListId] FOREIGN KEY ([BuildListId]) REFERENCES [BuildLists] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_BuildListItems_Skus_SkuId] FOREIGN KEY ([SkuId]) REFERENCES [Skus] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [CartItems] (
    [Id] bigint NOT NULL IDENTITY,
    [CartId] bigint NOT NULL,
    [SkuId] bigint NOT NULL,
    [Quantity] int NOT NULL,
    [AssemblyGroupKey] uniqueidentifier NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_CartItems] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_CartItems_Quantity] CHECK ([Quantity] >= 1 AND [Quantity] <= 99),
    CONSTRAINT [FK_CartItems_Carts_CartId] FOREIGN KEY ([CartId]) REFERENCES [Carts] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_CartItems_Skus_SkuId] FOREIGN KEY ([SkuId]) REFERENCES [Skus] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [InventoryBalances] (
    [Id] bigint NOT NULL IDENTITY,
    [SkuId] bigint NOT NULL,
    [OnHandQuantity] int NOT NULL DEFAULT 0,
    [ReservedQuantity] int NOT NULL DEFAULT 0,
    [AvailableQuantity] AS [OnHandQuantity] - [ReservedQuantity] PERSISTED,
    [ReorderLevel] int NOT NULL DEFAULT 0,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_InventoryBalances] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_InventoryBalances_OnHand] CHECK ([OnHandQuantity] >= 0),
    CONSTRAINT [CK_InventoryBalances_ReorderLevel] CHECK ([ReorderLevel] >= 0),
    CONSTRAINT [CK_InventoryBalances_Reserved] CHECK ([ReservedQuantity] >= 0 AND [ReservedQuantity] <= [OnHandQuantity]),
    CONSTRAINT [FK_InventoryBalances_Skus_SkuId] FOREIGN KEY ([SkuId]) REFERENCES [Skus] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [InventoryReservations] (
    [Id] bigint NOT NULL IDENTITY,
    [SkuId] bigint NOT NULL,
    [OrderId] bigint NOT NULL,
    [Quantity] int NOT NULL,
    [Status] varchar(16) NOT NULL,
    [ExpiresAtUtc] datetime2(3) NULL,
    [ReleasedAtUtc] datetime2(3) NULL,
    [ReleaseReason] varchar(32) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_InventoryReservations] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_InventoryReservations_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [FK_InventoryReservations_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InventoryReservations_Skus_SkuId] FOREIGN KEY ([SkuId]) REFERENCES [Skus] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [OrderItems] (
    [Id] bigint NOT NULL IDENTITY,
    [OrderId] bigint NOT NULL,
    [SkuId] bigint NULL,
    [SkuCodeSnapshot] nvarchar(64) NOT NULL,
    [ProductNameSnapshot] nvarchar(160) NOT NULL,
    [SkuNameSnapshot] nvarchar(160) NOT NULL,
    [Quantity] int NOT NULL,
    [ListUnitPrice] decimal(18,2) NOT NULL,
    [SaleUnitPrice] decimal(18,2) NOT NULL,
    [FinalUnitPrice] decimal(18,2) NOT NULL,
    [UnitCostSnapshot] decimal(18,2) NOT NULL,
    [LineSubtotal] decimal(18,2) NOT NULL,
    [DiscountAllocation] decimal(18,2) NOT NULL,
    [LineTotal] decimal(18,2) NOT NULL,
    [AssemblyGroupKey] uniqueidentifier NULL,
    [ReturnableQuantity] int NOT NULL,
    [ReturnedQuantity] int NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_OrderItems] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_OrderItems_Amounts_Nonnegative] CHECK ([ListUnitPrice] >= 0 AND [SaleUnitPrice] >= 0 AND [FinalUnitPrice] >= 0 AND [UnitCostSnapshot] >= 0 AND [LineSubtotal] >= 0 AND [DiscountAllocation] >= 0 AND [LineTotal] >= 0),
    CONSTRAINT [CK_OrderItems_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_OrderItems_ReturnedQuantity] CHECK ([ReturnedQuantity] >= 0 AND [ReturnableQuantity] >= [ReturnedQuantity] AND [Quantity] >= [ReturnableQuantity]),
    CONSTRAINT [FK_OrderItems_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderItems_Skus_SkuId] FOREIGN KEY ([SkuId]) REFERENCES [Skus] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ProductImages] (
    [Id] bigint NOT NULL IDENTITY,
    [ProductId] bigint NOT NULL,
    [SkuId] bigint NULL,
    [StorageKey] nvarchar(500) NOT NULL,
    [OriginalFileName] nvarchar(255) NOT NULL,
    [MediaType] varchar(100) NOT NULL,
    [FileSizeBytes] bigint NOT NULL,
    [Width] int NOT NULL,
    [Height] int NOT NULL,
    [Sha256] binary(32) NOT NULL,
    [AltTextZhTw] nvarchar(160) NOT NULL,
    [SourceUrl] nvarchar(2048) NULL,
    [LicenseUrl] nvarchar(2048) NULL,
    [AuthorName] nvarchar(160) NULL,
    [LicenseName] nvarchar(160) NULL,
    [DownloadedAtUtc] datetime2(3) NULL,
    [Status] varchar(24) NOT NULL,
    [SortOrder] int NOT NULL DEFAULT 0,
    [PublishedAtUtc] datetime2(3) NULL,
    [DeletedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ProductImages] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ProductImages_Dimensions] CHECK ([Width] > 0 AND [Height] > 0),
    CONSTRAINT [CK_ProductImages_FileSize] CHECK ([FileSizeBytes] >= 1 AND [FileSizeBytes] <= 10485760),
    CONSTRAINT [FK_ProductImages_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductImages_Skus_SkuId] FOREIGN KEY ([SkuId]) REFERENCES [Skus] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SalePrices] (
    [Id] bigint NOT NULL IDENTITY,
    [SkuId] bigint NOT NULL,
    [Price] decimal(18,2) NOT NULL,
    [StartsAtUtc] datetime2(3) NOT NULL,
    [EndsAtUtc] datetime2(3) NOT NULL,
    [Status] varchar(16) NOT NULL,
    [CreatedByAdminUserId] nvarchar(450) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_SalePrices] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SalePrices_Period] CHECK ([EndsAtUtc] > [StartsAtUtc]),
    CONSTRAINT [CK_SalePrices_Price] CHECK ([Price] >= 0),
    CONSTRAINT [FK_SalePrices_AspNetUsers_CreatedByAdminUserId] FOREIGN KEY ([CreatedByAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SalePrices_Skus_SkuId] FOREIGN KEY ([SkuId]) REFERENCES [Skus] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SkuTranslations] (
    [Id] bigint NOT NULL IDENTITY,
    [SkuId] bigint NOT NULL,
    [Locale] varchar(10) NOT NULL,
    [Name] nvarchar(160) NOT NULL,
    [TranslationStatus] varchar(16) NOT NULL,
    [ReviewedByAdminUserId] nvarchar(450) NULL,
    [ReviewedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_SkuTranslations] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SkuTranslations_Locale] CHECK ([Locale] IN ('zh-TW','ja-JP','ko-KR')),
    CONSTRAINT [FK_SkuTranslations_AspNetUsers_ReviewedByAdminUserId] FOREIGN KEY ([ReviewedByAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SkuTranslations_Skus_SkuId] FOREIGN KEY ([SkuId]) REFERENCES [Skus] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SkuSpecificationValues] (
    [Id] bigint NOT NULL IDENTITY,
    [SkuId] bigint NOT NULL,
    [SpecificationDefinitionId] bigint NOT NULL,
    [StringValue] nvarchar(500) NULL,
    [DecimalValue] decimal(18,4) NULL,
    [BooleanValue] bit NULL,
    [OptionId] bigint NULL,
    [SpecificationSourceId] bigint NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_SkuSpecificationValues] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SkuSpecificationValues_ExactlyOneValue] CHECK ((CASE WHEN [StringValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1),
    CONSTRAINT [FK_SkuSpecificationValues_Skus_SkuId] FOREIGN KEY ([SkuId]) REFERENCES [Skus] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SkuSpecificationValues_SpecificationDefinitions_SpecificationDefinitionId] FOREIGN KEY ([SpecificationDefinitionId]) REFERENCES [SpecificationDefinitions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SkuSpecificationValues_SpecificationOptions_OptionId] FOREIGN KEY ([OptionId]) REFERENCES [SpecificationOptions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SkuSpecificationValues_SpecificationSources_SpecificationSourceId] FOREIGN KEY ([SpecificationSourceId]) REFERENCES [SpecificationSources] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SpecificationOptionTranslations] (
    [Id] bigint NOT NULL IDENTITY,
    [SpecificationOptionId] bigint NOT NULL,
    [Locale] varchar(10) NOT NULL,
    [DisplayName] nvarchar(160) NOT NULL,
    [TranslationStatus] varchar(16) NOT NULL,
    [ReviewedByAdminUserId] nvarchar(450) NULL,
    [ReviewedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_SpecificationOptionTranslations] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SpecificationOptionTranslations_Locale] CHECK ([Locale] IN ('zh-TW','ja-JP','ko-KR')),
    CONSTRAINT [FK_SpecificationOptionTranslations_AspNetUsers_ReviewedByAdminUserId] FOREIGN KEY ([ReviewedByAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SpecificationOptionTranslations_SpecificationOptions_SpecificationOptionId] FOREIGN KEY ([SpecificationOptionId]) REFERENCES [SpecificationOptions] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [AssemblyJobStatusHistories] (
    [Id] bigint NOT NULL IDENTITY,
    [AssemblyJobId] bigint NOT NULL,
    [FromStatus] varchar(24) NULL,
    [ToStatus] varchar(24) NOT NULL,
    [ReasonCode] varchar(64) NULL,
    [ActorUserId] nvarchar(450) NULL,
    [OccurredAtUtc] datetime2(3) NOT NULL,
    [TraceId] nvarchar(64) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_AssemblyJobStatusHistories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AssemblyJobStatusHistories_AspNetUsers_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AssemblyJobStatusHistories_AssemblyJobs_AssemblyJobId] FOREIGN KEY ([AssemblyJobId]) REFERENCES [AssemblyJobs] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [OrderCoupons] (
    [Id] bigint NOT NULL IDENTITY,
    [OrderId] bigint NOT NULL,
    [CouponId] bigint NULL,
    [RedemptionId] bigint NULL,
    [CouponCodeSnapshot] nvarchar(64) NOT NULL,
    [NameSnapshot] nvarchar(160) NOT NULL,
    [DiscountType] varchar(16) NOT NULL,
    [RuleVersion] int NOT NULL,
    [DiscountValue] decimal(18,2) NULL,
    [AppliedAmount] decimal(18,2) NOT NULL,
    [EligibleSubtotal] decimal(18,2) NOT NULL,
    [IsFreeShipping] bit NOT NULL DEFAULT CAST(0 AS bit),
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_OrderCoupons] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_OrderCoupons_Amounts] CHECK (([DiscountValue] IS NULL OR [DiscountValue] >= 0) AND [AppliedAmount] >= 0 AND [EligibleSubtotal] >= 0 AND [RuleVersion] > 0),
    CONSTRAINT [FK_OrderCoupons_CouponRedemptions_RedemptionId] FOREIGN KEY ([RedemptionId]) REFERENCES [CouponRedemptions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderCoupons_Coupons_CouponId] FOREIGN KEY ([CouponId]) REFERENCES [Coupons] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderCoupons_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [GuestOrderAccessTokens] (
    [Id] bigint NOT NULL IDENTITY,
    [OrderId] bigint NOT NULL,
    [RequestId] bigint NOT NULL,
    [TokenHash] binary(32) NOT NULL,
    [ExpiresAtUtc] datetime2(3) NOT NULL,
    [RevokedAtUtc] datetime2(3) NULL,
    [ScopeViolationCount] int NOT NULL DEFAULT 0,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_GuestOrderAccessTokens] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_GuestOrderAccessTokens_ScopeViolationCount] CHECK ([ScopeViolationCount] >= 0),
    CONSTRAINT [FK_GuestOrderAccessTokens_GuestOrderAccessRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [GuestOrderAccessRequests] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_GuestOrderAccessTokens_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [PaymentEvents] (
    [Id] bigint NOT NULL IDENTITY,
    [PaymentAttemptId] bigint NOT NULL,
    [ExternalEventId] nvarchar(128) NOT NULL,
    [EventType] nvarchar(64) NOT NULL,
    [OccurredAt] datetimeoffset(3) NOT NULL,
    [ReceivedAtUtc] datetime2(3) NOT NULL,
    [PayloadHash] binary(32) NOT NULL,
    [PayloadSummaryJson] nvarchar(4000) NULL,
    [ProcessingStatus] varchar(24) NOT NULL DEFAULT 'Received',
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_PaymentEvents] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PaymentEvents_PaymentAttempts_PaymentAttemptId] FOREIGN KEY ([PaymentAttemptId]) REFERENCES [PaymentAttempts] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Refunds] (
    [Id] bigint NOT NULL IDENTITY,
    [OrderId] bigint NOT NULL,
    [ReturnRequestId] bigint NULL,
    [PaymentAttemptId] bigint NOT NULL,
    [RefundNumber] nvarchar(32) NOT NULL,
    [Status] varchar(24) NOT NULL DEFAULT 'PendingReview',
    [RequestedAmount] decimal(18,2) NOT NULL,
    [ApprovedAmount] decimal(18,2) NULL,
    [SucceededAmount] decimal(18,2) NULL,
    [ReasonCode] varchar(64) NOT NULL,
    [RequestedBy] nvarchar(450) NULL,
    [ApprovedBy] nvarchar(450) NULL,
    [ExecutedByAdminUserId] nvarchar(450) NULL,
    [IdempotencyKey] nvarchar(128) NOT NULL,
    [SucceededAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Refunds] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Refunds_Amounts] CHECK ([RequestedAmount] > 0 AND ([ApprovedAmount] IS NULL OR ([ApprovedAmount] > 0 AND [ApprovedAmount] <= [RequestedAmount])) AND ([SucceededAmount] IS NULL OR ([SucceededAmount] > 0 AND [SucceededAmount] <= [ApprovedAmount]))),
    CONSTRAINT [FK_Refunds_AspNetUsers_ApprovedBy] FOREIGN KEY ([ApprovedBy]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Refunds_AspNetUsers_ExecutedByAdminUserId] FOREIGN KEY ([ExecutedByAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Refunds_AspNetUsers_RequestedBy] FOREIGN KEY ([RequestedBy]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Refunds_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Refunds_PaymentAttempts_PaymentAttemptId] FOREIGN KEY ([PaymentAttemptId]) REFERENCES [PaymentAttempts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Refunds_ReturnRequests_ReturnRequestId] FOREIGN KEY ([ReturnRequestId]) REFERENCES [ReturnRequests] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ReturnAssignmentHistories] (
    [Id] bigint NOT NULL IDENTITY,
    [ReturnRequestId] bigint NOT NULL,
    [FromAdminUserId] nvarchar(450) NULL,
    [ToAdminUserId] nvarchar(450) NULL,
    [Action] varchar(24) NOT NULL,
    [Reason] nvarchar(500) NULL,
    [ActorUserId] nvarchar(450) NULL,
    [OccurredAtUtc] datetime2(3) NOT NULL,
    CONSTRAINT [PK_ReturnAssignmentHistories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ReturnAssignmentHistories_AspNetUsers_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReturnAssignmentHistories_AspNetUsers_FromAdminUserId] FOREIGN KEY ([FromAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReturnAssignmentHistories_AspNetUsers_ToAdminUserId] FOREIGN KEY ([ToAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReturnAssignmentHistories_ReturnRequests_ReturnRequestId] FOREIGN KEY ([ReturnRequestId]) REFERENCES [ReturnRequests] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ReturnAttachments] (
    [Id] bigint NOT NULL IDENTITY,
    [ReturnRequestId] bigint NOT NULL,
    [UploadedByUserId] nvarchar(450) NOT NULL,
    [OriginalFileName] nvarchar(255) NOT NULL,
    [StorageKey] nvarchar(500) NOT NULL,
    [Extension] varchar(10) NOT NULL,
    [MimeType] varchar(100) NOT NULL,
    [FileSizeBytes] bigint NOT NULL,
    [Sha256] binary(32) NOT NULL,
    [ScanStatus] varchar(20) NOT NULL,
    [ScannedAtUtc] datetime2(3) NULL,
    [RetentionUntilUtc] datetime2(3) NULL,
    [LegalHold] bit NOT NULL DEFAULT CAST(0 AS bit),
    [DeletedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ReturnAttachments] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ReturnAttachments_FileSize] CHECK ([FileSizeBytes] >= 1 AND [FileSizeBytes] <= 10485760),
    CONSTRAINT [FK_ReturnAttachments_AspNetUsers_UploadedByUserId] FOREIGN KEY ([UploadedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReturnAttachments_ReturnRequests_ReturnRequestId] FOREIGN KEY ([ReturnRequestId]) REFERENCES [ReturnRequests] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ReturnShipments] (
    [Id] bigint NOT NULL IDENTITY,
    [ReturnRequestId] bigint NOT NULL,
    [ShipmentNumber] nvarchar(32) NOT NULL,
    [Method] varchar(24) NOT NULL,
    [CarrierCode] varchar(32) NULL,
    [TrackingNumber] nvarchar(64) NULL,
    [Status] varchar(24) NOT NULL,
    [RecipientName] nvarchar(160) NULL,
    [RecipientPhone] nvarchar(32) NULL,
    [PostalCode] nvarchar(16) NULL,
    [AddressLine] nvarchar(500) NULL,
    [StoreCode] nvarchar(160) NULL,
    [StoreName] nvarchar(160) NULL,
    [ScheduledPickupAtUtc] datetime2(3) NULL,
    [ShippedAtUtc] datetime2(3) NULL,
    [ReceivedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ReturnShipments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ReturnShipments_ReturnRequests_ReturnRequestId] FOREIGN KEY ([ReturnRequestId]) REFERENCES [ReturnRequests] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ReturnStatusHistories] (
    [Id] bigint NOT NULL IDENTITY,
    [ReturnRequestId] bigint NOT NULL,
    [FromStatus] varchar(24) NULL,
    [ToStatus] varchar(24) NOT NULL,
    [ReasonCode] varchar(64) NULL,
    [Note] nvarchar(500) NULL,
    [ActorUserId] nvarchar(450) NULL,
    [OccurredAtUtc] datetime2(3) NOT NULL,
    CONSTRAINT [PK_ReturnStatusHistories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ReturnStatusHistories_AspNetUsers_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReturnStatusHistories_ReturnRequests_ReturnRequestId] FOREIGN KEY ([ReturnRequestId]) REFERENCES [ReturnRequests] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ShipmentStatusHistories] (
    [Id] bigint NOT NULL IDENTITY,
    [ShipmentId] bigint NOT NULL,
    [FromStatus] varchar(24) NULL,
    [ToStatus] varchar(24) NOT NULL,
    [ExternalEventId] nvarchar(128) NULL,
    [OccurredAtUtc] datetime2(3) NOT NULL,
    [ActorUserId] nvarchar(450) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_ShipmentStatusHistories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ShipmentStatusHistories_AspNetUsers_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ShipmentStatusHistories_Shipments_ShipmentId] FOREIGN KEY ([ShipmentId]) REFERENCES [Shipments] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SupportAssignmentHistories] (
    [Id] bigint NOT NULL IDENTITY,
    [SupportTicketId] bigint NOT NULL,
    [FromAdminUserId] nvarchar(450) NULL,
    [ToAdminUserId] nvarchar(450) NULL,
    [Action] varchar(24) NOT NULL,
    [Reason] nvarchar(500) NULL,
    [ActorUserId] nvarchar(450) NULL,
    [OccurredAtUtc] datetime2(3) NOT NULL,
    CONSTRAINT [PK_SupportAssignmentHistories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SupportAssignmentHistories_AspNetUsers_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SupportAssignmentHistories_AspNetUsers_FromAdminUserId] FOREIGN KEY ([FromAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SupportAssignmentHistories_AspNetUsers_ToAdminUserId] FOREIGN KEY ([ToAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SupportAssignmentHistories_SupportTickets_SupportTicketId] FOREIGN KEY ([SupportTicketId]) REFERENCES [SupportTickets] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SupportMessages] (
    [Id] bigint NOT NULL IDENTITY,
    [SupportTicketId] bigint NOT NULL,
    [SenderType] varchar(16) NOT NULL,
    [SenderUserId] nvarchar(450) NULL,
    [Body] nvarchar(4000) NOT NULL,
    [IsInternal] bit NOT NULL DEFAULT CAST(0 AS bit),
    [AiGenerated] bit NOT NULL DEFAULT CAST(0 AS bit),
    [ReplyToMessageId] bigint NULL,
    [Language] varchar(10) NOT NULL DEFAULT 'zh-TW',
    [SentAtUtc] datetime2(3) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_SupportMessages] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SupportMessages_InternalSender] CHECK ([IsInternal] = 0 OR [SenderType] = 'Admin'),
    CONSTRAINT [FK_SupportMessages_AspNetUsers_SenderUserId] FOREIGN KEY ([SenderUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SupportMessages_SupportMessages_ReplyToMessageId] FOREIGN KEY ([ReplyToMessageId]) REFERENCES [SupportMessages] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SupportMessages_SupportTickets_SupportTicketId] FOREIGN KEY ([SupportTicketId]) REFERENCES [SupportTickets] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SupportSlaEvents] (
    [Id] bigint NOT NULL IDENTITY,
    [SupportTicketId] bigint NOT NULL,
    [EventType] varchar(32) NOT NULL,
    [TargetType] varchar(24) NOT NULL,
    [DueAtUtc] datetime2(3) NULL,
    [DurationSeconds] int NULL,
    [OccurredAtUtc] datetime2(3) NOT NULL,
    [MetadataJson] nvarchar(2000) NULL,
    CONSTRAINT [PK_SupportSlaEvents] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SupportSlaEvents_Duration] CHECK ([DurationSeconds] IS NULL OR [DurationSeconds] >= 0),
    CONSTRAINT [CK_SupportSlaEvents_MetadataJson] CHECK ([MetadataJson] IS NULL OR ISJSON([MetadataJson]) = 1),
    CONSTRAINT [FK_SupportSlaEvents_SupportTickets_SupportTicketId] FOREIGN KEY ([SupportTicketId]) REFERENCES [SupportTickets] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SupportStatusHistories] (
    [Id] bigint NOT NULL IDENTITY,
    [SupportTicketId] bigint NOT NULL,
    [FromStatus] varchar(32) NULL,
    [ToStatus] varchar(32) NOT NULL,
    [ReasonCode] varchar(64) NULL,
    [Note] nvarchar(500) NULL,
    [ActorUserId] nvarchar(450) NULL,
    [OccurredAtUtc] datetime2(3) NOT NULL,
    CONSTRAINT [PK_SupportStatusHistories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SupportStatusHistories_AspNetUsers_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SupportStatusHistories_SupportTickets_SupportTicketId] FOREIGN KEY ([SupportTicketId]) REFERENCES [SupportTickets] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [InventoryMovements] (
    [Id] bigint NOT NULL IDENTITY,
    [SkuId] bigint NOT NULL,
    [ReservationId] bigint NULL,
    [MovementType] varchar(32) NOT NULL,
    [OnHandDelta] int NOT NULL,
    [ReservedDelta] int NOT NULL,
    [BeforeOnHand] int NOT NULL,
    [AfterOnHand] int NOT NULL,
    [BeforeReserved] int NOT NULL,
    [AfterReserved] int NOT NULL,
    [ReasonCode] varchar(32) NOT NULL,
    [ReferenceType] varchar(32) NOT NULL,
    [ReferencePublicId] uniqueidentifier NULL,
    [ActorUserId] nvarchar(450) NULL,
    [OccurredAtUtc] datetime2(3) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_InventoryMovements] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_InventoryMovements_OnHand] CHECK ([BeforeOnHand] + [OnHandDelta] = [AfterOnHand]),
    CONSTRAINT [CK_InventoryMovements_Reserved] CHECK ([BeforeReserved] + [ReservedDelta] = [AfterReserved]),
    CONSTRAINT [FK_InventoryMovements_AspNetUsers_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InventoryMovements_InventoryReservations_ReservationId] FOREIGN KEY ([ReservationId]) REFERENCES [InventoryReservations] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InventoryMovements_Skus_SkuId] FOREIGN KEY ([SkuId]) REFERENCES [Skus] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ProductReviews] (
    [Id] bigint NOT NULL IDENTITY,
    [MemberUserId] nvarchar(450) NOT NULL,
    [OrderItemId] bigint NOT NULL,
    [ProductId] bigint NOT NULL,
    [Rating] tinyint NOT NULL,
    [Title] nvarchar(160) NULL,
    [Content] nvarchar(2000) NOT NULL,
    [Status] varchar(24) NOT NULL DEFAULT 'Draft',
    [ReviewedByAdminUserId] nvarchar(450) NULL,
    [ReviewedAtUtc] datetime2(3) NULL,
    [RejectionReason] nvarchar(500) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ProductReviews] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ProductReviews_Rating] CHECK ([Rating] >= 1 AND [Rating] <= 5),
    CONSTRAINT [CK_ProductReviews_RejectionReason] CHECK ([Status] <> 'Rejected' OR [RejectionReason] IS NOT NULL),
    CONSTRAINT [FK_ProductReviews_AspNetUsers_MemberUserId] FOREIGN KEY ([MemberUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductReviews_AspNetUsers_ReviewedByAdminUserId] FOREIGN KEY ([ReviewedByAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductReviews_OrderItems_OrderItemId] FOREIGN KEY ([OrderItemId]) REFERENCES [OrderItems] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProductReviews_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ReturnItems] (
    [Id] bigint NOT NULL IDENTITY,
    [ReturnRequestId] bigint NOT NULL,
    [OrderItemId] bigint NOT NULL,
    [Quantity] int NOT NULL,
    [RequestedRefund] decimal(18,2) NOT NULL,
    [InspectionStatus] varchar(24) NOT NULL,
    [RestockDisposition] varchar(24) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_ReturnItems] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ReturnItems_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_ReturnItems_RequestedRefund] CHECK ([RequestedRefund] >= 0),
    CONSTRAINT [FK_ReturnItems_OrderItems_OrderItemId] FOREIGN KEY ([OrderItemId]) REFERENCES [OrderItems] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReturnItems_ReturnRequests_ReturnRequestId] FOREIGN KEY ([ReturnRequestId]) REFERENCES [ReturnRequests] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SimulatedInvoiceItems] (
    [Id] bigint NOT NULL IDENTITY,
    [SimulatedInvoiceId] bigint NOT NULL,
    [OrderItemId] bigint NULL,
    [ProductNameSnapshot] nvarchar(200) NOT NULL,
    [SkuCodeSnapshot] varchar(100) NOT NULL,
    [Quantity] int NOT NULL,
    [UnitPrice] decimal(18,2) NOT NULL,
    [DiscountAmount] decimal(18,2) NOT NULL,
    [NetAmount] decimal(18,2) NOT NULL,
    [TaxAmount] decimal(18,2) NOT NULL,
    [GrossAmount] decimal(18,2) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_SimulatedInvoiceItems] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SimulatedInvoiceItems_Amounts] CHECK ([UnitPrice] >= 0 AND [DiscountAmount] >= 0 AND [NetAmount] >= 0 AND [TaxAmount] >= 0 AND [GrossAmount] = [NetAmount] + [TaxAmount]),
    CONSTRAINT [CK_SimulatedInvoiceItems_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [FK_SimulatedInvoiceItems_OrderItems_OrderItemId] FOREIGN KEY ([OrderItemId]) REFERENCES [OrderItems] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SimulatedInvoiceItems_SimulatedInvoices_SimulatedInvoiceId] FOREIGN KEY ([SimulatedInvoiceId]) REFERENCES [SimulatedInvoices] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [RefundAllocations] (
    [Id] bigint NOT NULL IDENTITY,
    [RefundId] bigint NOT NULL,
    [OrderItemId] bigint NULL,
    [AllocationType] varchar(24) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [OriginalDiscountAllocation] decimal(18,2) NOT NULL DEFAULT 0.0,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_RefundAllocations] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_RefundAllocations_Amounts] CHECK ([Amount] > 0 AND [OriginalDiscountAllocation] >= 0),
    CONSTRAINT [FK_RefundAllocations_OrderItems_OrderItemId] FOREIGN KEY ([OrderItemId]) REFERENCES [OrderItems] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RefundAllocations_Refunds_RefundId] FOREIGN KEY ([RefundId]) REFERENCES [Refunds] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SimulatedInvoiceAllowances] (
    [Id] bigint NOT NULL IDENTITY,
    [SimulatedInvoiceId] bigint NOT NULL,
    [RefundId] bigint NOT NULL,
    [AllowanceNumber] nvarchar(32) NOT NULL,
    [NetAmount] decimal(18,2) NOT NULL,
    [TaxAmount] decimal(18,2) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [IssuedAtUtc] datetime2(3) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_SimulatedInvoiceAllowances] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SimulatedInvoiceAllowances_Amounts] CHECK ([NetAmount] >= 0 AND [TaxAmount] >= 0 AND [Amount] > 0 AND [Amount] = [NetAmount] + [TaxAmount]),
    CONSTRAINT [FK_SimulatedInvoiceAllowances_Refunds_RefundId] FOREIGN KEY ([RefundId]) REFERENCES [Refunds] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SimulatedInvoiceAllowances_SimulatedInvoices_SimulatedInvoiceId] FOREIGN KEY ([SimulatedInvoiceId]) REFERENCES [SimulatedInvoices] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ReturnShipmentEvents] (
    [Id] bigint NOT NULL IDENTITY,
    [ReturnShipmentId] bigint NOT NULL,
    [ExternalEventId] nvarchar(128) NOT NULL,
    [Source] varchar(32) NOT NULL,
    [EventType] varchar(64) NOT NULL,
    [EventCode] varchar(64) NULL,
    [Description] nvarchar(500) NULL,
    [OccurredAtUtc] datetime2(3) NOT NULL,
    [ReceivedAtUtc] datetime2(3) NOT NULL,
    [PayloadHash] binary(32) NULL,
    [PayloadSummaryJson] nvarchar(2000) NULL,
    CONSTRAINT [PK_ReturnShipmentEvents] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ReturnShipmentEvents_PayloadJson] CHECK ([PayloadSummaryJson] IS NULL OR ISJSON([PayloadSummaryJson]) = 1),
    CONSTRAINT [FK_ReturnShipmentEvents_ReturnShipments_ReturnShipmentId] FOREIGN KEY ([ReturnShipmentId]) REFERENCES [ReturnShipments] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SupportAttachments] (
    [Id] bigint NOT NULL IDENTITY,
    [SupportTicketId] bigint NOT NULL,
    [SupportMessageId] bigint NULL,
    [UploadedByUserId] nvarchar(450) NOT NULL,
    [OriginalFileName] nvarchar(255) NOT NULL,
    [StorageKey] nvarchar(500) NOT NULL,
    [Extension] varchar(10) NOT NULL,
    [MimeType] varchar(100) NOT NULL,
    [FileSizeBytes] bigint NOT NULL,
    [Sha256] binary(32) NOT NULL,
    [ScanStatus] varchar(20) NOT NULL,
    [ScannedAtUtc] datetime2(3) NULL,
    [RetentionUntilUtc] datetime2(3) NULL,
    [LegalHold] bit NOT NULL DEFAULT CAST(0 AS bit),
    [DeletedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_SupportAttachments] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SupportAttachments_FileSize] CHECK ([FileSizeBytes] >= 1 AND [FileSizeBytes] <= 10485760),
    CONSTRAINT [FK_SupportAttachments_AspNetUsers_UploadedByUserId] FOREIGN KEY ([UploadedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SupportAttachments_SupportMessages_SupportMessageId] FOREIGN KEY ([SupportMessageId]) REFERENCES [SupportMessages] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SupportAttachments_SupportTickets_SupportTicketId] FOREIGN KEY ([SupportTicketId]) REFERENCES [SupportTickets] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SupportSummaries] (
    [Id] bigint NOT NULL IDENTITY,
    [SupportTicketId] bigint NOT NULL,
    [SourceLastMessageId] bigint NOT NULL,
    [Summary] nvarchar(1500) NOT NULL,
    [Model] nvarchar(100) NOT NULL,
    [PromptVersion] nvarchar(64) NOT NULL,
    [Status] varchar(24) NOT NULL DEFAULT 'Pending',
    [GeneratedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_SupportSummaries] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SupportSummaries_SupportMessages_SourceLastMessageId] FOREIGN KEY ([SourceLastMessageId]) REFERENCES [SupportMessages] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SupportSummaries_SupportTickets_SupportTicketId] FOREIGN KEY ([SupportTicketId]) REFERENCES [SupportTickets] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [InventoryReconciliationCases] (
    [Id] bigint NOT NULL IDENTITY,
    [SkuId] bigint NOT NULL,
    [Status] varchar(24) NOT NULL,
    [ExpectedOnHand] int NOT NULL,
    [ActualOnHand] int NOT NULL,
    [ExpectedReserved] int NOT NULL,
    [ActualReserved] int NOT NULL,
    [DetectedAtUtc] datetime2(3) NOT NULL,
    [AcknowledgedBy] nvarchar(450) NULL,
    [ResolvedByAdminUserId] nvarchar(450) NULL,
    [ResolutionMovementId] bigint NULL,
    [ResolutionReason] nvarchar(1000) NULL,
    [ResolvedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_InventoryReconciliationCases] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_InventoryReconciliationCases_Quantities] CHECK ([ExpectedOnHand] >= 0 AND [ActualOnHand] >= 0 AND [ExpectedReserved] >= 0 AND [ActualReserved] >= 0),
    CONSTRAINT [CK_InventoryReconciliationCases_Resolution] CHECK (([Status] = 'Resolved' AND [ResolutionMovementId] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL) OR ([Status] = 'Dismissed' AND [ResolutionMovementId] IS NULL AND [ResolutionReason] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL) OR [Status] IN ('Open','Acknowledged')),
    CONSTRAINT [FK_InventoryReconciliationCases_AspNetUsers_AcknowledgedBy] FOREIGN KEY ([AcknowledgedBy]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InventoryReconciliationCases_AspNetUsers_ResolvedByAdminUserId] FOREIGN KEY ([ResolvedByAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InventoryReconciliationCases_InventoryMovements_ResolutionMovementId] FOREIGN KEY ([ResolutionMovementId]) REFERENCES [InventoryMovements] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InventoryReconciliationCases_Skus_SkuId] FOREIGN KEY ([SkuId]) REFERENCES [Skus] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ProductReviewRevisions] (
    [Id] bigint NOT NULL IDENTITY,
    [ProductReviewId] bigint NOT NULL,
    [Rating] tinyint NOT NULL,
    [Title] nvarchar(160) NULL,
    [Content] nvarchar(2000) NOT NULL,
    [PublishedAtUtc] datetime2(3) NOT NULL,
    [SupersededAtUtc] datetime2(3) NOT NULL,
    [SupersededReason] varchar(24) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    CONSTRAINT [PK_ProductReviewRevisions] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ProductReviewRevisions_Rating] CHECK ([Rating] >= 1 AND [Rating] <= 5),
    CONSTRAINT [FK_ProductReviewRevisions_ProductReviews_ProductReviewId] FOREIGN KEY ([ProductReviewId]) REFERENCES [ProductReviews] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ReviewImages] (
    [Id] bigint NOT NULL IDENTITY,
    [ProductReviewId] bigint NOT NULL,
    [StorageKey] nvarchar(500) NOT NULL,
    [OriginalFileName] nvarchar(255) NOT NULL,
    [MediaType] varchar(100) NOT NULL,
    [FileSizeBytes] bigint NOT NULL,
    [Sha256] binary(32) NOT NULL,
    [ScanStatus] varchar(20) NOT NULL DEFAULT 'Pending',
    [SortOrder] int NOT NULL DEFAULT 0,
    [DeletedAtUtc] datetime2(3) NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [UpdatedAtUtc] datetime2(3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ReviewImages] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ReviewImages_FileSize] CHECK ([FileSizeBytes] >= 1 AND [FileSizeBytes] <= 5242880),
    CONSTRAINT [FK_ReviewImages_ProductReviews_ProductReviewId] FOREIGN KEY ([ProductReviewId]) REFERENCES [ProductReviews] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ReturnInspections] (
    [Id] bigint NOT NULL IDENTITY,
    [ReturnItemId] bigint NOT NULL,
    [Result] varchar(24) NOT NULL,
    [ConditionCode] varchar(64) NOT NULL,
    [Note] nvarchar(1000) NULL,
    [InspectedByAdminUserId] nvarchar(450) NOT NULL,
    [InspectedAtUtc] datetime2(3) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_ReturnInspections] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ReturnInspections_AspNetUsers_InspectedByAdminUserId] FOREIGN KEY ([InspectedByAdminUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReturnInspections_ReturnItems_ReturnItemId] FOREIGN KEY ([ReturnItemId]) REFERENCES [ReturnItems] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SimulatedInvoiceAllowanceItems] (
    [Id] bigint NOT NULL IDENTITY,
    [AllowanceId] bigint NOT NULL,
    [SimulatedInvoiceItemId] bigint NOT NULL,
    [Quantity] int NOT NULL,
    [NetAmount] decimal(18,2) NOT NULL,
    [TaxAmount] decimal(18,2) NOT NULL,
    [GrossAmount] decimal(18,2) NOT NULL,
    [CreatedAtUtc] datetime2(3) NOT NULL,
    [PublicId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_SimulatedInvoiceAllowanceItems] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SimulatedInvoiceAllowanceItems_Amounts] CHECK ([NetAmount] >= 0 AND [TaxAmount] >= 0 AND [GrossAmount] > 0 AND [GrossAmount] = [NetAmount] + [TaxAmount]),
    CONSTRAINT [CK_SimulatedInvoiceAllowanceItems_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [FK_SimulatedInvoiceAllowanceItems_SimulatedInvoiceAllowances_AllowanceId] FOREIGN KEY ([AllowanceId]) REFERENCES [SimulatedInvoiceAllowances] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SimulatedInvoiceAllowanceItems_SimulatedInvoiceItems_SimulatedInvoiceItemId] FOREIGN KEY ([SimulatedInvoiceItemId]) REFERENCES [SimulatedInvoiceItems] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_AdminProfiles_IsActive] ON [AdminProfiles] ([IsActive]);

CREATE UNIQUE INDEX [UX_AdminProfiles_EmployeeCode] ON [AdminProfiles] ([EmployeeCode]);

CREATE UNIQUE INDEX [UX_AdminProfiles_PublicId] ON [AdminProfiles] ([PublicId]);

CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;

CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);

CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;

CREATE UNIQUE INDEX [UX_AspNetUsers_NormalizedEmail] ON [AspNetUsers] ([NormalizedEmail]) WHERE [NormalizedEmail] IS NOT NULL;

CREATE UNIQUE INDEX [UX_AspNetUsers_PublicId] ON [AspNetUsers] ([PublicId]);

CREATE INDEX [IX_AssemblyJobs_AssignedAdminUserId] ON [AssemblyJobs] ([AssignedAdminUserId]);

CREATE UNIQUE INDEX [UX_AssemblyJobs_OrderId_AssemblyGroupKey] ON [AssemblyJobs] ([OrderId], [AssemblyGroupKey]);

CREATE UNIQUE INDEX [UX_AssemblyJobs_PublicId] ON [AssemblyJobs] ([PublicId]);

CREATE INDEX [IX_AssemblyJobStatusHistories_ActorUserId] ON [AssemblyJobStatusHistories] ([ActorUserId]);

CREATE INDEX [IX_AssemblyJobStatusHistories_AssemblyJobId_OccurredAtUtc] ON [AssemblyJobStatusHistories] ([AssemblyJobId], [OccurredAtUtc]);

CREATE UNIQUE INDEX [UX_AssemblyJobStatusHistories_PublicId] ON [AssemblyJobStatusHistories] ([PublicId]);

CREATE UNIQUE INDEX [UX_Brands_Code] ON [Brands] ([Code]);

CREATE UNIQUE INDEX [UX_Brands_PublicId] ON [Brands] ([PublicId]);

CREATE INDEX [IX_BrandTranslations_ReviewedByAdminUserId] ON [BrandTranslations] ([ReviewedByAdminUserId]);

CREATE UNIQUE INDEX [UX_BrandTranslations_BrandId_Locale] ON [BrandTranslations] ([BrandId], [Locale]);

CREATE INDEX [IX_BuildListItems_SkuId] ON [BuildListItems] ([SkuId]);

CREATE UNIQUE INDEX [UX_BuildListItems_BuildListId_SkuId] ON [BuildListItems] ([BuildListId], [SkuId]);

CREATE UNIQUE INDEX [UX_BuildListItems_PublicId] ON [BuildListItems] ([PublicId]);

CREATE INDEX [IX_BuildLists_OwnerUserId_UpdatedAtUtc] ON [BuildLists] ([OwnerUserId], [UpdatedAtUtc]);

CREATE UNIQUE INDEX [UX_BuildLists_PublicId] ON [BuildLists] ([PublicId]);

CREATE INDEX [IX_BuildShareTokens_BuildListId] ON [BuildShareTokens] ([BuildListId]);

CREATE INDEX [IX_BuildShareTokens_ExpiresAtUtc] ON [BuildShareTokens] ([ExpiresAtUtc]) WHERE [ExpiresAtUtc] IS NOT NULL;

CREATE UNIQUE INDEX [UX_BuildShareTokens_PublicId] ON [BuildShareTokens] ([PublicId]);

CREATE UNIQUE INDEX [UX_BuildShareTokens_TokenHash] ON [BuildShareTokens] ([TokenHash]);

CREATE INDEX [IX_CartItems_SkuId] ON [CartItems] ([SkuId]);

CREATE UNIQUE INDEX [UX_CartItems_CartId_SkuId_AssemblyGroupKey] ON [CartItems] ([CartId], [SkuId], [AssemblyGroupKey]) WHERE [AssemblyGroupKey] IS NOT NULL;

CREATE UNIQUE INDEX [UX_CartItems_PublicId] ON [CartItems] ([PublicId]);

CREATE UNIQUE INDEX [UX_Carts_GuestCartKeyHash_Active] ON [Carts] ([GuestCartKeyHash]) WHERE [GuestCartKeyHash] IS NOT NULL AND [Status] = 'Active';

CREATE UNIQUE INDEX [UX_Carts_OwnerUserId_Active] ON [Carts] ([OwnerUserId]) WHERE [OwnerUserId] IS NOT NULL AND [Status] = 'Active';

CREATE UNIQUE INDEX [UX_Carts_PublicId] ON [Carts] ([PublicId]);

CREATE INDEX [IX_Categories_ParentCategoryId] ON [Categories] ([ParentCategoryId]);

CREATE UNIQUE INDEX [UX_Categories_Code] ON [Categories] ([Code]);

CREATE UNIQUE INDEX [UX_Categories_PublicId] ON [Categories] ([PublicId]);

CREATE UNIQUE INDEX [UX_Categories_Slug] ON [Categories] ([Slug]);

CREATE INDEX [IX_CategoryTranslations_ReviewedByAdminUserId] ON [CategoryTranslations] ([ReviewedByAdminUserId]);

CREATE UNIQUE INDEX [UX_CategoryTranslations_CategoryId_Locale] ON [CategoryTranslations] ([CategoryId], [Locale]);

CREATE INDEX [IX_CompatibilityCheckResults_RunId_Severity] ON [CompatibilityCheckResults] ([CompatibilityCheckRunId], [Severity]);

CREATE INDEX [IX_CompatibilityCheckRuns_BuildListId_EvaluatedAtUtc] ON [CompatibilityCheckRuns] ([BuildListId], [EvaluatedAtUtc]);

CREATE UNIQUE INDEX [UX_CompatibilityCheckRuns_PublicId] ON [CompatibilityCheckRuns] ([PublicId]);

CREATE INDEX [IX_CompatibilityRuleSettings_ChangedByAdminUserId] ON [CompatibilityRuleSettings] ([ChangedByAdminUserId]);

CREATE UNIQUE INDEX [UX_CompatibilityRuleSettings_PublicId] ON [CompatibilityRuleSettings] ([PublicId]);

CREATE UNIQUE INDEX [UX_CompatibilityRuleSettings_RuleCode_SettingCode_SettingsVersion] ON [CompatibilityRuleSettings] ([RuleCode], [SettingCode], [SettingsVersion]);

CREATE INDEX [IX_ConvenienceStores_City_District_IsActive] ON [ConvenienceStores] ([City], [District], [IsActive]);

CREATE UNIQUE INDEX [UX_ConvenienceStores_ProviderCode_StoreCode] ON [ConvenienceStores] ([ProviderCode], [StoreCode]);

CREATE UNIQUE INDEX [UX_ConvenienceStores_PublicId] ON [ConvenienceStores] ([PublicId]);

CREATE INDEX [IX_CouponCategories_CategoryId] ON [CouponCategories] ([CategoryId]);

CREATE INDEX [IX_CouponExcludedProducts_ProductId] ON [CouponExcludedProducts] ([ProductId]);

CREATE INDEX [IX_CouponProducts_ProductId] ON [CouponProducts] ([ProductId]);

CREATE INDEX [IX_CouponRedemptions_CouponId_GuestUsageKeyHash_Status] ON [CouponRedemptions] ([CouponId], [GuestUsageKeyHash], [Status]);

CREATE INDEX [IX_CouponRedemptions_CouponId_MemberUserId_Status] ON [CouponRedemptions] ([CouponId], [MemberUserId], [Status]);

CREATE INDEX [IX_CouponRedemptions_CouponId_Status] ON [CouponRedemptions] ([CouponId], [Status]);

CREATE INDEX [IX_CouponRedemptions_MemberUserId] ON [CouponRedemptions] ([MemberUserId]);

CREATE INDEX [IX_CouponRedemptions_OrderId] ON [CouponRedemptions] ([OrderId]);

CREATE UNIQUE INDEX [UX_CouponRedemptions_CouponId_OrderId] ON [CouponRedemptions] ([CouponId], [OrderId]);

CREATE UNIQUE INDEX [UX_CouponRedemptions_PublicId] ON [CouponRedemptions] ([PublicId]);

CREATE INDEX [IX_Coupons_EndsAtUtc] ON [Coupons] ([EndsAtUtc]);

CREATE INDEX [IX_Coupons_MemberOnly] ON [Coupons] ([MemberOnly]);

CREATE INDEX [IX_Coupons_NameZhTw] ON [Coupons] ([NameZhTw]);

CREATE INDEX [IX_Coupons_StartsAtUtc] ON [Coupons] ([StartsAtUtc]);

CREATE UNIQUE INDEX [UX_Coupons_Code] ON [Coupons] ([Code]);

CREATE UNIQUE INDEX [UX_Coupons_PublicId] ON [Coupons] ([PublicId]);

CREATE INDEX [IX_Favorites_ProductId] ON [Favorites] ([ProductId]);

CREATE INDEX [IX_GuestOrderAccessRequests_EmailKeyHash_CreatedAtUtc] ON [GuestOrderAccessRequests] ([EmailKeyHash], [CreatedAtUtc]);

CREATE INDEX [IX_GuestOrderAccessRequests_ExpiresAtUtc] ON [GuestOrderAccessRequests] ([ExpiresAtUtc]);

CREATE INDEX [IX_GuestOrderAccessRequests_OrderId] ON [GuestOrderAccessRequests] ([OrderId]);

CREATE INDEX [IX_GuestOrderAccessRequests_OrderLookupKeyHash_CreatedAtUtc] ON [GuestOrderAccessRequests] ([OrderLookupKeyHash], [CreatedAtUtc]);

CREATE INDEX [IX_GuestOrderAccessRequests_RequesterIpHash_CreatedAtUtc] ON [GuestOrderAccessRequests] ([RequesterIpHash], [CreatedAtUtc]);

CREATE UNIQUE INDEX [UX_GuestOrderAccessRequests_PublicId] ON [GuestOrderAccessRequests] ([PublicId]);

CREATE INDEX [IX_GuestOrderAccessTokens_ExpiresAtUtc] ON [GuestOrderAccessTokens] ([ExpiresAtUtc]);

CREATE INDEX [IX_GuestOrderAccessTokens_OrderId] ON [GuestOrderAccessTokens] ([OrderId]);

CREATE UNIQUE INDEX [UX_GuestOrderAccessTokens_PublicId] ON [GuestOrderAccessTokens] ([PublicId]);

CREATE UNIQUE INDEX [UX_GuestOrderAccessTokens_RequestId] ON [GuestOrderAccessTokens] ([RequestId]);

CREATE UNIQUE INDEX [UX_GuestOrderAccessTokens_TokenHash] ON [GuestOrderAccessTokens] ([TokenHash]);

CREATE INDEX [IX_ImportBatches_Status_ExpiresAtUtc] ON [ImportBatches] ([Status], [ExpiresAtUtc]);

CREATE UNIQUE INDEX [UX_ImportBatches_CreatedByAdminUserId_ImportType] ON [ImportBatches] ([CreatedByAdminUserId], [ImportType]) WHERE [Status] IN ('Uploaded','Validating','Ready','Committing');

CREATE UNIQUE INDEX [UX_ImportBatches_PublicId] ON [ImportBatches] ([PublicId]);

CREATE UNIQUE INDEX [UX_ImportRows_ImportBatchId_Dataset_ImportKey] ON [ImportRows] ([ImportBatchId], [Dataset], [ImportKey]);

CREATE UNIQUE INDEX [UX_ImportRows_ImportBatchId_Dataset_SourceRowNumber] ON [ImportRows] ([ImportBatchId], [Dataset], [SourceRowNumber]);

CREATE INDEX [IX_InventoryBalances_AvailableQuantity] ON [InventoryBalances] ([AvailableQuantity]);

CREATE UNIQUE INDEX [UX_InventoryBalances_PublicId] ON [InventoryBalances] ([PublicId]);

CREATE UNIQUE INDEX [UX_InventoryBalances_SkuId] ON [InventoryBalances] ([SkuId]);

CREATE INDEX [IX_InventoryMovements_ActorUserId] ON [InventoryMovements] ([ActorUserId]);

CREATE INDEX [IX_InventoryMovements_ReservationId] ON [InventoryMovements] ([ReservationId]);

CREATE INDEX [IX_InventoryMovements_SkuId_OccurredAtUtc] ON [InventoryMovements] ([SkuId], [OccurredAtUtc]);

CREATE UNIQUE INDEX [UX_InventoryMovements_PublicId] ON [InventoryMovements] ([PublicId]);

CREATE INDEX [IX_InventoryReconciliationCases_AcknowledgedBy] ON [InventoryReconciliationCases] ([AcknowledgedBy]);

CREATE INDEX [IX_InventoryReconciliationCases_ResolutionMovementId] ON [InventoryReconciliationCases] ([ResolutionMovementId]);

CREATE INDEX [IX_InventoryReconciliationCases_ResolvedByAdminUserId] ON [InventoryReconciliationCases] ([ResolvedByAdminUserId]);

CREATE INDEX [IX_InventoryReconciliationCases_Status_DetectedAtUtc] ON [InventoryReconciliationCases] ([Status], [DetectedAtUtc]);

CREATE UNIQUE INDEX [UX_InventoryReconciliationCases_PublicId] ON [InventoryReconciliationCases] ([PublicId]);

CREATE UNIQUE INDEX [UX_InventoryReconciliationCases_SkuId_Open] ON [InventoryReconciliationCases] ([SkuId]) WHERE [Status] = 'Open';

CREATE INDEX [IX_InventoryReservations_OrderId_SkuId] ON [InventoryReservations] ([OrderId], [SkuId]);

CREATE INDEX [IX_InventoryReservations_SkuId] ON [InventoryReservations] ([SkuId]);

CREATE INDEX [IX_InventoryReservations_Status_ExpiresAtUtc] ON [InventoryReservations] ([Status], [ExpiresAtUtc]);

CREATE UNIQUE INDEX [UX_InventoryReservations_PublicId] ON [InventoryReservations] ([PublicId]);

CREATE UNIQUE INDEX [UX_MeasurementUnits_Code] ON [MeasurementUnits] ([Code]);

CREATE UNIQUE INDEX [UX_MeasurementUnits_PublicId] ON [MeasurementUnits] ([PublicId]);

CREATE INDEX [IX_MemberAddresses_MemberUserId] ON [MemberAddresses] ([MemberUserId]);

CREATE UNIQUE INDEX [UX_MemberAddresses_MemberUserId_Default] ON [MemberAddresses] ([MemberUserId], [IsDefault]) WHERE [DeletedAtUtc] IS NULL AND [IsDefault] = 1;

CREATE UNIQUE INDEX [UX_MemberAddresses_PublicId] ON [MemberAddresses] ([PublicId]);

CREATE UNIQUE INDEX [UX_MemberProfiles_PublicId] ON [MemberProfiles] ([PublicId]);

CREATE INDEX [IX_OrderCoupons_CouponId] ON [OrderCoupons] ([CouponId]);

CREATE UNIQUE INDEX [UX_OrderCoupons_OrderId] ON [OrderCoupons] ([OrderId]);

CREATE UNIQUE INDEX [UX_OrderCoupons_PublicId] ON [OrderCoupons] ([PublicId]);

CREATE UNIQUE INDEX [UX_OrderCoupons_RedemptionId] ON [OrderCoupons] ([RedemptionId]) WHERE [RedemptionId] IS NOT NULL;

CREATE INDEX [IX_OrderItems_OrderId] ON [OrderItems] ([OrderId]);

CREATE INDEX [IX_OrderItems_OrderId_AssemblyGroupKey] ON [OrderItems] ([OrderId], [AssemblyGroupKey]);

CREATE INDEX [IX_OrderItems_SkuId] ON [OrderItems] ([SkuId]);

CREATE UNIQUE INDEX [UX_OrderItems_PublicId] ON [OrderItems] ([PublicId]);

CREATE INDEX [IX_Orders_CompletedAtUtc] ON [Orders] ([CompletedAtUtc]);

CREATE INDEX [IX_Orders_MemberUserId_CreatedAtUtc] ON [Orders] ([MemberUserId], [CreatedAtUtc]);

CREATE INDEX [IX_Orders_OrderStatus_PaymentDueAtUtc] ON [Orders] ([OrderStatus], [PaymentDueAtUtc]);

CREATE INDEX [IX_Orders_ShippingProviderProfileVersionId] ON [Orders] ([ShippingProviderProfileVersionId]);

CREATE UNIQUE INDEX [UX_Orders_CheckoutIdempotencyKey] ON [Orders] ([CheckoutIdempotencyKey]);

CREATE UNIQUE INDEX [UX_Orders_OrderNumber] ON [Orders] ([OrderNumber]);

CREATE UNIQUE INDEX [UX_Orders_PublicId] ON [Orders] ([PublicId]);

CREATE INDEX [IX_OrderStatusHistories_ActorUserId] ON [OrderStatusHistories] ([ActorUserId]);

CREATE INDEX [IX_OrderStatusHistories_OrderId_OccurredAtUtc] ON [OrderStatusHistories] ([OrderId], [OccurredAtUtc]);

CREATE UNIQUE INDEX [UX_OrderStatusHistories_PublicId] ON [OrderStatusHistories] ([PublicId]);

CREATE INDEX [IX_PackageLimitVersions_ProviderProfileId_Version] ON [PackageLimitVersions] ([ProviderProfileId], [Version]);

CREATE UNIQUE INDEX [UX_PackageLimitVersions_PublicId] ON [PackageLimitVersions] ([PublicId]);

CREATE INDEX [IX_PaymentAttempts_FailureCode] ON [PaymentAttempts] ([FailureCode]);

CREATE INDEX [IX_PaymentAttempts_InstructionExpiresAtUtc] ON [PaymentAttempts] ([InstructionExpiresAtUtc]);

CREATE INDEX [IX_PaymentAttempts_Method] ON [PaymentAttempts] ([Method]);

CREATE INDEX [IX_PaymentAttempts_OrderId_CreatedAtUtc] ON [PaymentAttempts] ([OrderId], [CreatedAtUtc]);

CREATE INDEX [IX_PaymentAttempts_ProviderCode] ON [PaymentAttempts] ([ProviderCode]);

CREATE INDEX [IX_PaymentAttempts_Status] ON [PaymentAttempts] ([Status]);

CREATE UNIQUE INDEX [UX_PaymentAttempts_ExternalReference] ON [PaymentAttempts] ([ExternalReference]) WHERE [ExternalReference] IS NOT NULL;

CREATE UNIQUE INDEX [UX_PaymentAttempts_IdempotencyKey] ON [PaymentAttempts] ([IdempotencyKey]);

CREATE UNIQUE INDEX [UX_PaymentAttempts_PublicId] ON [PaymentAttempts] ([PublicId]);

CREATE INDEX [IX_PaymentEvents_EventType] ON [PaymentEvents] ([EventType]);

CREATE INDEX [IX_PaymentEvents_OccurredAt] ON [PaymentEvents] ([OccurredAt]);

CREATE INDEX [IX_PaymentEvents_PayloadHash] ON [PaymentEvents] ([PayloadHash]);

CREATE INDEX [IX_PaymentEvents_PaymentAttemptId] ON [PaymentEvents] ([PaymentAttemptId]);

CREATE INDEX [IX_PaymentEvents_ProcessingStatus] ON [PaymentEvents] ([ProcessingStatus]);

CREATE INDEX [IX_PaymentEvents_ReceivedAtUtc] ON [PaymentEvents] ([ReceivedAtUtc]);

CREATE UNIQUE INDEX [UX_PaymentEvents_ExternalEventId] ON [PaymentEvents] ([ExternalEventId]);

CREATE UNIQUE INDEX [UX_PaymentEvents_PublicId] ON [PaymentEvents] ([PublicId]);

CREATE INDEX [IX_ProductImages_ProductId_Status_SortOrder] ON [ProductImages] ([ProductId], [Status], [SortOrder]);

CREATE INDEX [IX_ProductImages_SkuId] ON [ProductImages] ([SkuId]);

CREATE INDEX [IX_ProductImages_Status_DeletedAtUtc] ON [ProductImages] ([Status], [DeletedAtUtc]);

CREATE UNIQUE INDEX [UX_ProductImages_PublicId] ON [ProductImages] ([PublicId]);

CREATE UNIQUE INDEX [UX_ProductImages_StorageKey] ON [ProductImages] ([StorageKey]);

CREATE INDEX [IX_ProductReviewRevisions_ProductReviewId_SupersededAtUtc] ON [ProductReviewRevisions] ([ProductReviewId], [SupersededAtUtc]);

CREATE INDEX [IX_ProductReviews_MemberUserId_CreatedAtUtc] ON [ProductReviews] ([MemberUserId], [CreatedAtUtc]);

CREATE INDEX [IX_ProductReviews_ProductId_Status] ON [ProductReviews] ([ProductId], [Status]);

CREATE INDEX [IX_ProductReviews_ReviewedByAdminUserId] ON [ProductReviews] ([ReviewedByAdminUserId]);

CREATE UNIQUE INDEX [UX_ProductReviews_OrderItemId] ON [ProductReviews] ([OrderItemId]);

CREATE UNIQUE INDEX [UX_ProductReviews_PublicId] ON [ProductReviews] ([PublicId]);

CREATE INDEX [IX_Products_BrandId_Status] ON [Products] ([BrandId], [Status]);

CREATE INDEX [IX_Products_CategoryId_Status] ON [Products] ([CategoryId], [Status]);

CREATE UNIQUE INDEX [UX_Products_ProductCode] ON [Products] ([ProductCode]);

CREATE UNIQUE INDEX [UX_Products_PublicId] ON [Products] ([PublicId]);

CREATE INDEX [IX_ProductTags_TagId_ProductId] ON [ProductTags] ([TagId], [ProductId]);

CREATE INDEX [IX_ProductTranslations_ReviewedByAdminUserId] ON [ProductTranslations] ([ReviewedByAdminUserId]);

CREATE UNIQUE INDEX [UX_ProductTranslations_ProductId_Locale] ON [ProductTranslations] ([ProductId], [Locale]);

CREATE INDEX [IX_RefundAllocations_AllocationType] ON [RefundAllocations] ([AllocationType]);

CREATE INDEX [IX_RefundAllocations_OrderItemId] ON [RefundAllocations] ([OrderItemId]);

CREATE INDEX [IX_RefundAllocations_RefundId] ON [RefundAllocations] ([RefundId]);

CREATE UNIQUE INDEX [UX_RefundAllocations_PublicId] ON [RefundAllocations] ([PublicId]);

CREATE INDEX [IX_Refunds_ApprovedBy] ON [Refunds] ([ApprovedBy]);

CREATE INDEX [IX_Refunds_ExecutedByAdminUserId] ON [Refunds] ([ExecutedByAdminUserId]);

CREATE INDEX [IX_Refunds_OrderId] ON [Refunds] ([OrderId]);

CREATE INDEX [IX_Refunds_PaymentAttemptId] ON [Refunds] ([PaymentAttemptId]);

CREATE INDEX [IX_Refunds_ReasonCode] ON [Refunds] ([ReasonCode]);

CREATE INDEX [IX_Refunds_RequestedBy] ON [Refunds] ([RequestedBy]);

CREATE INDEX [IX_Refunds_ReturnRequestId] ON [Refunds] ([ReturnRequestId]);

CREATE INDEX [IX_Refunds_Status] ON [Refunds] ([Status]);

CREATE UNIQUE INDEX [UX_Refunds_IdempotencyKey] ON [Refunds] ([IdempotencyKey]);

CREATE UNIQUE INDEX [UX_Refunds_PublicId] ON [Refunds] ([PublicId]);

CREATE UNIQUE INDEX [UX_Refunds_RefundNumber] ON [Refunds] ([RefundNumber]);

CREATE INDEX [IX_ReportAssignmentHistories_ActorUserId] ON [ReportAssignmentHistories] ([ActorUserId]);

CREATE INDEX [IX_ReportAssignmentHistories_FromAdminUserId] ON [ReportAssignmentHistories] ([FromAdminUserId]);

CREATE INDEX [IX_ReportAssignmentHistories_ReportCaseId_OccurredAtUtc_Id] ON [ReportAssignmentHistories] ([ReportCaseId], [OccurredAtUtc], [Id]);

CREATE INDEX [IX_ReportAssignmentHistories_ToAdminUserId] ON [ReportAssignmentHistories] ([ToAdminUserId]);

CREATE INDEX [IX_ReportAttachments_ReportCaseId] ON [ReportAttachments] ([ReportCaseId]);

CREATE INDEX [IX_ReportAttachments_UploadedByUserId] ON [ReportAttachments] ([UploadedByUserId]);

CREATE UNIQUE INDEX [UX_ReportAttachments_PublicId] ON [ReportAttachments] ([PublicId]);

CREATE UNIQUE INDEX [UX_ReportAttachments_StorageKey] ON [ReportAttachments] ([StorageKey]);

CREATE INDEX [IX_ReportCases_AssigneeAdminUserId] ON [ReportCases] ([AssigneeAdminUserId]);

CREATE INDEX [IX_ReportCases_ReporterUserId] ON [ReportCases] ([ReporterUserId]);

CREATE INDEX [IX_ReportCases_Status_AssigneeAdminUserId_LastActivityAtUtc] ON [ReportCases] ([Status], [AssigneeAdminUserId], [LastActivityAtUtc]);

CREATE INDEX [IX_ReportCases_TargetType_TargetPublicId_Status] ON [ReportCases] ([TargetType], [TargetPublicId], [Status]);

CREATE UNIQUE INDEX [UX_ReportCases_OpenCaseKeyHash] ON [ReportCases] ([OpenCaseKeyHash]) WHERE [OpenCaseKeyHash] IS NOT NULL;

CREATE UNIQUE INDEX [UX_ReportCases_PublicId] ON [ReportCases] ([PublicId]);

CREATE UNIQUE INDEX [UX_ReportCases_ReportNumber] ON [ReportCases] ([ReportNumber]);

CREATE INDEX [IX_ReportStatusHistories_ActorUserId] ON [ReportStatusHistories] ([ActorUserId]);

CREATE INDEX [IX_ReportStatusHistories_ReportCaseId_OccurredAtUtc_Id] ON [ReportStatusHistories] ([ReportCaseId], [OccurredAtUtc], [Id]);

CREATE INDEX [IX_ReturnAssignmentHistories_ActorUserId] ON [ReturnAssignmentHistories] ([ActorUserId]);

CREATE INDEX [IX_ReturnAssignmentHistories_FromAdminUserId] ON [ReturnAssignmentHistories] ([FromAdminUserId]);

CREATE INDEX [IX_ReturnAssignmentHistories_ReturnRequestId_OccurredAtUtc_Id] ON [ReturnAssignmentHistories] ([ReturnRequestId], [OccurredAtUtc], [Id]);

CREATE INDEX [IX_ReturnAssignmentHistories_ToAdminUserId] ON [ReturnAssignmentHistories] ([ToAdminUserId]);

CREATE INDEX [IX_ReturnAttachments_ReturnRequestId] ON [ReturnAttachments] ([ReturnRequestId]);

CREATE INDEX [IX_ReturnAttachments_UploadedByUserId] ON [ReturnAttachments] ([UploadedByUserId]);

CREATE UNIQUE INDEX [UX_ReturnAttachments_PublicId] ON [ReturnAttachments] ([PublicId]);

CREATE UNIQUE INDEX [UX_ReturnAttachments_StorageKey] ON [ReturnAttachments] ([StorageKey]);

CREATE INDEX [IX_ReturnInspections_InspectedByAdminUserId] ON [ReturnInspections] ([InspectedByAdminUserId]);

CREATE INDEX [IX_ReturnInspections_ReturnItemId_InspectedAtUtc] ON [ReturnInspections] ([ReturnItemId], [InspectedAtUtc]);

CREATE UNIQUE INDEX [UX_ReturnInspections_PublicId] ON [ReturnInspections] ([PublicId]);

CREATE INDEX [IX_ReturnItems_OrderItemId] ON [ReturnItems] ([OrderItemId]);

CREATE UNIQUE INDEX [UX_ReturnItems_PublicId] ON [ReturnItems] ([PublicId]);

CREATE UNIQUE INDEX [UX_ReturnItems_ReturnRequestId_OrderItemId] ON [ReturnItems] ([ReturnRequestId], [OrderItemId]);

CREATE INDEX [IX_ReturnRequests_AssigneeAdminUserId] ON [ReturnRequests] ([AssigneeAdminUserId]);

CREATE INDEX [IX_ReturnRequests_OrderId_Status] ON [ReturnRequests] ([OrderId], [Status]);

CREATE INDEX [IX_ReturnRequests_RequesterUserId] ON [ReturnRequests] ([RequesterUserId]);

CREATE INDEX [IX_ReturnRequests_ReviewedByAdminUserId] ON [ReturnRequests] ([ReviewedByAdminUserId]);

CREATE INDEX [IX_ReturnRequests_Status_Priority_AssigneeAdminUserId_UpdatedAtUtc] ON [ReturnRequests] ([Status], [Priority], [AssigneeAdminUserId], [UpdatedAtUtc]);

CREATE UNIQUE INDEX [UX_ReturnRequests_PublicId] ON [ReturnRequests] ([PublicId]);

CREATE UNIQUE INDEX [UX_ReturnRequests_ReturnNumber] ON [ReturnRequests] ([ReturnNumber]);

CREATE INDEX [IX_ReturnShipmentEvents_ReturnShipmentId_OccurredAtUtc] ON [ReturnShipmentEvents] ([ReturnShipmentId], [OccurredAtUtc]);

CREATE UNIQUE INDEX [UX_ReturnShipmentEvents_Source_ExternalEventId] ON [ReturnShipmentEvents] ([Source], [ExternalEventId]);

CREATE INDEX [IX_ReturnShipments_CarrierCode_TrackingNumber] ON [ReturnShipments] ([CarrierCode], [TrackingNumber]);

CREATE UNIQUE INDEX [UX_ReturnShipments_PublicId] ON [ReturnShipments] ([PublicId]);

CREATE UNIQUE INDEX [UX_ReturnShipments_ReturnRequestId] ON [ReturnShipments] ([ReturnRequestId]);

CREATE UNIQUE INDEX [UX_ReturnShipments_ShipmentNumber] ON [ReturnShipments] ([ShipmentNumber]);

CREATE INDEX [IX_ReturnStatusHistories_ActorUserId] ON [ReturnStatusHistories] ([ActorUserId]);

CREATE INDEX [IX_ReturnStatusHistories_ReturnRequestId_OccurredAtUtc_Id] ON [ReturnStatusHistories] ([ReturnRequestId], [OccurredAtUtc], [Id]);

CREATE INDEX [IX_ReviewImages_ProductReviewId] ON [ReviewImages] ([ProductReviewId]);

CREATE UNIQUE INDEX [UX_ReviewImages_StorageKey] ON [ReviewImages] ([StorageKey]);

CREATE INDEX [IX_SalePrices_CreatedByAdminUserId] ON [SalePrices] ([CreatedByAdminUserId]);

CREATE INDEX [IX_SalePrices_SkuId_StartsAtUtc_EndsAtUtc] ON [SalePrices] ([SkuId], [StartsAtUtc], [EndsAtUtc]);

CREATE UNIQUE INDEX [UX_SalePrices_PublicId] ON [SalePrices] ([PublicId]);

CREATE INDEX [IX_Shipments_ConvenienceStoreId] ON [Shipments] ([ConvenienceStoreId]);

CREATE INDEX [IX_Shipments_OrderId] ON [Shipments] ([OrderId]);

CREATE INDEX [IX_Shipments_ProviderProfileVersionId] ON [Shipments] ([ProviderProfileVersionId]);

CREATE INDEX [IX_Shipments_ShippingMethodId] ON [Shipments] ([ShippingMethodId]);

CREATE UNIQUE INDEX [UX_Shipments_PublicId] ON [Shipments] ([PublicId]);

CREATE UNIQUE INDEX [UX_Shipments_ShipmentNumber] ON [Shipments] ([ShipmentNumber]);

CREATE INDEX [IX_ShipmentStatusHistories_ActorUserId] ON [ShipmentStatusHistories] ([ActorUserId]);

CREATE INDEX [IX_ShipmentStatusHistories_ShipmentId_OccurredAtUtc] ON [ShipmentStatusHistories] ([ShipmentId], [OccurredAtUtc]);

CREATE UNIQUE INDEX [UX_ShipmentStatusHistories_ExternalEventId] ON [ShipmentStatusHistories] ([ExternalEventId]) WHERE [ExternalEventId] IS NOT NULL;

CREATE UNIQUE INDEX [UX_ShipmentStatusHistories_PublicId] ON [ShipmentStatusHistories] ([PublicId]);

CREATE UNIQUE INDEX [UX_ShippingMethods_Code] ON [ShippingMethods] ([Code]);

CREATE UNIQUE INDEX [UX_ShippingMethods_PublicId] ON [ShippingMethods] ([PublicId]);

CREATE UNIQUE INDEX [UX_ProviderProfiles_ProviderCode_Published] ON [ShippingProviderProfiles] ([ProviderCode]) WHERE [Status] = 'Published';

CREATE UNIQUE INDEX [UX_ProviderProfiles_ProviderCode_Version] ON [ShippingProviderProfiles] ([ProviderCode], [Version]);

CREATE UNIQUE INDEX [UX_ShippingProviderProfiles_PublicId] ON [ShippingProviderProfiles] ([PublicId]);

CREATE INDEX [IX_SimulatedInvoiceAllowanceItems_AllowanceId] ON [SimulatedInvoiceAllowanceItems] ([AllowanceId]);

CREATE INDEX [IX_SimulatedInvoiceAllowanceItems_SimulatedInvoiceItemId] ON [SimulatedInvoiceAllowanceItems] ([SimulatedInvoiceItemId]);

CREATE UNIQUE INDEX [UX_SimulatedInvoiceAllowanceItems_PublicId] ON [SimulatedInvoiceAllowanceItems] ([PublicId]);

CREATE INDEX [IX_SimulatedInvoiceAllowances_IssuedAtUtc] ON [SimulatedInvoiceAllowances] ([IssuedAtUtc]);

CREATE INDEX [IX_SimulatedInvoiceAllowances_SimulatedInvoiceId] ON [SimulatedInvoiceAllowances] ([SimulatedInvoiceId]);

CREATE UNIQUE INDEX [UX_SimulatedInvoiceAllowances_AllowanceNumber] ON [SimulatedInvoiceAllowances] ([AllowanceNumber]);

CREATE UNIQUE INDEX [UX_SimulatedInvoiceAllowances_PublicId] ON [SimulatedInvoiceAllowances] ([PublicId]);

CREATE UNIQUE INDEX [UX_SimulatedInvoiceAllowances_RefundId] ON [SimulatedInvoiceAllowances] ([RefundId]);

CREATE INDEX [IX_SimulatedInvoiceItems_OrderItemId] ON [SimulatedInvoiceItems] ([OrderItemId]);

CREATE INDEX [IX_SimulatedInvoiceItems_SimulatedInvoiceId] ON [SimulatedInvoiceItems] ([SimulatedInvoiceId]);

CREATE INDEX [IX_SimulatedInvoiceItems_SkuCodeSnapshot] ON [SimulatedInvoiceItems] ([SkuCodeSnapshot]);

CREATE UNIQUE INDEX [UX_SimulatedInvoiceItems_PublicId] ON [SimulatedInvoiceItems] ([PublicId]);

CREATE INDEX [IX_SimulatedInvoices_BuyerType] ON [SimulatedInvoices] ([BuyerType]);

CREATE INDEX [IX_SimulatedInvoices_CompanyTaxId] ON [SimulatedInvoices] ([CompanyTaxId]);

CREATE INDEX [IX_SimulatedInvoices_DemoMarker] ON [SimulatedInvoices] ([DemoMarker]);

CREATE INDEX [IX_SimulatedInvoices_IssuedAtUtc] ON [SimulatedInvoices] ([IssuedAtUtc]);

CREATE INDEX [IX_SimulatedInvoices_Status] ON [SimulatedInvoices] ([Status]);

CREATE UNIQUE INDEX [UX_SimulatedInvoices_InvoiceNumber] ON [SimulatedInvoices] ([InvoiceNumber]);

CREATE UNIQUE INDEX [UX_SimulatedInvoices_OrderId] ON [SimulatedInvoices] ([OrderId]);

CREATE UNIQUE INDEX [UX_SimulatedInvoices_PublicId] ON [SimulatedInvoices] ([PublicId]);

CREATE INDEX [IX_Skus_ProductId_Status] ON [Skus] ([ProductId], [Status]);

CREATE UNIQUE INDEX [UX_Skus_ProductId_IsDefault] ON [Skus] ([ProductId], [IsDefault]) WHERE [IsDefault] = 1;

CREATE UNIQUE INDEX [UX_Skus_PublicId] ON [Skus] ([PublicId]);

CREATE UNIQUE INDEX [UX_Skus_SkuCode] ON [Skus] ([SkuCode]);

CREATE INDEX [IX_SkuSpecificationValues_DefinitionId_DecimalValue] ON [SkuSpecificationValues] ([SpecificationDefinitionId], [DecimalValue]);

CREATE INDEX [IX_SkuSpecificationValues_DefinitionId_OptionId] ON [SkuSpecificationValues] ([SpecificationDefinitionId], [OptionId]);

CREATE INDEX [IX_SkuSpecificationValues_OptionId] ON [SkuSpecificationValues] ([OptionId]);

CREATE INDEX [IX_SkuSpecificationValues_SpecificationSourceId] ON [SkuSpecificationValues] ([SpecificationSourceId]);

CREATE UNIQUE INDEX [UX_SkuSpecificationValues_SkuId_SpecificationDefinitionId] ON [SkuSpecificationValues] ([SkuId], [SpecificationDefinitionId]);

CREATE INDEX [IX_SkuTranslations_ReviewedByAdminUserId] ON [SkuTranslations] ([ReviewedByAdminUserId]);

CREATE UNIQUE INDEX [UX_SkuTranslations_SkuId_Locale] ON [SkuTranslations] ([SkuId], [Locale]);

CREATE INDEX [IX_SpecificationDefinitions_MeasurementUnitId] ON [SpecificationDefinitions] ([MeasurementUnitId]);

CREATE UNIQUE INDEX [UX_SpecificationDefinitions_CategoryId_SemanticKey] ON [SpecificationDefinitions] ([CategoryId], [SemanticKey]);

CREATE UNIQUE INDEX [UX_SpecificationDefinitions_PublicId] ON [SpecificationDefinitions] ([PublicId]);

CREATE INDEX [IX_SpecificationDefinitionTranslations_ReviewedByAdminUserId] ON [SpecificationDefinitionTranslations] ([ReviewedByAdminUserId]);

CREATE UNIQUE INDEX [UX_SpecDefTranslations_DefId_Locale] ON [SpecificationDefinitionTranslations] ([SpecificationDefinitionId], [Locale]);

CREATE UNIQUE INDEX [UX_SpecificationOptions_DefinitionId_Code] ON [SpecificationOptions] ([SpecificationDefinitionId], [Code]);

CREATE UNIQUE INDEX [UX_SpecificationOptions_PublicId] ON [SpecificationOptions] ([PublicId]);

CREATE INDEX [IX_SpecificationOptionTranslations_ReviewedByAdminUserId] ON [SpecificationOptionTranslations] ([ReviewedByAdminUserId]);

CREATE UNIQUE INDEX [UX_SpecOptTranslations_OptId_Locale] ON [SpecificationOptionTranslations] ([SpecificationOptionId], [Locale]);

CREATE INDEX [IX_SpecificationSources_ReviewedByAdminUserId] ON [SpecificationSources] ([ReviewedByAdminUserId]);

CREATE INDEX [IX_SpecificationSources_Url_Provider_Version] ON [SpecificationSources] ([SourceUrl], [ProviderName], [SourceVersion]);

CREATE UNIQUE INDEX [UX_SpecificationSources_PublicId] ON [SpecificationSources] ([PublicId]);

CREATE INDEX [IX_SupportAssignmentHistories_ActorUserId] ON [SupportAssignmentHistories] ([ActorUserId]);

CREATE INDEX [IX_SupportAssignmentHistories_FromAdminUserId] ON [SupportAssignmentHistories] ([FromAdminUserId]);

CREATE INDEX [IX_SupportAssignmentHistories_SupportTicketId_OccurredAtUtc_Id] ON [SupportAssignmentHistories] ([SupportTicketId], [OccurredAtUtc], [Id]);

CREATE INDEX [IX_SupportAssignmentHistories_ToAdminUserId] ON [SupportAssignmentHistories] ([ToAdminUserId]);

CREATE INDEX [IX_SupportAttachments_SupportMessageId] ON [SupportAttachments] ([SupportMessageId]);

CREATE INDEX [IX_SupportAttachments_SupportTicketId] ON [SupportAttachments] ([SupportTicketId]);

CREATE INDEX [IX_SupportAttachments_UploadedByUserId] ON [SupportAttachments] ([UploadedByUserId]);

CREATE UNIQUE INDEX [UX_SupportAttachments_PublicId] ON [SupportAttachments] ([PublicId]);

CREATE UNIQUE INDEX [UX_SupportAttachments_StorageKey] ON [SupportAttachments] ([StorageKey]);

CREATE INDEX [IX_SupportMessages_ReplyToMessageId] ON [SupportMessages] ([ReplyToMessageId]);

CREATE INDEX [IX_SupportMessages_SenderUserId] ON [SupportMessages] ([SenderUserId]);

CREATE INDEX [IX_SupportMessages_SupportTicketId_SentAtUtc_Id] ON [SupportMessages] ([SupportTicketId], [SentAtUtc], [Id]);

CREATE UNIQUE INDEX [UX_SupportMessages_PublicId] ON [SupportMessages] ([PublicId]);

CREATE INDEX [IX_SupportSlaEvents_SupportTicketId_OccurredAtUtc_Id] ON [SupportSlaEvents] ([SupportTicketId], [OccurredAtUtc], [Id]);

CREATE INDEX [IX_SupportStatusHistories_ActorUserId] ON [SupportStatusHistories] ([ActorUserId]);

CREATE INDEX [IX_SupportStatusHistories_SupportTicketId_OccurredAtUtc_Id] ON [SupportStatusHistories] ([SupportTicketId], [OccurredAtUtc], [Id]);

CREATE INDEX [IX_SupportSummaries_SourceLastMessageId] ON [SupportSummaries] ([SourceLastMessageId]);

CREATE UNIQUE INDEX [UX_SupportSummaries_PublicId] ON [SupportSummaries] ([PublicId]);

CREATE UNIQUE INDEX [UX_SupportSummaries_SupportTicketId_SourceLastMessageId] ON [SupportSummaries] ([SupportTicketId], [SourceLastMessageId]);

CREATE INDEX [IX_SupportTickets_AssigneeAdminUserId] ON [SupportTickets] ([AssigneeAdminUserId]);

CREATE INDEX [IX_SupportTickets_MemberUserId_CreatedAtUtc] ON [SupportTickets] ([MemberUserId], [CreatedAtUtc]);

CREATE INDEX [IX_SupportTickets_OrderId] ON [SupportTickets] ([OrderId]);

CREATE INDEX [IX_SupportTickets_Status_AssigneeAdminUserId_LastActivityAtUtc] ON [SupportTickets] ([Status], [AssigneeAdminUserId], [LastActivityAtUtc]);

CREATE INDEX [IX_SupportTickets_Status_FirstResponseDueAtUtc] ON [SupportTickets] ([Status], [FirstResponseDueAtUtc]);

CREATE INDEX [IX_SupportTickets_Status_ResolutionDueAtUtc] ON [SupportTickets] ([Status], [ResolutionDueAtUtc]);

CREATE UNIQUE INDEX [UX_SupportTickets_PublicId] ON [SupportTickets] ([PublicId]);

CREATE UNIQUE INDEX [UX_SupportTickets_TicketNumber] ON [SupportTickets] ([TicketNumber]);

CREATE UNIQUE INDEX [UX_Tags_Code] ON [Tags] ([Code]);

CREATE UNIQUE INDEX [UX_Tags_PublicId] ON [Tags] ([PublicId]);

EXEC(N'CREATE VIEW [dbo].[vw_CaseWorkbench]
AS
    SELECT
        support.[PublicId] AS [CasePublicId],
        CAST(''Support'' AS varchar(16)) AS [CaseType],
        support.[TicketNumber] AS [CaseNumber],
        CAST(support.[Category] AS nvarchar(1000)) AS [Title],
        CAST(support.[Status] AS varchar(32)) AS [Status],
        CAST(support.[Priority] AS varchar(16)) AS [Priority],
        CAST(N''會員'' AS nvarchar(200)) AS [RequesterDisplay],
        assignee.[PublicId] AS [AssigneePublicId],
        support.[CreatedAtUtc] AS [CreatedAtUtc],
        support.[LastActivityAtUtc] AS [LastActivityAtUtc],
        effectiveSla.[SlaDueAtUtc] AS [SlaDueAtUtc],
        CAST(
            CASE
                WHEN effectiveSla.[SlaDueAtUtc] IS NOT NULL
                    AND effectiveSla.[SlaDueAtUtc] < SYSUTCDATETIME()
                THEN 1
                ELSE 0
            END
            AS bit) AS [IsOverdue]
    FROM [dbo].[SupportTickets] AS support
    LEFT JOIN [dbo].[AdminProfiles] AS assignee
        ON assignee.[UserId] = support.[AssigneeAdminUserId]
    CROSS APPLY
    (
        SELECT CAST(
            CASE
                WHEN support.[Status] IN (''Resolved'', ''Closed'', ''Cancelled'') THEN NULL
                WHEN support.[FirstHumanResponseAtUtc] IS NULL THEN support.[FirstResponseDueAtUtc]
                ELSE DATEADD(
                    SECOND,
                    CASE
                        WHEN support.[WaitingForCustomerStartedAtUtc] IS NULL
                            OR support.[PausedSeconds] >= 259200
                            OR support.[WaitingForCustomerStartedAtUtc] >= SYSUTCDATETIME()
                        THEN support.[PausedSeconds]
                        WHEN DATEDIFF_BIG(
                            SECOND,
                            support.[WaitingForCustomerStartedAtUtc],
                            SYSUTCDATETIME()) >= 259200 - support.[PausedSeconds]
                        THEN 259200
                        ELSE support.[PausedSeconds] + CAST(
                            DATEDIFF_BIG(
                                SECOND,
                                support.[WaitingForCustomerStartedAtUtc],
                                SYSUTCDATETIME())
                            AS int)
                    END,
                    support.[ResolutionDueAtUtc])
            END
            AS datetime2(3)) AS [SlaDueAtUtc]
    ) AS effectiveSla

    UNION ALL

    SELECT
        returns.[PublicId] AS [CasePublicId],
        CAST(''Return'' AS varchar(16)) AS [CaseType],
        returns.[ReturnNumber] AS [CaseNumber],
        CAST(returns.[ReasonCode] AS nvarchar(1000)) AS [Title],
        CAST(returns.[Status] AS varchar(32)) AS [Status],
        CAST(returns.[Priority] AS varchar(16)) AS [Priority],
        CAST(
            CASE
                WHEN returns.[RequesterUserId] IS NULL THEN N''訪客''
                ELSE N''會員''
            END
            AS nvarchar(200)) AS [RequesterDisplay],
        assignee.[PublicId] AS [AssigneePublicId],
        returns.[CreatedAtUtc] AS [CreatedAtUtc],
        returns.[UpdatedAtUtc] AS [LastActivityAtUtc],
        CAST(NULL AS datetime2(3)) AS [SlaDueAtUtc],
        CAST(0 AS bit) AS [IsOverdue]
    FROM [dbo].[ReturnRequests] AS returns
    LEFT JOIN [dbo].[AdminProfiles] AS assignee
        ON assignee.[UserId] = returns.[AssigneeAdminUserId]

    UNION ALL

    SELECT
        reports.[PublicId] AS [CasePublicId],
        CAST(''Report'' AS varchar(16)) AS [CaseType],
        reports.[ReportNumber] AS [CaseNumber],
        CAST(reports.[ReasonCode] AS nvarchar(1000)) AS [Title],
        CAST(reports.[Status] AS varchar(32)) AS [Status],
        CAST(reports.[Priority] AS varchar(16)) AS [Priority],
        CAST(N''會員'' AS nvarchar(200)) AS [RequesterDisplay],
        assignee.[PublicId] AS [AssigneePublicId],
        reports.[CreatedAtUtc] AS [CreatedAtUtc],
        reports.[LastActivityAtUtc] AS [LastActivityAtUtc],
        CAST(NULL AS datetime2(3)) AS [SlaDueAtUtc],
        CAST(0 AS bit) AS [IsOverdue]
    FROM [dbo].[ReportCases] AS reports
    LEFT JOIN [dbo].[AdminProfiles] AS assignee
        ON assignee.[UserId] = reports.[AssigneeAdminUserId];');

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260819013357_InitialCreate', N'10.0.10');

COMMIT;
GO

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountType = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    AccountStatus = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    PreferredLocale = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValue: "zh-TW"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    AnonymizedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.CheckConstraint("CK_AspNetUsers_AccountStatus", "[AccountStatus] IN ('PendingEmailVerification','Active','Suspended','Anonymized','Disabled')");
                    table.CheckConstraint("CK_AspNetUsers_AccountType", "[AccountType] IN ('Member','Admin')");
                    table.CheckConstraint("CK_AspNetUsers_PreferredLocale", "[PreferredLocale] IN ('zh-TW','ja-JP','ko-KR')");
                });

            migrationBuilder.CreateTable(
                name: "Brands",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameZhTw = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    WebsiteUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameZhTw = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ParentCategoryId = table.Column<long>(type: "bigint", nullable: true),
                    Slug = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.CheckConstraint("CK_Categories_NotSelfParent", "[ParentCategoryId] IS NULL OR [ParentCategoryId] <> [Id]");
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConvenienceStores",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProviderCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StoreCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StoreName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    City = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    District = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsDemoData = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConvenienceStores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Coupons",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameZhTw = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    DiscountType = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    DiscountValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MinimumSpend = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MaximumDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    TotalUsageLimit = table.Column<int>(type: "int", nullable: true),
                    PerMemberLimit = table.Column<int>(type: "int", nullable: true),
                    MemberOnly = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ExcludeSaleItems = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ScopeType = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, defaultValue: "All"),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, defaultValue: "Draft"),
                    RuleVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coupons", x => x.Id);
                    table.CheckConstraint("CK_Coupons_Amounts", "([DiscountValue] IS NULL OR [DiscountValue] >= 0) AND ([MinimumSpend] IS NULL OR [MinimumSpend] >= 0) AND ([MaximumDiscount] IS NULL OR [MaximumDiscount] >= 0)");
                    table.CheckConstraint("CK_Coupons_Percentage", "[DiscountType] <> 'Percentage' OR ([DiscountValue] >= 0 AND [DiscountValue] <= 1)");
                    table.CheckConstraint("CK_Coupons_Period", "[EndsAtUtc] > [StartsAtUtc]");
                    table.CheckConstraint("CK_Coupons_UsageLimits", "([TotalUsageLimit] IS NULL OR [TotalUsageLimit] > 0) AND ([PerMemberLimit] IS NULL OR [PerMemberLimit] > 0)");
                });

            migrationBuilder.CreateTable(
                name: "MeasurementUnits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameZhTw = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Symbol = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Dimension = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementUnits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShippingMethods",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameZhTw = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Kind = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    BaseFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FreeShippingThreshold = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    AllowsCod = table.Column<bool>(type: "bit", nullable: false),
                    RequiresPrepayment = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingMethods", x => x.Id);
                    table.CheckConstraint("CK_ShippingMethods_CodCapability", "NOT ([AllowsCod] = 1 AND [RequiresPrepayment] = 1)");
                    table.CheckConstraint("CK_ShippingMethods_Fees", "[BaseFee] >= 0 AND ([FreeShippingThreshold] IS NULL OR [FreeShippingThreshold] >= 0)");
                });

            migrationBuilder.CreateTable(
                name: "ShippingProviderProfiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProviderCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    EffectiveToUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingProviderProfiles", x => x.Id);
                    table.CheckConstraint("CK_ShippingProviderProfiles_Period", "[EffectiveFromUtc] IS NULL OR [EffectiveToUtc] IS NULL OR [EffectiveToUtc] > [EffectiveFromUtc]");
                    table.CheckConstraint("CK_ShippingProviderProfiles_SchemaVersion", "[SchemaVersion] > 0");
                    table.CheckConstraint("CK_ShippingProviderProfiles_Version", "[Version] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameZhTw = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdminProfiles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_AdminProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BuildLists",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CompatibilityStatus = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildLists_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Carts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    GuestCartKeyHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carts", x => x.Id);
                    table.CheckConstraint("CK_Carts_ExactlyOneOwner", "([OwnerUserId] IS NOT NULL AND [GuestCartKeyHash] IS NULL) OR ([OwnerUserId] IS NULL AND [GuestCartKeyHash] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Carts_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompatibilityRuleSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RuleCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SettingCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    SettingsVersion = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ChangedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompatibilityRuleSettings", x => x.Id);
                    table.CheckConstraint("CK_CompatibilityRuleSettings_ExactlyOneValue", "([DecimalValue] IS NOT NULL AND [BooleanValue] IS NULL) OR ([DecimalValue] IS NULL AND [BooleanValue] IS NOT NULL)");
                    table.CheckConstraint("CK_CompatibilityRuleSettings_SettingsVersion", "[SettingsVersion] > 0");
                    table.ForeignKey(
                        name: "FK_CompatibilityRuleSettings_AspNetUsers_ChangedByAdminUserId",
                        column: x => x.ChangedByAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportType = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    TemplateVersion = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    CreatedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    SourceFileHash1 = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    SourceFileHash2 = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    SourceFileHash3 = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    SourceFileNameDisplay1 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SourceFileNameDisplay2 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SourceFileNameDisplay3 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    RowCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    NewCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    UpdatedCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    UnchangedCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ErrorCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    NormalizedContentVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ResultSummaryJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                    table.CheckConstraint("CK_ImportBatches_Counts", "[NewCount] >= 0 AND [UpdatedCount] >= 0 AND [UnchangedCount] >= 0 AND [ErrorCount] >= 0 AND [NewCount] + [UpdatedCount] + [UnchangedCount] + [ErrorCount] = [RowCount]");
                    table.CheckConstraint("CK_ImportBatches_RowCount", "[RowCount] >= 0 AND [RowCount] <= 5000");
                    table.ForeignKey(
                        name: "FK_ImportBatches_AspNetUsers_CreatedByAdminUserId",
                        column: x => x.CreatedByAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MemberAddresses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PostalCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    City = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    District = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AddressLine1 = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    AddressLine2 = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberAddresses_AspNetUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MemberProfiles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_MemberProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportCases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ReporterUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TargetType = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    TargetPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    Priority = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    AssigneeAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ResolutionCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LastActivityAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    OpenCaseKeyHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportCases_AspNetUsers_AssigneeAdminUserId",
                        column: x => x.AssigneeAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportCases_AspNetUsers_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpecificationSources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceType = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    ProviderName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    OriginalFieldName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    RetrievedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ReviewedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SourceVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecificationSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpecificationSources_AspNetUsers_ReviewedByAdminUserId",
                        column: x => x.ReviewedByAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BrandTranslations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BrandId = table.Column<long>(type: "bigint", nullable: false),
                    Locale = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TranslationStatus = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ReviewedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandTranslations", x => x.Id);
                    table.CheckConstraint("CK_BrandTranslations_Locale", "[Locale] IN ('zh-TW','ja-JP','ko-KR')");
                    table.ForeignKey(
                        name: "FK_BrandTranslations_AspNetUsers_ReviewedByAdminUserId",
                        column: x => x.ReviewedByAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BrandTranslations_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CategoryTranslations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryId = table.Column<long>(type: "bigint", nullable: false),
                    Locale = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TranslationStatus = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ReviewedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryTranslations", x => x.Id);
                    table.CheckConstraint("CK_CategoryTranslations_Locale", "[Locale] IN ('zh-TW','ja-JP','ko-KR')");
                    table.ForeignKey(
                        name: "FK_CategoryTranslations_AspNetUsers_ReviewedByAdminUserId",
                        column: x => x.ReviewedByAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CategoryTranslations_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BrandId = table.Column<long>(type: "bigint", nullable: false),
                    CategoryId = table.Column<long>(type: "bigint", nullable: false),
                    NameZhTw = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    DescriptionZhTw = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    WarrantyMonths = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    IsFeatured = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.CheckConstraint("CK_Products_WarrantyMonths", "[WarrantyMonths] IS NULL OR ([WarrantyMonths] >= 0 AND [WarrantyMonths] <= 120)");
                    table.ForeignKey(
                        name: "FK_Products_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CouponCategories",
                columns: table => new
                {
                    CouponId = table.Column<long>(type: "bigint", nullable: false),
                    CategoryId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponCategories", x => new { x.CouponId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_CouponCategories_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CouponCategories_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpecificationDefinitions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryId = table.Column<long>(type: "bigint", nullable: false),
                    SemanticKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayNameZhTw = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ValueType = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    MeasurementUnitId = table.Column<long>(type: "bigint", nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsProtected = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecificationDefinitions", x => x.Id);
                    table.CheckConstraint("CK_SpecificationDefinitions_MeasurementUnit", "[MeasurementUnitId] IS NULL OR [ValueType] = 'Decimal'");
                    table.ForeignKey(
                        name: "FK_SpecificationDefinitions_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpecificationDefinitions_MeasurementUnits_MeasurementUnitId",
                        column: x => x.MeasurementUnitId,
                        principalTable: "MeasurementUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MemberUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    GuestEmailNormalized = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    OrderStatus = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    PaymentStatus = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    FulfillmentStatus = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    AssemblyStatus = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    OrderRefundStatus = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    MerchandiseSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ItemDiscountTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AssemblyFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecipientPhone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    PostalCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    RecipientCity = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RecipientDistrict = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AddressLine1 = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    AddressLine2 = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ShippingMethodCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ShippingProviderProfileVersionId = table.Column<long>(type: "bigint", nullable: false),
                    StoreCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    StoreName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    StoreAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ShippingConstraintPolicyVersion = table.Column<int>(type: "int", nullable: false),
                    ReturnPolicyVersion = table.Column<int>(type: "int", nullable: false),
                    CouponPolicyVersion = table.Column<int>(type: "int", nullable: true),
                    PaymentDueAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ShippedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CheckoutIdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceCartPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.CheckConstraint("CK_Orders_Amounts_Nonnegative", "[MerchandiseSubtotal] >= 0 AND [ItemDiscountTotal] >= 0 AND [ShippingFee] >= 0 AND [AssemblyFee] >= 0 AND [GrandTotal] >= 0 AND [PaidAmount] >= 0 AND [RefundedAmount] >= 0");
                    table.CheckConstraint("CK_Orders_Currency", "[Currency] = 'TWD'");
                    table.CheckConstraint("CK_Orders_GrandTotal", "[GrandTotal] = [MerchandiseSubtotal] - [ItemDiscountTotal] + [ShippingFee] + [AssemblyFee]");
                    table.CheckConstraint("CK_Orders_Owner", "[MemberUserId] IS NOT NULL OR [GuestEmailNormalized] IS NOT NULL");
                    table.CheckConstraint("CK_Orders_PaidAmount", "[PaidAmount] <= [GrandTotal]");
                    table.CheckConstraint("CK_Orders_PolicyVersions", "[ShippingConstraintPolicyVersion] > 0 AND [ReturnPolicyVersion] > 0 AND ([CouponPolicyVersion] IS NULL OR [CouponPolicyVersion] > 0)");
                    table.CheckConstraint("CK_Orders_RefundedAmount", "[RefundedAmount] <= [PaidAmount]");
                    table.ForeignKey(
                        name: "FK_Orders_AspNetUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_ShippingProviderProfiles_ShippingProviderProfileVersionId",
                        column: x => x.ShippingProviderProfileVersionId,
                        principalTable: "ShippingProviderProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PackageLimitVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProviderProfileId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    MaxWeightKg = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: false),
                    MaxLengthCm = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    MaxWidthCm = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    MaxHeightCm = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    MaxTotalCm = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    MaxDeclaredValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    EffectiveToUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageLimitVersions", x => x.Id);
                    table.CheckConstraint("CK_PackageLimitVersions_Limits", "[MaxWeightKg] > 0 AND [MaxLengthCm] > 0 AND [MaxWidthCm] > 0 AND [MaxHeightCm] > 0 AND [MaxTotalCm] > 0 AND [MaxDeclaredValue] > 0");
                    table.CheckConstraint("CK_PackageLimitVersions_Period", "[EffectiveFromUtc] IS NULL OR [EffectiveToUtc] IS NULL OR [EffectiveToUtc] > [EffectiveFromUtc]");
                    table.CheckConstraint("CK_PackageLimitVersions_Version", "[Version] > 0");
                    table.ForeignKey(
                        name: "FK_PackageLimitVersions_ShippingProviderProfiles_ProviderProfileId",
                        column: x => x.ProviderProfileId,
                        principalTable: "ShippingProviderProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BuildShareTokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuildListId = table.Column<long>(type: "bigint", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LastAccessedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildShareTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildShareTokens_BuildLists_BuildListId",
                        column: x => x.BuildListId,
                        principalTable: "BuildLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompatibilityCheckRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuildListId = table.Column<long>(type: "bigint", nullable: true),
                    RuleSetVersion = table.Column<int>(type: "int", nullable: false),
                    SettingsVersion = table.Column<int>(type: "int", nullable: false),
                    Overall = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    InputHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompatibilityCheckRuns", x => x.Id);
                    table.CheckConstraint("CK_CompatibilityCheckRuns_Overall", "[Overall] IN ('Compatible','Warning','Blocked','InsufficientData')");
                    table.CheckConstraint("CK_CompatibilityCheckRuns_Versions", "[RuleSetVersion] > 0 AND [SettingsVersion] > 0");
                    table.ForeignKey(
                        name: "FK_CompatibilityCheckRuns_BuildLists_BuildListId",
                        column: x => x.BuildListId,
                        principalTable: "BuildLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportBatchId = table.Column<long>(type: "bigint", nullable: false),
                    Dataset = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                    ImportKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    NormalizedPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorCodes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRows", x => x.Id);
                    table.CheckConstraint("CK_ImportRows_SourceRowNumber", "[SourceRowNumber] > 0");
                    table.ForeignKey(
                        name: "FK_ImportRows_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportAssignmentHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportCaseId = table.Column<long>(type: "bigint", nullable: false),
                    FromAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ToAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Action = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportAssignmentHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportAssignmentHistories_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportAssignmentHistories_AspNetUsers_FromAdminUserId",
                        column: x => x.FromAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportAssignmentHistories_AspNetUsers_ToAdminUserId",
                        column: x => x.ToAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportAssignmentHistories_ReportCases_ReportCaseId",
                        column: x => x.ReportCaseId,
                        principalTable: "ReportCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportAttachments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportCaseId = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Extension = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    MimeType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ScanStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ScannedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    RetentionUntilUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LegalHold = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportAttachments", x => x.Id);
                    table.CheckConstraint("CK_ReportAttachments_FileSize", "[FileSizeBytes] >= 1 AND [FileSizeBytes] <= 10485760");
                    table.ForeignKey(
                        name: "FK_ReportAttachments_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportAttachments_ReportCases_ReportCaseId",
                        column: x => x.ReportCaseId,
                        principalTable: "ReportCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportStatusHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportCaseId = table.Column<long>(type: "bigint", nullable: false),
                    FromStatus = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: true),
                    ToStatus = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    ActionCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportStatusHistories_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportStatusHistories_ReportCases_ReportCaseId",
                        column: x => x.ReportCaseId,
                        principalTable: "ReportCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CouponExcludedProducts",
                columns: table => new
                {
                    CouponId = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponExcludedProducts", x => new { x.CouponId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_CouponExcludedProducts_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CouponExcludedProducts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CouponProducts",
                columns: table => new
                {
                    CouponId = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponProducts", x => new { x.CouponId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_CouponProducts_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CouponProducts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Favorites",
                columns: table => new
                {
                    MemberUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favorites", x => new { x.MemberUserId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_Favorites_AspNetUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Favorites_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductTags",
                columns: table => new
                {
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    TagId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTags", x => new { x.ProductId, x.TagId });
                    table.ForeignKey(
                        name: "FK_ProductTags_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductTranslations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    Locale = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    TranslationStatus = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ReviewedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTranslations", x => x.Id);
                    table.CheckConstraint("CK_ProductTranslations_Locale", "[Locale] IN ('zh-TW','ja-JP','ko-KR')");
                    table.ForeignKey(
                        name: "FK_ProductTranslations_AspNetUsers_ReviewedByAdminUserId",
                        column: x => x.ReviewedByAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductTranslations_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Skus",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SkuCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    NameZhTw = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ListPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    WeightKg = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    LengthCm = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    WidthCm = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    HeightCm = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RequiresPrepayment = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Skus", x => x.Id);
                    table.CheckConstraint("CK_Skus_Dimensions", "([WeightKg] IS NULL OR [WeightKg] > 0) AND ([LengthCm] IS NULL OR [LengthCm] > 0) AND ([WidthCm] IS NULL OR [WidthCm] > 0) AND ([HeightCm] IS NULL OR [HeightCm] > 0)");
                    table.CheckConstraint("CK_Skus_Prices", "[ListPrice] >= 0 AND [UnitCost] >= 0");
                    table.ForeignKey(
                        name: "FK_Skus_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpecificationDefinitionTranslations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SpecificationDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    Locale = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    HelpText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TranslationStatus = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ReviewedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecificationDefinitionTranslations", x => x.Id);
                    table.CheckConstraint("CK_SpecificationDefinitionTranslations_Locale", "[Locale] IN ('zh-TW','ja-JP','ko-KR')");
                    table.ForeignKey(
                        name: "FK_SpecificationDefinitionTranslations_AspNetUsers_ReviewedByAdminUserId",
                        column: x => x.ReviewedByAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpecificationDefinitionTranslations_SpecificationDefinitions_SpecificationDefinitionId",
                        column: x => x.SpecificationDefinitionId,
                        principalTable: "SpecificationDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpecificationOptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SpecificationDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayNameZhTw = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecificationOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpecificationOptions_SpecificationDefinitions_SpecificationDefinitionId",
                        column: x => x.SpecificationDefinitionId,
                        principalTable: "SpecificationDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssemblyJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    AssemblyGroupKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    AssignedAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssemblyJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssemblyJobs_AspNetUsers_AssignedAdminUserId",
                        column: x => x.AssignedAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssemblyJobs_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CouponRedemptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CouponId = table.Column<long>(type: "bigint", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    MemberUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    GuestUsageKeyHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, defaultValue: "Reserved"),
                    ReservedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponRedemptions", x => x.Id);
                    table.CheckConstraint("CK_CouponRedemptions_Owner", "([MemberUserId] IS NOT NULL AND [GuestUsageKeyHash] IS NULL) OR ([MemberUserId] IS NULL AND [GuestUsageKeyHash] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CouponRedemptions_AspNetUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CouponRedemptions_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CouponRedemptions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GuestOrderAccessRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<long>(type: "bigint", nullable: true),
                    CodeHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    RequesterIpHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    EmailKeyHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    OrderLookupKeyHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    SendCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LastSentAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuestOrderAccessRequests", x => x.Id);
                    table.CheckConstraint("CK_GuestOrderAccessRequests_AttemptCount", "[AttemptCount] >= 0 AND [AttemptCount] <= 5");
                    table.CheckConstraint("CK_GuestOrderAccessRequests_SendCount", "[SendCount] >= 0 AND [SendCount] <= 3");
                    table.ForeignKey(
                        name: "FK_GuestOrderAccessRequests_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderStatusHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    StateDimension = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    FromStatus = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    ToStatus = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    TraceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderStatusHistories_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderStatusHistories_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAttempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    Method = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false, defaultValue: "Pending"),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExternalReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    InstructionExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAttempts", x => x.Id);
                    table.CheckConstraint("CK_PaymentAttempts_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_PaymentAttempts_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    RequesterUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    Priority = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, defaultValue: "Normal"),
                    ReasonCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AssigneeAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    PolicyVersion = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ReturnShipmentDueAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnRequests", x => x.Id);
                    table.CheckConstraint("CK_ReturnRequests_PolicyVersion", "[PolicyVersion] > 0");
                    table.ForeignKey(
                        name: "FK_ReturnRequests_AspNetUsers_AssigneeAdminUserId",
                        column: x => x.AssigneeAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnRequests_AspNetUsers_RequesterUserId",
                        column: x => x.RequesterUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnRequests_AspNetUsers_ReviewedByAdminUserId",
                        column: x => x.ReviewedByAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnRequests_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    ShippingMethodId = table.Column<long>(type: "bigint", nullable: false),
                    ProviderProfileVersionId = table.Column<long>(type: "bigint", nullable: false),
                    ConvenienceStoreId = table.Column<long>(type: "bigint", nullable: true),
                    ShipmentNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    TrackingNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FeeSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                    table.CheckConstraint("CK_Shipments_FeeSnapshot", "[FeeSnapshot] >= 0");
                    table.ForeignKey(
                        name: "FK_Shipments_ConvenienceStores_ConvenienceStoreId",
                        column: x => x.ConvenienceStoreId,
                        principalTable: "ConvenienceStores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_ShippingMethods_ShippingMethodId",
                        column: x => x.ShippingMethodId,
                        principalTable: "ShippingMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_ShippingProviderProfiles_ProviderProfileVersionId",
                        column: x => x.ProviderProfileVersionId,
                        principalTable: "ShippingProviderProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SimulatedInvoices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BuyerType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    BuyerEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    CarrierType = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    CarrierValueMasked = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CompanyTaxId = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IssuedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, nullable: false, defaultValue: "TWD"),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, defaultValue: "Pending"),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    DemoMarker = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "DEMO-NOT-A-TAX-INVOICE"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulatedInvoices", x => x.Id);
                    table.CheckConstraint("CK_SimulatedInvoices_Amounts", "[NetAmount] >= 0 AND [TaxAmount] >= 0 AND [IssuedAmount] = [NetAmount] + [TaxAmount]");
                    table.CheckConstraint("CK_SimulatedInvoices_CompanyBuyer", "[BuyerType] <> 'Company' OR ([CompanyTaxId] IS NOT NULL AND [CompanyName] IS NOT NULL)");
                    table.CheckConstraint("CK_SimulatedInvoices_Currency", "[Currency] = 'TWD'");
                    table.CheckConstraint("CK_SimulatedInvoices_DemoMarker", "[DemoMarker] = 'DEMO-NOT-A-TAX-INVOICE'");
                    table.ForeignKey(
                        name: "FK_SimulatedInvoices_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportTickets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MemberUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: true),
                    Category = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Priority = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    AssigneeAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    FirstResponseDueAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ResolutionDueAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    FirstHumanResponseAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    WaitingForCustomerStartedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    PausedSeconds = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LastActivityAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ReopenCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTickets", x => x.Id);
                    table.CheckConstraint("CK_SupportTickets_PausedSeconds", "[PausedSeconds] >= 0 AND [PausedSeconds] <= 259200");
                    table.CheckConstraint("CK_SupportTickets_ReopenCount", "[ReopenCount] >= 0");
                    table.ForeignKey(
                        name: "FK_SupportTickets_AspNetUsers_AssigneeAdminUserId",
                        column: x => x.AssigneeAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportTickets_AspNetUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportTickets_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompatibilityCheckResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompatibilityCheckRunId = table.Column<long>(type: "bigint", nullable: false),
                    RuleCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    MessageKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    FactsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompatibilityCheckResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompatibilityCheckResults_CompatibilityCheckRuns_CompatibilityCheckRunId",
                        column: x => x.CompatibilityCheckRunId,
                        principalTable: "CompatibilityCheckRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BuildListItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuildListId = table.Column<long>(type: "bigint", nullable: false),
                    SkuId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildListItems", x => x.Id);
                    table.CheckConstraint("CK_BuildListItems_Quantity", "[Quantity] >= 1 AND [Quantity] <= 8");
                    table.ForeignKey(
                        name: "FK_BuildListItems_BuildLists_BuildListId",
                        column: x => x.BuildListId,
                        principalTable: "BuildLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuildListItems_Skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "Skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CartItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CartId = table.Column<long>(type: "bigint", nullable: false),
                    SkuId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    AssemblyGroupKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartItems", x => x.Id);
                    table.CheckConstraint("CK_CartItems_Quantity", "[Quantity] >= 1 AND [Quantity] <= 99");
                    table.ForeignKey(
                        name: "FK_CartItems_Carts_CartId",
                        column: x => x.CartId,
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CartItems_Skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "Skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryBalances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SkuId = table.Column<long>(type: "bigint", nullable: false),
                    OnHandQuantity = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ReservedQuantity = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    AvailableQuantity = table.Column<int>(type: "int", nullable: false, computedColumnSql: "[OnHandQuantity] - [ReservedQuantity]", stored: true),
                    ReorderLevel = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryBalances", x => x.Id);
                    table.CheckConstraint("CK_InventoryBalances_OnHand", "[OnHandQuantity] >= 0");
                    table.CheckConstraint("CK_InventoryBalances_ReorderLevel", "[ReorderLevel] >= 0");
                    table.CheckConstraint("CK_InventoryBalances_Reserved", "[ReservedQuantity] >= 0 AND [ReservedQuantity] <= [OnHandQuantity]");
                    table.ForeignKey(
                        name: "FK_InventoryBalances_Skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "Skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryReservations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SkuId = table.Column<long>(type: "bigint", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ReleasedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ReleaseReason = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryReservations", x => x.Id);
                    table.CheckConstraint("CK_InventoryReservations_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_InventoryReservations_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservations_Skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "Skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    SkuId = table.Column<long>(type: "bigint", nullable: true),
                    SkuCodeSnapshot = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProductNameSnapshot = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    SkuNameSnapshot = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    ListUnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SaleUnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FinalUnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitCostSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAllocation = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AssemblyGroupKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReturnableQuantity = table.Column<int>(type: "int", nullable: false),
                    ReturnedQuantity = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.CheckConstraint("CK_OrderItems_Amounts_Nonnegative", "[ListUnitPrice] >= 0 AND [SaleUnitPrice] >= 0 AND [FinalUnitPrice] >= 0 AND [UnitCostSnapshot] >= 0 AND [LineSubtotal] >= 0 AND [DiscountAllocation] >= 0 AND [LineTotal] >= 0");
                    table.CheckConstraint("CK_OrderItems_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_OrderItems_ReturnedQuantity", "[ReturnedQuantity] >= 0 AND [ReturnableQuantity] >= [ReturnedQuantity] AND [Quantity] >= [ReturnableQuantity]");
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderItems_Skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "Skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductImages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    SkuId = table.Column<long>(type: "bigint", nullable: true),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MediaType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Sha256 = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    AltTextZhTw = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    LicenseUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    AuthorName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    LicenseName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    DownloadedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImages", x => x.Id);
                    table.CheckConstraint("CK_ProductImages_Dimensions", "[Width] > 0 AND [Height] > 0");
                    table.CheckConstraint("CK_ProductImages_FileSize", "[FileSizeBytes] >= 1 AND [FileSizeBytes] <= 10485760");
                    table.ForeignKey(
                        name: "FK_ProductImages_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductImages_Skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "Skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalePrices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SkuId = table.Column<long>(type: "bigint", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    CreatedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalePrices", x => x.Id);
                    table.CheckConstraint("CK_SalePrices_Period", "[EndsAtUtc] > [StartsAtUtc]");
                    table.CheckConstraint("CK_SalePrices_Price", "[Price] >= 0");
                    table.ForeignKey(
                        name: "FK_SalePrices_AspNetUsers_CreatedByAdminUserId",
                        column: x => x.CreatedByAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalePrices_Skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "Skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SkuTranslations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SkuId = table.Column<long>(type: "bigint", nullable: false),
                    Locale = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    TranslationStatus = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ReviewedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkuTranslations", x => x.Id);
                    table.CheckConstraint("CK_SkuTranslations_Locale", "[Locale] IN ('zh-TW','ja-JP','ko-KR')");
                    table.ForeignKey(
                        name: "FK_SkuTranslations_AspNetUsers_ReviewedByAdminUserId",
                        column: x => x.ReviewedByAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkuTranslations_Skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "Skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SkuSpecificationValues",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SkuId = table.Column<long>(type: "bigint", nullable: false),
                    SpecificationDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    StringValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    OptionId = table.Column<long>(type: "bigint", nullable: true),
                    SpecificationSourceId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkuSpecificationValues", x => x.Id);
                    table.CheckConstraint("CK_SkuSpecificationValues_ExactlyOneValue", "(CASE WHEN [StringValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_SkuSpecificationValues_Skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "Skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkuSpecificationValues_SpecificationDefinitions_SpecificationDefinitionId",
                        column: x => x.SpecificationDefinitionId,
                        principalTable: "SpecificationDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkuSpecificationValues_SpecificationOptions_OptionId",
                        column: x => x.OptionId,
                        principalTable: "SpecificationOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkuSpecificationValues_SpecificationSources_SpecificationSourceId",
                        column: x => x.SpecificationSourceId,
                        principalTable: "SpecificationSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpecificationOptionTranslations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SpecificationOptionId = table.Column<long>(type: "bigint", nullable: false),
                    Locale = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    TranslationStatus = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ReviewedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecificationOptionTranslations", x => x.Id);
                    table.CheckConstraint("CK_SpecificationOptionTranslations_Locale", "[Locale] IN ('zh-TW','ja-JP','ko-KR')");
                    table.ForeignKey(
                        name: "FK_SpecificationOptionTranslations_AspNetUsers_ReviewedByAdminUserId",
                        column: x => x.ReviewedByAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpecificationOptionTranslations_SpecificationOptions_SpecificationOptionId",
                        column: x => x.SpecificationOptionId,
                        principalTable: "SpecificationOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssemblyJobStatusHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssemblyJobId = table.Column<long>(type: "bigint", nullable: false),
                    FromStatus = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: true),
                    ToStatus = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    TraceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssemblyJobStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssemblyJobStatusHistories_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssemblyJobStatusHistories_AssemblyJobs_AssemblyJobId",
                        column: x => x.AssemblyJobId,
                        principalTable: "AssemblyJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderCoupons",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    CouponId = table.Column<long>(type: "bigint", nullable: true),
                    RedemptionId = table.Column<long>(type: "bigint", nullable: true),
                    CouponCodeSnapshot = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameSnapshot = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    DiscountType = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    RuleVersion = table.Column<int>(type: "int", nullable: false),
                    DiscountValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    AppliedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EligibleSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsFreeShipping = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderCoupons", x => x.Id);
                    table.CheckConstraint("CK_OrderCoupons_Amounts", "([DiscountValue] IS NULL OR [DiscountValue] >= 0) AND [AppliedAmount] >= 0 AND [EligibleSubtotal] >= 0 AND [RuleVersion] > 0");
                    table.ForeignKey(
                        name: "FK_OrderCoupons_CouponRedemptions_RedemptionId",
                        column: x => x.RedemptionId,
                        principalTable: "CouponRedemptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderCoupons_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderCoupons_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GuestOrderAccessTokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ScopeViolationCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuestOrderAccessTokens", x => x.Id);
                    table.CheckConstraint("CK_GuestOrderAccessTokens_ScopeViolationCount", "[ScopeViolationCount] >= 0");
                    table.ForeignKey(
                        name: "FK_GuestOrderAccessTokens_GuestOrderAccessRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "GuestOrderAccessRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GuestOrderAccessTokens_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentAttemptId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalEventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PayloadHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    PayloadSummaryJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ProcessingStatus = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false, defaultValue: "Received"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentEvents_PaymentAttempts_PaymentAttemptId",
                        column: x => x.PaymentAttemptId,
                        principalTable: "PaymentAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Refunds",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    ReturnRequestId = table.Column<long>(type: "bigint", nullable: true),
                    PaymentAttemptId = table.Column<long>(type: "bigint", nullable: false),
                    RefundNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false, defaultValue: "PendingReview"),
                    RequestedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    SucceededAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ReasonCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ExecutedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SucceededAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refunds", x => x.Id);
                    table.CheckConstraint("CK_Refunds_Amounts", "[RequestedAmount] > 0 AND ([ApprovedAmount] IS NULL OR ([ApprovedAmount] > 0 AND [ApprovedAmount] <= [RequestedAmount])) AND ([SucceededAmount] IS NULL OR ([SucceededAmount] > 0 AND [SucceededAmount] <= [ApprovedAmount]))");
                    table.ForeignKey(
                        name: "FK_Refunds_AspNetUsers_ApprovedBy",
                        column: x => x.ApprovedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Refunds_AspNetUsers_ExecutedByAdminUserId",
                        column: x => x.ExecutedByAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Refunds_AspNetUsers_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Refunds_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Refunds_PaymentAttempts_PaymentAttemptId",
                        column: x => x.PaymentAttemptId,
                        principalTable: "PaymentAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Refunds_ReturnRequests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnAssignmentHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnRequestId = table.Column<long>(type: "bigint", nullable: false),
                    FromAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ToAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Action = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnAssignmentHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnAssignmentHistories_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnAssignmentHistories_AspNetUsers_FromAdminUserId",
                        column: x => x.FromAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnAssignmentHistories_AspNetUsers_ToAdminUserId",
                        column: x => x.ToAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnAssignmentHistories_ReturnRequests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnAttachments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnRequestId = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Extension = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    MimeType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ScanStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ScannedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    RetentionUntilUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LegalHold = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnAttachments", x => x.Id);
                    table.CheckConstraint("CK_ReturnAttachments_FileSize", "[FileSizeBytes] >= 1 AND [FileSizeBytes] <= 10485760");
                    table.ForeignKey(
                        name: "FK_ReturnAttachments_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnAttachments_ReturnRequests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnShipments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnRequestId = table.Column<long>(type: "bigint", nullable: false),
                    ShipmentNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Method = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    CarrierCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    TrackingNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    RecipientPhone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    AddressLine = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StoreCode = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    StoreName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    ScheduledPickupAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ShippedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnShipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnShipments_ReturnRequests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnStatusHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnRequestId = table.Column<long>(type: "bigint", nullable: false),
                    FromStatus = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: true),
                    ToStatus = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnStatusHistories_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnStatusHistories_ReturnRequests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentStatusHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentId = table.Column<long>(type: "bigint", nullable: false),
                    FromStatus = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: true),
                    ToStatus = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    ExternalEventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentStatusHistories_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentStatusHistories_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportAssignmentHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupportTicketId = table.Column<long>(type: "bigint", nullable: false),
                    FromAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ToAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Action = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportAssignmentHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportAssignmentHistories_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportAssignmentHistories_AspNetUsers_FromAdminUserId",
                        column: x => x.FromAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportAssignmentHistories_AspNetUsers_ToAdminUserId",
                        column: x => x.ToAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportAssignmentHistories_SupportTickets_SupportTicketId",
                        column: x => x.SupportTicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupportTicketId = table.Column<long>(type: "bigint", nullable: false),
                    SenderType = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    SenderUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    IsInternal = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AiGenerated = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ReplyToMessageId = table.Column<long>(type: "bigint", nullable: true),
                    Language = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValue: "zh-TW"),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportMessages", x => x.Id);
                    table.CheckConstraint("CK_SupportMessages_InternalSender", "[IsInternal] = 0 OR [SenderType] = 'Admin'");
                    table.ForeignKey(
                        name: "FK_SupportMessages_AspNetUsers_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportMessages_SupportMessages_ReplyToMessageId",
                        column: x => x.ReplyToMessageId,
                        principalTable: "SupportMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportMessages_SupportTickets_SupportTicketId",
                        column: x => x.SupportTicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportSlaEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupportTicketId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    TargetType = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportSlaEvents", x => x.Id);
                    table.CheckConstraint("CK_SupportSlaEvents_Duration", "[DurationSeconds] IS NULL OR [DurationSeconds] >= 0");
                    table.CheckConstraint("CK_SupportSlaEvents_MetadataJson", "[MetadataJson] IS NULL OR ISJSON([MetadataJson]) = 1");
                    table.ForeignKey(
                        name: "FK_SupportSlaEvents_SupportTickets_SupportTicketId",
                        column: x => x.SupportTicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportStatusHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupportTicketId = table.Column<long>(type: "bigint", nullable: false),
                    FromStatus = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    ToStatus = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportStatusHistories_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportStatusHistories_SupportTickets_SupportTicketId",
                        column: x => x.SupportTicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryMovements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SkuId = table.Column<long>(type: "bigint", nullable: false),
                    ReservationId = table.Column<long>(type: "bigint", nullable: true),
                    MovementType = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    OnHandDelta = table.Column<int>(type: "int", nullable: false),
                    ReservedDelta = table.Column<int>(type: "int", nullable: false),
                    BeforeOnHand = table.Column<int>(type: "int", nullable: false),
                    AfterOnHand = table.Column<int>(type: "int", nullable: false),
                    BeforeReserved = table.Column<int>(type: "int", nullable: false),
                    AfterReserved = table.Column<int>(type: "int", nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ReferenceType = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ReferencePublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovements", x => x.Id);
                    table.CheckConstraint("CK_InventoryMovements_OnHand", "[BeforeOnHand] + [OnHandDelta] = [AfterOnHand]");
                    table.CheckConstraint("CK_InventoryMovements_Reserved", "[BeforeReserved] + [ReservedDelta] = [AfterReserved]");
                    table.ForeignKey(
                        name: "FK_InventoryMovements_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_InventoryReservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "InventoryReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_Skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "Skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductReviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrderItemId = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    Rating = table.Column<byte>(type: "tinyint", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false, defaultValue: "Draft"),
                    ReviewedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductReviews", x => x.Id);
                    table.CheckConstraint("CK_ProductReviews_Rating", "[Rating] >= 1 AND [Rating] <= 5");
                    table.CheckConstraint("CK_ProductReviews_RejectionReason", "[Status] <> 'Rejected' OR [RejectionReason] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_ProductReviews_AspNetUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductReviews_AspNetUsers_ReviewedByAdminUserId",
                        column: x => x.ReviewedByAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductReviews_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductReviews_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnRequestId = table.Column<long>(type: "bigint", nullable: false),
                    OrderItemId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    RequestedRefund = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    InspectionStatus = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    RestockDisposition = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnItems", x => x.Id);
                    table.CheckConstraint("CK_ReturnItems_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_ReturnItems_RequestedRefund", "[RequestedRefund] >= 0");
                    table.ForeignKey(
                        name: "FK_ReturnItems_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnItems_ReturnRequests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SimulatedInvoiceItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SimulatedInvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    OrderItemId = table.Column<long>(type: "bigint", nullable: true),
                    ProductNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SkuCodeSnapshot = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulatedInvoiceItems", x => x.Id);
                    table.CheckConstraint("CK_SimulatedInvoiceItems_Amounts", "[UnitPrice] >= 0 AND [DiscountAmount] >= 0 AND [NetAmount] >= 0 AND [TaxAmount] >= 0 AND [GrossAmount] = [NetAmount] + [TaxAmount]");
                    table.CheckConstraint("CK_SimulatedInvoiceItems_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_SimulatedInvoiceItems_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SimulatedInvoiceItems_SimulatedInvoices_SimulatedInvoiceId",
                        column: x => x.SimulatedInvoiceId,
                        principalTable: "SimulatedInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefundAllocations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RefundId = table.Column<long>(type: "bigint", nullable: false),
                    OrderItemId = table.Column<long>(type: "bigint", nullable: true),
                    AllocationType = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OriginalDiscountAllocation = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundAllocations", x => x.Id);
                    table.CheckConstraint("CK_RefundAllocations_Amounts", "[Amount] > 0 AND [OriginalDiscountAllocation] >= 0");
                    table.ForeignKey(
                        name: "FK_RefundAllocations_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RefundAllocations_Refunds_RefundId",
                        column: x => x.RefundId,
                        principalTable: "Refunds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SimulatedInvoiceAllowances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SimulatedInvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    RefundId = table.Column<long>(type: "bigint", nullable: false),
                    AllowanceNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulatedInvoiceAllowances", x => x.Id);
                    table.CheckConstraint("CK_SimulatedInvoiceAllowances_Amounts", "[NetAmount] >= 0 AND [TaxAmount] >= 0 AND [Amount] > 0 AND [Amount] = [NetAmount] + [TaxAmount]");
                    table.ForeignKey(
                        name: "FK_SimulatedInvoiceAllowances_Refunds_RefundId",
                        column: x => x.RefundId,
                        principalTable: "Refunds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SimulatedInvoiceAllowances_SimulatedInvoices_SimulatedInvoiceId",
                        column: x => x.SimulatedInvoiceId,
                        principalTable: "SimulatedInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnShipmentEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnShipmentId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalEventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Source = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    EventType = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    EventCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PayloadHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    PayloadSummaryJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnShipmentEvents", x => x.Id);
                    table.CheckConstraint("CK_ReturnShipmentEvents_PayloadJson", "[PayloadSummaryJson] IS NULL OR ISJSON([PayloadSummaryJson]) = 1");
                    table.ForeignKey(
                        name: "FK_ReturnShipmentEvents_ReturnShipments_ReturnShipmentId",
                        column: x => x.ReturnShipmentId,
                        principalTable: "ReturnShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportAttachments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupportTicketId = table.Column<long>(type: "bigint", nullable: false),
                    SupportMessageId = table.Column<long>(type: "bigint", nullable: true),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Extension = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    MimeType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ScanStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ScannedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    RetentionUntilUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LegalHold = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportAttachments", x => x.Id);
                    table.CheckConstraint("CK_SupportAttachments_FileSize", "[FileSizeBytes] >= 1 AND [FileSizeBytes] <= 10485760");
                    table.ForeignKey(
                        name: "FK_SupportAttachments_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportAttachments_SupportMessages_SupportMessageId",
                        column: x => x.SupportMessageId,
                        principalTable: "SupportMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportAttachments_SupportTickets_SupportTicketId",
                        column: x => x.SupportTicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportSummaries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupportTicketId = table.Column<long>(type: "bigint", nullable: false),
                    SourceLastMessageId = table.Column<long>(type: "bigint", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PromptVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false, defaultValue: "Pending"),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportSummaries_SupportMessages_SourceLastMessageId",
                        column: x => x.SourceLastMessageId,
                        principalTable: "SupportMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportSummaries_SupportTickets_SupportTicketId",
                        column: x => x.SupportTicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryReconciliationCases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SkuId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    ExpectedOnHand = table.Column<int>(type: "int", nullable: false),
                    ActualOnHand = table.Column<int>(type: "int", nullable: false),
                    ExpectedReserved = table.Column<int>(type: "int", nullable: false),
                    ActualReserved = table.Column<int>(type: "int", nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    AcknowledgedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ResolvedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ResolutionMovementId = table.Column<long>(type: "bigint", nullable: true),
                    ResolutionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryReconciliationCases", x => x.Id);
                    table.CheckConstraint("CK_InventoryReconciliationCases_Quantities", "[ExpectedOnHand] >= 0 AND [ActualOnHand] >= 0 AND [ExpectedReserved] >= 0 AND [ActualReserved] >= 0");
                    table.CheckConstraint("CK_InventoryReconciliationCases_Resolution", "([Status] = 'Resolved' AND [ResolutionMovementId] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL) OR ([Status] = 'Dismissed' AND [ResolutionMovementId] IS NULL AND [ResolutionReason] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL) OR [Status] IN ('Open','Acknowledged')");
                    table.ForeignKey(
                        name: "FK_InventoryReconciliationCases_AspNetUsers_AcknowledgedBy",
                        column: x => x.AcknowledgedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReconciliationCases_AspNetUsers_ResolvedByAdminUserId",
                        column: x => x.ResolvedByAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReconciliationCases_InventoryMovements_ResolutionMovementId",
                        column: x => x.ResolutionMovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReconciliationCases_Skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "Skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductReviewRevisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductReviewId = table.Column<long>(type: "bigint", nullable: false),
                    Rating = table.Column<byte>(type: "tinyint", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    SupersededAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    SupersededReason = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductReviewRevisions", x => x.Id);
                    table.CheckConstraint("CK_ProductReviewRevisions_Rating", "[Rating] >= 1 AND [Rating] <= 5");
                    table.ForeignKey(
                        name: "FK_ProductReviewRevisions_ProductReviews_ProductReviewId",
                        column: x => x.ProductReviewId,
                        principalTable: "ProductReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReviewImages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductReviewId = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MediaType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ScanStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Pending"),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewImages", x => x.Id);
                    table.CheckConstraint("CK_ReviewImages_FileSize", "[FileSizeBytes] >= 1 AND [FileSizeBytes] <= 5242880");
                    table.ForeignKey(
                        name: "FK_ReviewImages_ProductReviews_ProductReviewId",
                        column: x => x.ProductReviewId,
                        principalTable: "ProductReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnInspections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnItemId = table.Column<long>(type: "bigint", nullable: false),
                    Result = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    ConditionCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    InspectedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    InspectedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnInspections_AspNetUsers_InspectedByAdminUserId",
                        column: x => x.InspectedByAdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnInspections_ReturnItems_ReturnItemId",
                        column: x => x.ReturnItemId,
                        principalTable: "ReturnItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SimulatedInvoiceAllowanceItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AllowanceId = table.Column<long>(type: "bigint", nullable: false),
                    SimulatedInvoiceItemId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulatedInvoiceAllowanceItems", x => x.Id);
                    table.CheckConstraint("CK_SimulatedInvoiceAllowanceItems_Amounts", "[NetAmount] >= 0 AND [TaxAmount] >= 0 AND [GrossAmount] > 0 AND [GrossAmount] = [NetAmount] + [TaxAmount]");
                    table.CheckConstraint("CK_SimulatedInvoiceAllowanceItems_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_SimulatedInvoiceAllowanceItems_SimulatedInvoiceAllowances_AllowanceId",
                        column: x => x.AllowanceId,
                        principalTable: "SimulatedInvoiceAllowances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SimulatedInvoiceAllowanceItems_SimulatedInvoiceItems_SimulatedInvoiceItemId",
                        column: x => x.SimulatedInvoiceItemId,
                        principalTable: "SimulatedInvoiceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminProfiles_IsActive",
                table: "AdminProfiles",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "UX_AdminProfiles_EmployeeCode",
                table: "AdminProfiles",
                column: "EmployeeCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AdminProfiles_PublicId",
                table: "AdminProfiles",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_AspNetUsers_NormalizedEmail",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true,
                filter: "[NormalizedEmail] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_AspNetUsers_PublicId",
                table: "AspNetUsers",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyJobs_AssignedAdminUserId",
                table: "AssemblyJobs",
                column: "AssignedAdminUserId");

            migrationBuilder.CreateIndex(
                name: "UX_AssemblyJobs_OrderId_AssemblyGroupKey",
                table: "AssemblyJobs",
                columns: new[] { "OrderId", "AssemblyGroupKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AssemblyJobs_PublicId",
                table: "AssemblyJobs",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyJobStatusHistories_ActorUserId",
                table: "AssemblyJobStatusHistories",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyJobStatusHistories_AssemblyJobId_OccurredAtUtc",
                table: "AssemblyJobStatusHistories",
                columns: new[] { "AssemblyJobId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_AssemblyJobStatusHistories_PublicId",
                table: "AssemblyJobStatusHistories",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Brands_Code",
                table: "Brands",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Brands_PublicId",
                table: "Brands",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BrandTranslations_ReviewedByAdminUserId",
                table: "BrandTranslations",
                column: "ReviewedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "UX_BrandTranslations_BrandId_Locale",
                table: "BrandTranslations",
                columns: new[] { "BrandId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuildListItems_SkuId",
                table: "BuildListItems",
                column: "SkuId");

            migrationBuilder.CreateIndex(
                name: "UX_BuildListItems_BuildListId_SkuId",
                table: "BuildListItems",
                columns: new[] { "BuildListId", "SkuId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_BuildListItems_PublicId",
                table: "BuildListItems",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuildLists_OwnerUserId_UpdatedAtUtc",
                table: "BuildLists",
                columns: new[] { "OwnerUserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_BuildLists_PublicId",
                table: "BuildLists",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuildShareTokens_BuildListId",
                table: "BuildShareTokens",
                column: "BuildListId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildShareTokens_ExpiresAtUtc",
                table: "BuildShareTokens",
                column: "ExpiresAtUtc",
                filter: "[ExpiresAtUtc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_BuildShareTokens_PublicId",
                table: "BuildShareTokens",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_BuildShareTokens_TokenHash",
                table: "BuildShareTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_SkuId",
                table: "CartItems",
                column: "SkuId");

            migrationBuilder.CreateIndex(
                name: "UX_CartItems_CartId_SkuId_AssemblyGroupKey",
                table: "CartItems",
                columns: new[] { "CartId", "SkuId", "AssemblyGroupKey" },
                unique: true,
                filter: "[AssemblyGroupKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CartItems_PublicId",
                table: "CartItems",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Carts_GuestCartKeyHash_Active",
                table: "Carts",
                column: "GuestCartKeyHash",
                unique: true,
                filter: "[GuestCartKeyHash] IS NOT NULL AND [Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "UX_Carts_OwnerUserId_Active",
                table: "Carts",
                column: "OwnerUserId",
                unique: true,
                filter: "[OwnerUserId] IS NOT NULL AND [Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "UX_Carts_PublicId",
                table: "Carts",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentCategoryId",
                table: "Categories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "UX_Categories_Code",
                table: "Categories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Categories_PublicId",
                table: "Categories",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Categories_Slug",
                table: "Categories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategoryTranslations_ReviewedByAdminUserId",
                table: "CategoryTranslations",
                column: "ReviewedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "UX_CategoryTranslations_CategoryId_Locale",
                table: "CategoryTranslations",
                columns: new[] { "CategoryId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompatibilityCheckResults_RunId_Severity",
                table: "CompatibilityCheckResults",
                columns: new[] { "CompatibilityCheckRunId", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_CompatibilityCheckRuns_BuildListId_EvaluatedAtUtc",
                table: "CompatibilityCheckRuns",
                columns: new[] { "BuildListId", "EvaluatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_CompatibilityCheckRuns_PublicId",
                table: "CompatibilityCheckRuns",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompatibilityRuleSettings_ChangedByAdminUserId",
                table: "CompatibilityRuleSettings",
                column: "ChangedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "UX_CompatibilityRuleSettings_PublicId",
                table: "CompatibilityRuleSettings",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CompatibilityRuleSettings_RuleCode_SettingCode_SettingsVersion",
                table: "CompatibilityRuleSettings",
                columns: new[] { "RuleCode", "SettingCode", "SettingsVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConvenienceStores_City_District_IsActive",
                table: "ConvenienceStores",
                columns: new[] { "City", "District", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_ConvenienceStores_ProviderCode_StoreCode",
                table: "ConvenienceStores",
                columns: new[] { "ProviderCode", "StoreCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ConvenienceStores_PublicId",
                table: "ConvenienceStores",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CouponCategories_CategoryId",
                table: "CouponCategories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponExcludedProducts_ProductId",
                table: "CouponExcludedProducts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponProducts_ProductId",
                table: "CouponProducts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_CouponId_GuestUsageKeyHash_Status",
                table: "CouponRedemptions",
                columns: new[] { "CouponId", "GuestUsageKeyHash", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_CouponId_MemberUserId_Status",
                table: "CouponRedemptions",
                columns: new[] { "CouponId", "MemberUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_CouponId_Status",
                table: "CouponRedemptions",
                columns: new[] { "CouponId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_MemberUserId",
                table: "CouponRedemptions",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_OrderId",
                table: "CouponRedemptions",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "UX_CouponRedemptions_CouponId_OrderId",
                table: "CouponRedemptions",
                columns: new[] { "CouponId", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CouponRedemptions_PublicId",
                table: "CouponRedemptions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_EndsAtUtc",
                table: "Coupons",
                column: "EndsAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_MemberOnly",
                table: "Coupons",
                column: "MemberOnly");

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_NameZhTw",
                table: "Coupons",
                column: "NameZhTw");

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_StartsAtUtc",
                table: "Coupons",
                column: "StartsAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_Coupons_Code",
                table: "Coupons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Coupons_PublicId",
                table: "Coupons",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_ProductId",
                table: "Favorites",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_GuestOrderAccessRequests_EmailKeyHash_CreatedAtUtc",
                table: "GuestOrderAccessRequests",
                columns: new[] { "EmailKeyHash", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GuestOrderAccessRequests_ExpiresAtUtc",
                table: "GuestOrderAccessRequests",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GuestOrderAccessRequests_OrderId",
                table: "GuestOrderAccessRequests",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_GuestOrderAccessRequests_OrderLookupKeyHash_CreatedAtUtc",
                table: "GuestOrderAccessRequests",
                columns: new[] { "OrderLookupKeyHash", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GuestOrderAccessRequests_RequesterIpHash_CreatedAtUtc",
                table: "GuestOrderAccessRequests",
                columns: new[] { "RequesterIpHash", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_GuestOrderAccessRequests_PublicId",
                table: "GuestOrderAccessRequests",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuestOrderAccessTokens_ExpiresAtUtc",
                table: "GuestOrderAccessTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GuestOrderAccessTokens_OrderId",
                table: "GuestOrderAccessTokens",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "UX_GuestOrderAccessTokens_PublicId",
                table: "GuestOrderAccessTokens",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_GuestOrderAccessTokens_RequestId",
                table: "GuestOrderAccessTokens",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_GuestOrderAccessTokens_TokenHash",
                table: "GuestOrderAccessTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_Status_ExpiresAtUtc",
                table: "ImportBatches",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ImportBatches_CreatedByAdminUserId_ImportType",
                table: "ImportBatches",
                columns: new[] { "CreatedByAdminUserId", "ImportType" },
                unique: true,
                filter: "[Status] IN ('Uploaded','Validating','Ready','Committing')");

            migrationBuilder.CreateIndex(
                name: "UX_ImportBatches_PublicId",
                table: "ImportBatches",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ImportRows_ImportBatchId_Dataset_ImportKey",
                table: "ImportRows",
                columns: new[] { "ImportBatchId", "Dataset", "ImportKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ImportRows_ImportBatchId_Dataset_SourceRowNumber",
                table: "ImportRows",
                columns: new[] { "ImportBatchId", "Dataset", "SourceRowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_AvailableQuantity",
                table: "InventoryBalances",
                column: "AvailableQuantity");

            migrationBuilder.CreateIndex(
                name: "UX_InventoryBalances_PublicId",
                table: "InventoryBalances",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_InventoryBalances_SkuId",
                table: "InventoryBalances",
                column: "SkuId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_ActorUserId",
                table: "InventoryMovements",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_ReservationId",
                table: "InventoryMovements",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SkuId_OccurredAtUtc",
                table: "InventoryMovements",
                columns: new[] { "SkuId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_InventoryMovements_PublicId",
                table: "InventoryMovements",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReconciliationCases_AcknowledgedBy",
                table: "InventoryReconciliationCases",
                column: "AcknowledgedBy");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReconciliationCases_ResolutionMovementId",
                table: "InventoryReconciliationCases",
                column: "ResolutionMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReconciliationCases_ResolvedByAdminUserId",
                table: "InventoryReconciliationCases",
                column: "ResolvedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReconciliationCases_Status_DetectedAtUtc",
                table: "InventoryReconciliationCases",
                columns: new[] { "Status", "DetectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_InventoryReconciliationCases_PublicId",
                table: "InventoryReconciliationCases",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_InventoryReconciliationCases_SkuId_Open",
                table: "InventoryReconciliationCases",
                column: "SkuId",
                unique: true,
                filter: "[Status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_OrderId_SkuId",
                table: "InventoryReservations",
                columns: new[] { "OrderId", "SkuId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_SkuId",
                table: "InventoryReservations",
                column: "SkuId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_Status_ExpiresAtUtc",
                table: "InventoryReservations",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_InventoryReservations_PublicId",
                table: "InventoryReservations",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_MeasurementUnits_Code",
                table: "MeasurementUnits",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_MeasurementUnits_PublicId",
                table: "MeasurementUnits",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemberAddresses_MemberUserId",
                table: "MemberAddresses",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "UX_MemberAddresses_MemberUserId_Default",
                table: "MemberAddresses",
                columns: new[] { "MemberUserId", "IsDefault" },
                unique: true,
                filter: "[DeletedAtUtc] IS NULL AND [IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_MemberAddresses_PublicId",
                table: "MemberAddresses",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_MemberProfiles_PublicId",
                table: "MemberProfiles",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderCoupons_CouponId",
                table: "OrderCoupons",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "UX_OrderCoupons_OrderId",
                table: "OrderCoupons",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_OrderCoupons_PublicId",
                table: "OrderCoupons",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_OrderCoupons_RedemptionId",
                table: "OrderCoupons",
                column: "RedemptionId",
                unique: true,
                filter: "[RedemptionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId_AssemblyGroupKey",
                table: "OrderItems",
                columns: new[] { "OrderId", "AssemblyGroupKey" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_SkuId",
                table: "OrderItems",
                column: "SkuId");

            migrationBuilder.CreateIndex(
                name: "UX_OrderItems_PublicId",
                table: "OrderItems",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompletedAtUtc",
                table: "Orders",
                column: "CompletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_MemberUserId_CreatedAtUtc",
                table: "Orders",
                columns: new[] { "MemberUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderStatus_PaymentDueAtUtc",
                table: "Orders",
                columns: new[] { "OrderStatus", "PaymentDueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ShippingProviderProfileVersionId",
                table: "Orders",
                column: "ShippingProviderProfileVersionId");

            migrationBuilder.CreateIndex(
                name: "UX_Orders_CheckoutIdempotencyKey",
                table: "Orders",
                column: "CheckoutIdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Orders_OrderNumber",
                table: "Orders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Orders_PublicId",
                table: "Orders",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistories_ActorUserId",
                table: "OrderStatusHistories",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistories_OrderId_OccurredAtUtc",
                table: "OrderStatusHistories",
                columns: new[] { "OrderId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_OrderStatusHistories_PublicId",
                table: "OrderStatusHistories",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackageLimitVersions_ProviderProfileId_Version",
                table: "PackageLimitVersions",
                columns: new[] { "ProviderProfileId", "Version" });

            migrationBuilder.CreateIndex(
                name: "UX_PackageLimitVersions_PublicId",
                table: "PackageLimitVersions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_FailureCode",
                table: "PaymentAttempts",
                column: "FailureCode");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_InstructionExpiresAtUtc",
                table: "PaymentAttempts",
                column: "InstructionExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_Method",
                table: "PaymentAttempts",
                column: "Method");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_OrderId_CreatedAtUtc",
                table: "PaymentAttempts",
                columns: new[] { "OrderId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_ProviderCode",
                table: "PaymentAttempts",
                column: "ProviderCode");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_Status",
                table: "PaymentAttempts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_PaymentAttempts_ExternalReference",
                table: "PaymentAttempts",
                column: "ExternalReference",
                unique: true,
                filter: "[ExternalReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_PaymentAttempts_IdempotencyKey",
                table: "PaymentAttempts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PaymentAttempts_PublicId",
                table: "PaymentAttempts",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_EventType",
                table: "PaymentEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_OccurredAt",
                table: "PaymentEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_PayloadHash",
                table: "PaymentEvents",
                column: "PayloadHash");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_PaymentAttemptId",
                table: "PaymentEvents",
                column: "PaymentAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_ProcessingStatus",
                table: "PaymentEvents",
                column: "ProcessingStatus");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_ReceivedAtUtc",
                table: "PaymentEvents",
                column: "ReceivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_PaymentEvents_ExternalEventId",
                table: "PaymentEvents",
                column: "ExternalEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PaymentEvents_PublicId",
                table: "PaymentEvents",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductId_Status_SortOrder",
                table: "ProductImages",
                columns: new[] { "ProductId", "Status", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_SkuId",
                table: "ProductImages",
                column: "SkuId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_Status_DeletedAtUtc",
                table: "ProductImages",
                columns: new[] { "Status", "DeletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ProductImages_PublicId",
                table: "ProductImages",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProductImages_StorageKey",
                table: "ProductImages",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviewRevisions_ProductReviewId_SupersededAtUtc",
                table: "ProductReviewRevisions",
                columns: new[] { "ProductReviewId", "SupersededAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_MemberUserId_CreatedAtUtc",
                table: "ProductReviews",
                columns: new[] { "MemberUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_ProductId_Status",
                table: "ProductReviews",
                columns: new[] { "ProductId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_ReviewedByAdminUserId",
                table: "ProductReviews",
                column: "ReviewedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ProductReviews_OrderItemId",
                table: "ProductReviews",
                column: "OrderItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProductReviews_PublicId",
                table: "ProductReviews",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_BrandId_Status",
                table: "Products",
                columns: new[] { "BrandId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId_Status",
                table: "Products",
                columns: new[] { "CategoryId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_Products_ProductCode",
                table: "Products",
                column: "ProductCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Products_PublicId",
                table: "Products",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductTags_TagId_ProductId",
                table: "ProductTags",
                columns: new[] { "TagId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductTranslations_ReviewedByAdminUserId",
                table: "ProductTranslations",
                column: "ReviewedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ProductTranslations_ProductId_Locale",
                table: "ProductTranslations",
                columns: new[] { "ProductId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefundAllocations_AllocationType",
                table: "RefundAllocations",
                column: "AllocationType");

            migrationBuilder.CreateIndex(
                name: "IX_RefundAllocations_OrderItemId",
                table: "RefundAllocations",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RefundAllocations_RefundId",
                table: "RefundAllocations",
                column: "RefundId");

            migrationBuilder.CreateIndex(
                name: "UX_RefundAllocations_PublicId",
                table: "RefundAllocations",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_ApprovedBy",
                table: "Refunds",
                column: "ApprovedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_ExecutedByAdminUserId",
                table: "Refunds",
                column: "ExecutedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_OrderId",
                table: "Refunds",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_PaymentAttemptId",
                table: "Refunds",
                column: "PaymentAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_ReasonCode",
                table: "Refunds",
                column: "ReasonCode");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_RequestedBy",
                table: "Refunds",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_ReturnRequestId",
                table: "Refunds",
                column: "ReturnRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_Status",
                table: "Refunds",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_Refunds_IdempotencyKey",
                table: "Refunds",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Refunds_PublicId",
                table: "Refunds",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Refunds_RefundNumber",
                table: "Refunds",
                column: "RefundNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportAssignmentHistories_ActorUserId",
                table: "ReportAssignmentHistories",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAssignmentHistories_FromAdminUserId",
                table: "ReportAssignmentHistories",
                column: "FromAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAssignmentHistories_ReportCaseId_OccurredAtUtc_Id",
                table: "ReportAssignmentHistories",
                columns: new[] { "ReportCaseId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportAssignmentHistories_ToAdminUserId",
                table: "ReportAssignmentHistories",
                column: "ToAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAttachments_ReportCaseId",
                table: "ReportAttachments",
                column: "ReportCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAttachments_UploadedByUserId",
                table: "ReportAttachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ReportAttachments_PublicId",
                table: "ReportAttachments",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReportAttachments_StorageKey",
                table: "ReportAttachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportCases_AssigneeAdminUserId",
                table: "ReportCases",
                column: "AssigneeAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportCases_ReporterUserId",
                table: "ReportCases",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportCases_Status_AssigneeAdminUserId_LastActivityAtUtc",
                table: "ReportCases",
                columns: new[] { "Status", "AssigneeAdminUserId", "LastActivityAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportCases_TargetType_TargetPublicId_Status",
                table: "ReportCases",
                columns: new[] { "TargetType", "TargetPublicId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_ReportCases_OpenCaseKeyHash",
                table: "ReportCases",
                column: "OpenCaseKeyHash",
                unique: true,
                filter: "[OpenCaseKeyHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ReportCases_PublicId",
                table: "ReportCases",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReportCases_ReportNumber",
                table: "ReportCases",
                column: "ReportNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportStatusHistories_ActorUserId",
                table: "ReportStatusHistories",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportStatusHistories_ReportCaseId_OccurredAtUtc_Id",
                table: "ReportStatusHistories",
                columns: new[] { "ReportCaseId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAssignmentHistories_ActorUserId",
                table: "ReturnAssignmentHistories",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAssignmentHistories_FromAdminUserId",
                table: "ReturnAssignmentHistories",
                column: "FromAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAssignmentHistories_ReturnRequestId_OccurredAtUtc_Id",
                table: "ReturnAssignmentHistories",
                columns: new[] { "ReturnRequestId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAssignmentHistories_ToAdminUserId",
                table: "ReturnAssignmentHistories",
                column: "ToAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAttachments_ReturnRequestId",
                table: "ReturnAttachments",
                column: "ReturnRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAttachments_UploadedByUserId",
                table: "ReturnAttachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ReturnAttachments_PublicId",
                table: "ReturnAttachments",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReturnAttachments_StorageKey",
                table: "ReturnAttachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspections_InspectedByAdminUserId",
                table: "ReturnInspections",
                column: "InspectedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspections_ReturnItemId_InspectedAtUtc",
                table: "ReturnInspections",
                columns: new[] { "ReturnItemId", "InspectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ReturnInspections_PublicId",
                table: "ReturnInspections",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnItems_OrderItemId",
                table: "ReturnItems",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "UX_ReturnItems_PublicId",
                table: "ReturnItems",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReturnItems_ReturnRequestId_OrderItemId",
                table: "ReturnItems",
                columns: new[] { "ReturnRequestId", "OrderItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_AssigneeAdminUserId",
                table: "ReturnRequests",
                column: "AssigneeAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_OrderId_Status",
                table: "ReturnRequests",
                columns: new[] { "OrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_RequesterUserId",
                table: "ReturnRequests",
                column: "RequesterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_ReviewedByAdminUserId",
                table: "ReturnRequests",
                column: "ReviewedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_Status_Priority_AssigneeAdminUserId_UpdatedAtUtc",
                table: "ReturnRequests",
                columns: new[] { "Status", "Priority", "AssigneeAdminUserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ReturnRequests_PublicId",
                table: "ReturnRequests",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReturnRequests_ReturnNumber",
                table: "ReturnRequests",
                column: "ReturnNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnShipmentEvents_ReturnShipmentId_OccurredAtUtc",
                table: "ReturnShipmentEvents",
                columns: new[] { "ReturnShipmentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ReturnShipmentEvents_Source_ExternalEventId",
                table: "ReturnShipmentEvents",
                columns: new[] { "Source", "ExternalEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnShipments_CarrierCode_TrackingNumber",
                table: "ReturnShipments",
                columns: new[] { "CarrierCode", "TrackingNumber" });

            migrationBuilder.CreateIndex(
                name: "UX_ReturnShipments_PublicId",
                table: "ReturnShipments",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReturnShipments_ReturnRequestId",
                table: "ReturnShipments",
                column: "ReturnRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReturnShipments_ShipmentNumber",
                table: "ReturnShipments",
                column: "ShipmentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnStatusHistories_ActorUserId",
                table: "ReturnStatusHistories",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnStatusHistories_ReturnRequestId_OccurredAtUtc_Id",
                table: "ReturnStatusHistories",
                columns: new[] { "ReturnRequestId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewImages_ProductReviewId",
                table: "ReviewImages",
                column: "ProductReviewId");

            migrationBuilder.CreateIndex(
                name: "UX_ReviewImages_StorageKey",
                table: "ReviewImages",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalePrices_CreatedByAdminUserId",
                table: "SalePrices",
                column: "CreatedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SalePrices_SkuId_StartsAtUtc_EndsAtUtc",
                table: "SalePrices",
                columns: new[] { "SkuId", "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_SalePrices_PublicId",
                table: "SalePrices",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ConvenienceStoreId",
                table: "Shipments",
                column: "ConvenienceStoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OrderId",
                table: "Shipments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ProviderProfileVersionId",
                table: "Shipments",
                column: "ProviderProfileVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ShippingMethodId",
                table: "Shipments",
                column: "ShippingMethodId");

            migrationBuilder.CreateIndex(
                name: "UX_Shipments_PublicId",
                table: "Shipments",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Shipments_ShipmentNumber",
                table: "Shipments",
                column: "ShipmentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentStatusHistories_ActorUserId",
                table: "ShipmentStatusHistories",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentStatusHistories_ShipmentId_OccurredAtUtc",
                table: "ShipmentStatusHistories",
                columns: new[] { "ShipmentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ShipmentStatusHistories_ExternalEventId",
                table: "ShipmentStatusHistories",
                column: "ExternalEventId",
                unique: true,
                filter: "[ExternalEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ShipmentStatusHistories_PublicId",
                table: "ShipmentStatusHistories",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ShippingMethods_Code",
                table: "ShippingMethods",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ShippingMethods_PublicId",
                table: "ShippingMethods",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProviderProfiles_ProviderCode_Published",
                table: "ShippingProviderProfiles",
                column: "ProviderCode",
                unique: true,
                filter: "[Status] = 'Published'");

            migrationBuilder.CreateIndex(
                name: "UX_ProviderProfiles_ProviderCode_Version",
                table: "ShippingProviderProfiles",
                columns: new[] { "ProviderCode", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ShippingProviderProfiles_PublicId",
                table: "ShippingProviderProfiles",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SimulatedInvoiceAllowanceItems_AllowanceId",
                table: "SimulatedInvoiceAllowanceItems",
                column: "AllowanceId");

            migrationBuilder.CreateIndex(
                name: "IX_SimulatedInvoiceAllowanceItems_SimulatedInvoiceItemId",
                table: "SimulatedInvoiceAllowanceItems",
                column: "SimulatedInvoiceItemId");

            migrationBuilder.CreateIndex(
                name: "UX_SimulatedInvoiceAllowanceItems_PublicId",
                table: "SimulatedInvoiceAllowanceItems",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SimulatedInvoiceAllowances_IssuedAtUtc",
                table: "SimulatedInvoiceAllowances",
                column: "IssuedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SimulatedInvoiceAllowances_SimulatedInvoiceId",
                table: "SimulatedInvoiceAllowances",
                column: "SimulatedInvoiceId");

            migrationBuilder.CreateIndex(
                name: "UX_SimulatedInvoiceAllowances_AllowanceNumber",
                table: "SimulatedInvoiceAllowances",
                column: "AllowanceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SimulatedInvoiceAllowances_PublicId",
                table: "SimulatedInvoiceAllowances",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SimulatedInvoiceAllowances_RefundId",
                table: "SimulatedInvoiceAllowances",
                column: "RefundId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SimulatedInvoiceItems_OrderItemId",
                table: "SimulatedInvoiceItems",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SimulatedInvoiceItems_SimulatedInvoiceId",
                table: "SimulatedInvoiceItems",
                column: "SimulatedInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SimulatedInvoiceItems_SkuCodeSnapshot",
                table: "SimulatedInvoiceItems",
                column: "SkuCodeSnapshot");

            migrationBuilder.CreateIndex(
                name: "UX_SimulatedInvoiceItems_PublicId",
                table: "SimulatedInvoiceItems",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SimulatedInvoices_BuyerType",
                table: "SimulatedInvoices",
                column: "BuyerType");

            migrationBuilder.CreateIndex(
                name: "IX_SimulatedInvoices_CompanyTaxId",
                table: "SimulatedInvoices",
                column: "CompanyTaxId");

            migrationBuilder.CreateIndex(
                name: "IX_SimulatedInvoices_DemoMarker",
                table: "SimulatedInvoices",
                column: "DemoMarker");

            migrationBuilder.CreateIndex(
                name: "IX_SimulatedInvoices_IssuedAtUtc",
                table: "SimulatedInvoices",
                column: "IssuedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SimulatedInvoices_Status",
                table: "SimulatedInvoices",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_SimulatedInvoices_InvoiceNumber",
                table: "SimulatedInvoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SimulatedInvoices_OrderId",
                table: "SimulatedInvoices",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SimulatedInvoices_PublicId",
                table: "SimulatedInvoices",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Skus_ProductId_Status",
                table: "Skus",
                columns: new[] { "ProductId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_Skus_ProductId_IsDefault",
                table: "Skus",
                columns: new[] { "ProductId", "IsDefault" },
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Skus_PublicId",
                table: "Skus",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Skus_SkuCode",
                table: "Skus",
                column: "SkuCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SkuSpecificationValues_DefinitionId_DecimalValue",
                table: "SkuSpecificationValues",
                columns: new[] { "SpecificationDefinitionId", "DecimalValue" });

            migrationBuilder.CreateIndex(
                name: "IX_SkuSpecificationValues_DefinitionId_OptionId",
                table: "SkuSpecificationValues",
                columns: new[] { "SpecificationDefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SkuSpecificationValues_OptionId",
                table: "SkuSpecificationValues",
                column: "OptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SkuSpecificationValues_SpecificationSourceId",
                table: "SkuSpecificationValues",
                column: "SpecificationSourceId");

            migrationBuilder.CreateIndex(
                name: "UX_SkuSpecificationValues_SkuId_SpecificationDefinitionId",
                table: "SkuSpecificationValues",
                columns: new[] { "SkuId", "SpecificationDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SkuTranslations_ReviewedByAdminUserId",
                table: "SkuTranslations",
                column: "ReviewedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "UX_SkuTranslations_SkuId_Locale",
                table: "SkuTranslations",
                columns: new[] { "SkuId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpecificationDefinitions_MeasurementUnitId",
                table: "SpecificationDefinitions",
                column: "MeasurementUnitId");

            migrationBuilder.CreateIndex(
                name: "UX_SpecificationDefinitions_CategoryId_SemanticKey",
                table: "SpecificationDefinitions",
                columns: new[] { "CategoryId", "SemanticKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SpecificationDefinitions_PublicId",
                table: "SpecificationDefinitions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpecificationDefinitionTranslations_ReviewedByAdminUserId",
                table: "SpecificationDefinitionTranslations",
                column: "ReviewedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "UX_SpecDefTranslations_DefId_Locale",
                table: "SpecificationDefinitionTranslations",
                columns: new[] { "SpecificationDefinitionId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SpecificationOptions_DefinitionId_Code",
                table: "SpecificationOptions",
                columns: new[] { "SpecificationDefinitionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SpecificationOptions_PublicId",
                table: "SpecificationOptions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpecificationOptionTranslations_ReviewedByAdminUserId",
                table: "SpecificationOptionTranslations",
                column: "ReviewedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "UX_SpecOptTranslations_OptId_Locale",
                table: "SpecificationOptionTranslations",
                columns: new[] { "SpecificationOptionId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpecificationSources_ReviewedByAdminUserId",
                table: "SpecificationSources",
                column: "ReviewedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SpecificationSources_Url_Provider_Version",
                table: "SpecificationSources",
                columns: new[] { "SourceUrl", "ProviderName", "SourceVersion" });

            migrationBuilder.CreateIndex(
                name: "UX_SpecificationSources_PublicId",
                table: "SpecificationSources",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportAssignmentHistories_ActorUserId",
                table: "SupportAssignmentHistories",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAssignmentHistories_FromAdminUserId",
                table: "SupportAssignmentHistories",
                column: "FromAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAssignmentHistories_SupportTicketId_OccurredAtUtc_Id",
                table: "SupportAssignmentHistories",
                columns: new[] { "SupportTicketId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportAssignmentHistories_ToAdminUserId",
                table: "SupportAssignmentHistories",
                column: "ToAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_SupportMessageId",
                table: "SupportAttachments",
                column: "SupportMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_SupportTicketId",
                table: "SupportAttachments",
                column: "SupportTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_UploadedByUserId",
                table: "SupportAttachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_SupportAttachments_PublicId",
                table: "SupportAttachments",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SupportAttachments_StorageKey",
                table: "SupportAttachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessages_ReplyToMessageId",
                table: "SupportMessages",
                column: "ReplyToMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessages_SenderUserId",
                table: "SupportMessages",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessages_SupportTicketId_SentAtUtc_Id",
                table: "SupportMessages",
                columns: new[] { "SupportTicketId", "SentAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_SupportMessages_PublicId",
                table: "SupportMessages",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportSlaEvents_SupportTicketId_OccurredAtUtc_Id",
                table: "SupportSlaEvents",
                columns: new[] { "SupportTicketId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportStatusHistories_ActorUserId",
                table: "SupportStatusHistories",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportStatusHistories_SupportTicketId_OccurredAtUtc_Id",
                table: "SupportStatusHistories",
                columns: new[] { "SupportTicketId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportSummaries_SourceLastMessageId",
                table: "SupportSummaries",
                column: "SourceLastMessageId");

            migrationBuilder.CreateIndex(
                name: "UX_SupportSummaries_PublicId",
                table: "SupportSummaries",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SupportSummaries_SupportTicketId_SourceLastMessageId",
                table: "SupportSummaries",
                columns: new[] { "SupportTicketId", "SourceLastMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_AssigneeAdminUserId",
                table: "SupportTickets",
                column: "AssigneeAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_MemberUserId_CreatedAtUtc",
                table: "SupportTickets",
                columns: new[] { "MemberUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_OrderId",
                table: "SupportTickets",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_Status_AssigneeAdminUserId_LastActivityAtUtc",
                table: "SupportTickets",
                columns: new[] { "Status", "AssigneeAdminUserId", "LastActivityAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_Status_FirstResponseDueAtUtc",
                table: "SupportTickets",
                columns: new[] { "Status", "FirstResponseDueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_Status_ResolutionDueAtUtc",
                table: "SupportTickets",
                columns: new[] { "Status", "ResolutionDueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_SupportTickets_PublicId",
                table: "SupportTickets",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SupportTickets_TicketNumber",
                table: "SupportTickets",
                column: "TicketNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Tags_Code",
                table: "Tags",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Tags_PublicId",
                table: "Tags",
                column: "PublicId",
                unique: true);

            migrationBuilder.Sql(
                """
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS [dbo].[vw_CaseWorkbench];");

            migrationBuilder.DropTable(
                name: "AdminProfiles");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AssemblyJobStatusHistories");

            migrationBuilder.DropTable(
                name: "BrandTranslations");

            migrationBuilder.DropTable(
                name: "BuildListItems");

            migrationBuilder.DropTable(
                name: "BuildShareTokens");

            migrationBuilder.DropTable(
                name: "CartItems");

            migrationBuilder.DropTable(
                name: "CategoryTranslations");

            migrationBuilder.DropTable(
                name: "CompatibilityCheckResults");

            migrationBuilder.DropTable(
                name: "CompatibilityRuleSettings");

            migrationBuilder.DropTable(
                name: "CouponCategories");

            migrationBuilder.DropTable(
                name: "CouponExcludedProducts");

            migrationBuilder.DropTable(
                name: "CouponProducts");

            migrationBuilder.DropTable(
                name: "Favorites");

            migrationBuilder.DropTable(
                name: "GuestOrderAccessTokens");

            migrationBuilder.DropTable(
                name: "ImportRows");

            migrationBuilder.DropTable(
                name: "InventoryBalances");

            migrationBuilder.DropTable(
                name: "InventoryReconciliationCases");

            migrationBuilder.DropTable(
                name: "MemberAddresses");

            migrationBuilder.DropTable(
                name: "MemberProfiles");

            migrationBuilder.DropTable(
                name: "OrderCoupons");

            migrationBuilder.DropTable(
                name: "OrderStatusHistories");

            migrationBuilder.DropTable(
                name: "PackageLimitVersions");

            migrationBuilder.DropTable(
                name: "PaymentEvents");

            migrationBuilder.DropTable(
                name: "ProductImages");

            migrationBuilder.DropTable(
                name: "ProductReviewRevisions");

            migrationBuilder.DropTable(
                name: "ProductTags");

            migrationBuilder.DropTable(
                name: "ProductTranslations");

            migrationBuilder.DropTable(
                name: "RefundAllocations");

            migrationBuilder.DropTable(
                name: "ReportAssignmentHistories");

            migrationBuilder.DropTable(
                name: "ReportAttachments");

            migrationBuilder.DropTable(
                name: "ReportStatusHistories");

            migrationBuilder.DropTable(
                name: "ReturnAssignmentHistories");

            migrationBuilder.DropTable(
                name: "ReturnAttachments");

            migrationBuilder.DropTable(
                name: "ReturnInspections");

            migrationBuilder.DropTable(
                name: "ReturnShipmentEvents");

            migrationBuilder.DropTable(
                name: "ReturnStatusHistories");

            migrationBuilder.DropTable(
                name: "ReviewImages");

            migrationBuilder.DropTable(
                name: "SalePrices");

            migrationBuilder.DropTable(
                name: "ShipmentStatusHistories");

            migrationBuilder.DropTable(
                name: "SimulatedInvoiceAllowanceItems");

            migrationBuilder.DropTable(
                name: "SkuSpecificationValues");

            migrationBuilder.DropTable(
                name: "SkuTranslations");

            migrationBuilder.DropTable(
                name: "SpecificationDefinitionTranslations");

            migrationBuilder.DropTable(
                name: "SpecificationOptionTranslations");

            migrationBuilder.DropTable(
                name: "SupportAssignmentHistories");

            migrationBuilder.DropTable(
                name: "SupportAttachments");

            migrationBuilder.DropTable(
                name: "SupportSlaEvents");

            migrationBuilder.DropTable(
                name: "SupportStatusHistories");

            migrationBuilder.DropTable(
                name: "SupportSummaries");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AssemblyJobs");

            migrationBuilder.DropTable(
                name: "Carts");

            migrationBuilder.DropTable(
                name: "CompatibilityCheckRuns");

            migrationBuilder.DropTable(
                name: "GuestOrderAccessRequests");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "InventoryMovements");

            migrationBuilder.DropTable(
                name: "CouponRedemptions");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "ReportCases");

            migrationBuilder.DropTable(
                name: "ReturnItems");

            migrationBuilder.DropTable(
                name: "ReturnShipments");

            migrationBuilder.DropTable(
                name: "ProductReviews");

            migrationBuilder.DropTable(
                name: "Shipments");

            migrationBuilder.DropTable(
                name: "SimulatedInvoiceAllowances");

            migrationBuilder.DropTable(
                name: "SimulatedInvoiceItems");

            migrationBuilder.DropTable(
                name: "SpecificationSources");

            migrationBuilder.DropTable(
                name: "SpecificationOptions");

            migrationBuilder.DropTable(
                name: "SupportMessages");

            migrationBuilder.DropTable(
                name: "BuildLists");

            migrationBuilder.DropTable(
                name: "InventoryReservations");

            migrationBuilder.DropTable(
                name: "Coupons");

            migrationBuilder.DropTable(
                name: "ConvenienceStores");

            migrationBuilder.DropTable(
                name: "ShippingMethods");

            migrationBuilder.DropTable(
                name: "Refunds");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "SimulatedInvoices");

            migrationBuilder.DropTable(
                name: "SpecificationDefinitions");

            migrationBuilder.DropTable(
                name: "SupportTickets");

            migrationBuilder.DropTable(
                name: "PaymentAttempts");

            migrationBuilder.DropTable(
                name: "ReturnRequests");

            migrationBuilder.DropTable(
                name: "Skus");

            migrationBuilder.DropTable(
                name: "MeasurementUnits");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "ShippingProviderProfiles");

            migrationBuilder.DropTable(
                name: "Brands");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}

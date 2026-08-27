using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutPolicyInvoiceShippingAndPaymentIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_PaymentAttempts_IdempotencyKey",
                table: "PaymentAttempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_PolicyVersions",
                table: "Orders");

            migrationBuilder.AddColumn<string>(
                name: "ProviderCode",
                table: "ShippingMethods",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceBuyerEmail",
                table: "Orders",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceBuyerType",
                table: "Orders",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceCarrierType",
                table: "Orders",
                type: "varchar(30)",
                unicode: false,
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceCarrierValueMasked",
                table: "Orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceCompanyName",
                table: "Orders",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceCompanyTaxId",
                table: "Orders",
                type: "varchar(8)",
                unicode: false,
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrivacyPolicyVersion",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TermsPolicyVersion",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingMethods_ProviderCode",
                table: "ShippingMethods",
                column: "ProviderCode");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_IdempotencyKey",
                table: "PaymentAttempts",
                column: "IdempotencyKey");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_InvoiceCarrier",
                table: "Orders",
                sql: "([InvoiceCarrierType] IS NULL AND [InvoiceCarrierValueMasked] IS NULL) OR ([InvoiceCarrierType] IS NOT NULL AND [InvoiceCarrierValueMasked] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_InvoiceCompany",
                table: "Orders",
                sql: "([InvoiceBuyerType] IS NULL AND [InvoiceBuyerEmail] IS NULL AND [InvoiceCarrierType] IS NULL AND [InvoiceCarrierValueMasked] IS NULL AND [InvoiceCompanyTaxId] IS NULL AND [InvoiceCompanyName] IS NULL) OR ([InvoiceBuyerType] = 'Company' AND [InvoiceBuyerEmail] IS NOT NULL AND [InvoiceCompanyTaxId] IS NOT NULL AND [InvoiceCompanyName] IS NOT NULL) OR ([InvoiceBuyerType] = 'Individual' AND [InvoiceBuyerEmail] IS NOT NULL AND [InvoiceCompanyTaxId] IS NULL AND [InvoiceCompanyName] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_PolicyVersions",
                table: "Orders",
                sql: "[ShippingConstraintPolicyVersion] > 0 AND [ReturnPolicyVersion] > 0 AND ([TermsPolicyVersion] IS NULL OR [TermsPolicyVersion] > 0) AND ([PrivacyPolicyVersion] IS NULL OR [PrivacyPolicyVersion] > 0) AND ([CouponPolicyVersion] IS NULL OR [CouponPolicyVersion] > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShippingMethods_ProviderCode",
                table: "ShippingMethods");

            migrationBuilder.DropIndex(
                name: "IX_PaymentAttempts_IdempotencyKey",
                table: "PaymentAttempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_InvoiceCarrier",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_InvoiceCompany",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_PolicyVersions",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ProviderCode",
                table: "ShippingMethods");

            migrationBuilder.DropColumn(
                name: "InvoiceBuyerEmail",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceBuyerType",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceCarrierType",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceCarrierValueMasked",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceCompanyName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceCompanyTaxId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PrivacyPolicyVersion",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TermsPolicyVersion",
                table: "Orders");

            migrationBuilder.CreateIndex(
                name: "UX_PaymentAttempts_IdempotencyKey",
                table: "PaymentAttempts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_PolicyVersions",
                table: "Orders",
                sql: "[ShippingConstraintPolicyVersion] > 0 AND [ReturnPolicyVersion] > 0 AND ([CouponPolicyVersion] IS NULL OR [CouponPolicyVersion] > 0)");
        }
    }
}

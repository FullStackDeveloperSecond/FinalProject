using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Common;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Promotions;

namespace DoSelect.Application.Checkout;

public enum CheckoutInvoiceType
{
    Simulated,
}

public enum CheckoutInvoiceBuyerType
{
    Personal,
    Company,
}

public sealed record CheckoutBuyerInput(
    [Required, EmailAddress, StringLength(320)] string Email,
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required, StringLength(32, MinimumLength = 6)] string Phone);

public sealed record CheckoutAddressInput(
    [Required, StringLength(100, MinimumLength = 1)] string RecipientName,
    [Required, StringLength(32, MinimumLength = 6)] string Phone,
    [StringLength(16, MinimumLength = 1)] string? PostalCode,
    [StringLength(50, MinimumLength = 1)] string? City,
    [StringLength(50, MinimumLength = 1)] string? District,
    [StringLength(300, MinimumLength = 1)] string? AddressLine1,
    [StringLength(300)] string? AddressLine2);

public sealed record CheckoutShippingInput(
    [Required, StringLength(64, MinimumLength = 1)] string MethodCode,
    CheckoutAddressInput? Address,
    Guid? StorePublicId,
    [StringLength(500)] string? DeliveryNote = null);

public sealed record CheckoutInvoiceInput(
    CheckoutInvoiceType Type,
    CheckoutInvoiceBuyerType BuyerType,
    [StringLength(30, MinimumLength = 1)] string? CarrierType,
    [StringLength(64, MinimumLength = 1)] string? CarrierValue,
    [StringLength(8, MinimumLength = 8)] string? CompanyTaxId,
    [StringLength(160, MinimumLength = 1)] string? CompanyName);

public sealed record AcceptedPolicyVersions(
    [Range(1, int.MaxValue)] int Terms,
    [Range(1, int.MaxValue)] int Return,
    [Range(1, int.MaxValue)] int Privacy);

public sealed record CreateOrderRequest(
    Guid CartPublicId,
    [Required] byte[] CartRowVersion,
    [Required] CheckoutBuyerInput Buyer,
    [Required] CheckoutShippingInput Shipping,
    PaymentMethod PaymentMethod,
    [StringLength(64, MinimumLength = 1)] string? CouponCode,
    [Required] CheckoutInvoiceInput Invoice,
    [Required] AcceptedPolicyVersions AcceptPolicyVersions);

/// <summary>
/// Backend-resolved Checkout identity. A member carries both its internal Identity id and public id;
/// a guest carries only the secret cart key. Client input must never populate this object directly.
/// </summary>
public sealed record CheckoutActor(string? MemberUserId, Guid? MemberPublicId, string? GuestCartKey)
{
    public static CheckoutActor ForMember(string memberUserId, Guid memberPublicId) =>
        new(Require(memberUserId, nameof(memberUserId)),
            memberPublicId == Guid.Empty
                ? throw new ArgumentException("Member PublicId is required.", nameof(memberPublicId))
                : memberPublicId,
            null);

    public static CheckoutActor ForGuest(string guestCartKey) =>
        new(null, null, Require(guestCartKey, nameof(guestCartKey)));

    public bool IsMember => MemberUserId is not null;

    private static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The value is required.", parameterName)
            : value.Trim();
}

public sealed record CheckoutRecipientSnapshot(
    string Name,
    string Phone,
    string Email,
    string CountryCode,
    string? PostalCode,
    string? City,
    string? District,
    string? AddressLine1,
    string? AddressLine2);

public sealed record CheckoutInvoicePreferenceSnapshot(
    SimulatedInvoiceBuyerType BuyerType,
    string BuyerEmail,
    string? CarrierType,
    string? CarrierValueMasked,
    string? CompanyTaxId,
    string? CompanyName)
{
    public OrderInvoicePreference ToDomain() =>
        new(BuyerType, BuyerEmail, CarrierType, CarrierValueMasked, CompanyTaxId, CompanyName);
}

/// <summary>
/// Normalized command entering the single SQL Checkout transaction. It contains user intent and
/// immutable acceptance input only; prices, stock, shipping fees, coupon usage and compatibility
/// remain trusted server-side queries and must be revalidated by the transaction gateway.
/// </summary>
public sealed record CheckoutCommand(
    CheckoutActor Actor,
    Guid CartPublicId,
    byte[] CartRowVersion,
    CheckoutRecipientSnapshot Recipient,
    string? DeliveryNote,
    string ShippingMethodCode,
    Guid? StorePublicId,
    PaymentMethod PaymentMethod,
    string? CouponCode,
    CheckoutInvoicePreferenceSnapshot InvoicePreference,
    CheckoutPolicySnapshot PolicyVersions,
    string IdempotencyKey);

public sealed record CheckoutCreatedOrder(
    Guid PublicId,
    string OrderNumber,
    decimal GrandTotal,
    string Currency,
    Guid PaymentAttemptPublicId,
    DateTime? PaymentDueAtUtc);

/// <summary>
/// Infrastructure owns the actual atomic Order → Reservation → Coupon → Payment write. The
/// implementation must share the transaction opened by IIdempotencyExecutor and must not commit,
/// suppress, or replace it. Every mutable upstream value is re-read inside that transaction.
/// </summary>
public interface ICheckoutTransactionGateway
{
    Task<CheckoutCreatedOrder> ExecuteAsync(
        CheckoutCommand command,
        CancellationToken cancellationToken = default);

    Task<CheckoutCreatedOrder?> FindCreatedOrderAsync(
        Guid orderPublicId,
        CancellationToken cancellationToken = default);
}

public static class CheckoutCommandFactory
{
    public static CheckoutCommand Create(
        CheckoutActor actor,
        CreateOrderRequest request,
        string idempotencyKey,
        CheckoutPolicySnapshot currentPolicies)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(actor);

        if (request.CartPublicId == Guid.Empty || request.CartRowVersion is not { Length: > 0 })
        {
            throw DomainProblemException.Validation("Cart PublicId and RowVersion are required.");
        }

        var buyer = request.Buyer ?? throw DomainProblemException.Validation("Buyer is required.");
        var shipping = request.Shipping ?? throw DomainProblemException.Validation("Shipping is required.");
        var invoice = request.Invoice ?? throw DomainProblemException.Validation("Invoice preference is required.");
        var policies = request.AcceptPolicyVersions ??
            throw DomainProblemException.Validation("Accepted policy versions are required.");

        var buyerEmail = Require(buyer.Email, 320, "Buyer Email").ToLowerInvariant();
        var buyerName = Require(buyer.Name, 100, "Buyer name");
        var buyerPhone = Require(buyer.Phone, 32, "Buyer phone");
        ValidatePolicies(policies, currentPolicies);

        var recipient = BuildRecipient(buyerEmail, buyerName, buyerPhone, shipping);
        var invoicePreference = BuildInvoicePreference(buyerEmail, invoice);
        var normalizedCoupon = string.IsNullOrWhiteSpace(request.CouponCode)
            ? null
            : CouponCode.Normalize(request.CouponCode);

        return new CheckoutCommand(
            actor,
            request.CartPublicId,
            request.CartRowVersion.ToArray(),
            recipient,
            Optional(shipping.DeliveryNote, 500, "Delivery note"),
            Require(shipping.MethodCode, 64, "Shipping method code"),
            shipping.StorePublicId,
            request.PaymentMethod,
            normalizedCoupon,
            invoicePreference,
            currentPolicies,
            Require(idempotencyKey, 128, "Idempotency-Key"));
    }

    private static CheckoutRecipientSnapshot BuildRecipient(
        string buyerEmail,
        string buyerName,
        string buyerPhone,
        CheckoutShippingInput shipping)
    {
        if (shipping.Address is not null && shipping.StorePublicId.HasValue)
        {
            throw DomainProblemException.Validation(
                "Shipping address and convenience-store PublicId are mutually exclusive.");
        }

        if (shipping.Address is null && !shipping.StorePublicId.HasValue)
        {
            throw DomainProblemException.Validation(
                "A shipping address or convenience-store PublicId is required.");
        }

        if (shipping.StorePublicId == Guid.Empty)
        {
            throw DomainProblemException.Validation("Convenience-store PublicId cannot be empty.");
        }

        if (shipping.Address is null)
        {
            return new CheckoutRecipientSnapshot(
                buyerName,
                buyerPhone,
                buyerEmail,
                "TW",
                null,
                null,
                null,
                null,
                null);
        }

        var address = shipping.Address;
        return new CheckoutRecipientSnapshot(
            Require(address.RecipientName, 100, "Recipient name"),
            Require(address.Phone, 32, "Recipient phone"),
            buyerEmail,
            "TW",
            Optional(address.PostalCode, 16, "Postal code"),
            Optional(address.City, 50, "City"),
            Optional(address.District, 50, "District"),
            Optional(address.AddressLine1, 300, "Address line 1"),
            Optional(address.AddressLine2, 300, "Address line 2"));
    }

    private static CheckoutInvoicePreferenceSnapshot BuildInvoicePreference(
        string buyerEmail,
        CheckoutInvoiceInput invoice)
    {
        if (invoice.Type != CheckoutInvoiceType.Simulated)
        {
            throw DomainProblemException.Validation("Only simulated invoices are supported.");
        }

        var carrierType = Optional(invoice.CarrierType, 30, "Carrier type");
        var carrierValue = Optional(invoice.CarrierValue, 64, "Carrier value");
        var companyTaxId = Optional(invoice.CompanyTaxId, 8, "Company tax id");
        var companyName = Optional(invoice.CompanyName, 160, "Company name");

        if ((carrierType is null) != (carrierValue is null))
        {
            throw DomainProblemException.Validation(
                "Carrier type and carrier value must be supplied together.");
        }

        if (invoice.BuyerType == CheckoutInvoiceBuyerType.Company &&
            (companyTaxId is null || companyName is null) ||
            invoice.BuyerType == CheckoutInvoiceBuyerType.Personal &&
            (companyTaxId is not null || companyName is not null))
        {
            throw DomainProblemException.Validation("Invoice buyer details do not match buyer type.");
        }

        return new CheckoutInvoicePreferenceSnapshot(
            invoice.BuyerType == CheckoutInvoiceBuyerType.Company
                ? SimulatedInvoiceBuyerType.Company
                : SimulatedInvoiceBuyerType.Individual,
            buyerEmail,
            carrierType,
            carrierValue is null ? null : MaskCarrierValue(carrierValue),
            companyTaxId,
            companyName);
    }

    private static string MaskCarrierValue(string value)
    {
        var visibleLength = Math.Min(4, value.Length);
        return new string('*', value.Length - visibleLength) + value[^visibleLength..];
    }

    private static void ValidateActor(CheckoutActor actor)
    {
        if (actor.IsMember != actor.MemberPublicId.HasValue ||
            actor.IsMember == !string.IsNullOrWhiteSpace(actor.GuestCartKey))
        {
            throw new ArgumentException("Checkout actor must be exactly one member or guest.", nameof(actor));
        }
    }

    private static void ValidatePolicies(
        AcceptedPolicyVersions policies,
        CheckoutPolicySnapshot currentPolicies)
    {
        if (policies.Terms <= 0 || policies.Return <= 0 || policies.Privacy <= 0 ||
            currentPolicies.Terms <= 0 || currentPolicies.Return <= 0 ||
            currentPolicies.Privacy <= 0 || currentPolicies.ShippingConstraint <= 0)
        {
            throw DomainProblemException.Validation("Accepted policy versions must be positive.");
        }

        if (policies.Terms != currentPolicies.Terms ||
            policies.Return != currentPolicies.Return ||
            policies.Privacy != currentPolicies.Privacy)
        {
            throw DomainProblemException.Validation(
                "Accepted policy versions no longer match the current Checkout policies.");
        }
    }

    private static string Require(string value, int maximumLength, string fieldName)
    {
        var normalized = InputNormalization.Canonicalize(value);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumLength)
        {
            throw DomainProblemException.Validation($"{fieldName} is invalid.");
        }

        return normalized;
    }

    private static string? Optional(string? value, int maximumLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Require(value, maximumLength, fieldName);
    }
}

using DoSelect.Application.Checkout;
using DoSelect.Application.Common;
using DoSelect.Domain.Payments;

namespace DoSelect.Application.Tests.Checkout;

public sealed class CheckoutCommandFactoryTests
{
    [Fact]
    public void Create_HomeDeliveryUsesAddressRecipientAndBuildsApprovedSnapshots()
    {
        var cartPublicId = Guid.NewGuid();
        var request = Request(
            cartPublicId,
            new CheckoutShippingInput(
                "HOME_DELIVERY",
                new CheckoutAddressInput(
                    "Recipient",
                    "0987654321",
                    "100",
                    "Taipei",
                    "Zhongzheng",
                    "No. 1",
                    null),
                null,
                "Leave with reception"),
            new CheckoutInvoiceInput(
                CheckoutInvoiceType.Simulated,
                CheckoutInvoiceBuyerType.Personal,
                "MobileBarcode",
                "/ABC1234",
                null,
                null));

        var command = CheckoutCommandFactory.Create(
            CheckoutActor.ForGuest("guest-cart-key"),
            request,
            "checkout-key",
            new CheckoutPolicySnapshot(3, 4, 5, 6));

        Assert.Equal("SAVE10", command.CouponCode);
        Assert.Equal("Recipient", command.Recipient.Name);
        Assert.Equal("0987654321", command.Recipient.Phone);
        Assert.Equal("buyer@example.com", command.Recipient.Email);
        Assert.Equal("TW", command.Recipient.CountryCode);
        Assert.Equal("Leave with reception", command.DeliveryNote);
        Assert.Equal(3, command.PolicyVersions.Terms);
        Assert.Equal(4, command.PolicyVersions.Return);
        Assert.Equal(5, command.PolicyVersions.Privacy);
        Assert.Equal(6, command.PolicyVersions.ShippingConstraint);
        Assert.Equal("****1234", command.InvoicePreference.CarrierValueMasked);
    }

    [Fact]
    public void Create_StorePickupUsesBuyerAsRecipient()
    {
        var request = Request(
            Guid.NewGuid(),
            new CheckoutShippingInput("CVS_PICKUP", null, Guid.NewGuid(), null),
            new CheckoutInvoiceInput(
                CheckoutInvoiceType.Simulated,
                CheckoutInvoiceBuyerType.Personal,
                null,
                null,
                null,
                null));

        var command = CheckoutCommandFactory.Create(
            CheckoutActor.ForGuest("guest-cart-key"),
            request,
            "checkout-key",
            new CheckoutPolicySnapshot(3, 4, 5, 6));

        Assert.Equal("Buyer", command.Recipient.Name);
        Assert.Equal("0912345678", command.Recipient.Phone);
    }

    [Fact]
    public void Create_CompanyInvoiceWithoutCompanyIdentity_RejectsRequest()
    {
        var request = Request(
            Guid.NewGuid(),
            new CheckoutShippingInput("CVS_PICKUP", null, Guid.NewGuid(), null),
            new CheckoutInvoiceInput(
                CheckoutInvoiceType.Simulated,
                CheckoutInvoiceBuyerType.Company,
                null,
                null,
                null,
                null));

        Assert.Throws<DomainProblemException>(() =>
            CheckoutCommandFactory.Create(
                CheckoutActor.ForGuest("guest-cart-key"),
                request,
                "checkout-key",
                new CheckoutPolicySnapshot(3, 4, 5, 6)));
    }

    [Fact]
    public void Create_WithStaleAcceptedPolicyVersion_RejectsRequest()
    {
        var request = Request(
            Guid.NewGuid(),
            new CheckoutShippingInput("CVS_PICKUP", null, Guid.NewGuid(), null),
            new CheckoutInvoiceInput(
                CheckoutInvoiceType.Simulated,
                CheckoutInvoiceBuyerType.Personal,
                null,
                null,
                null,
                null));

        Assert.Throws<DomainProblemException>(() => CheckoutCommandFactory.Create(
            CheckoutActor.ForGuest("guest-cart-key"),
            request,
            "checkout-key",
            new CheckoutPolicySnapshot(4, 4, 5, 6)));
    }

    [Fact]
    public void Create_HomeDeliveryWithIncompleteAddress_RejectsRequest()
    {
        var request = Request(
            Guid.NewGuid(),
            new CheckoutShippingInput(
                "HOME_DELIVERY",
                new CheckoutAddressInput(
                    "Recipient",
                    "0987654321",
                    null,
                    "Taipei",
                    "Zhongzheng",
                    "No. 1",
                    null),
                null),
            new CheckoutInvoiceInput(
                CheckoutInvoiceType.Simulated,
                CheckoutInvoiceBuyerType.Personal,
                null,
                null,
                null,
                null));

        var exception = Assert.Throws<DomainProblemException>(() => CheckoutCommandFactory.Create(
            CheckoutActor.ForGuest("guest-cart-key"),
            request,
            "checkout-key",
            new CheckoutPolicySnapshot(3, 4, 5, 6)));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(DomainErrorCodes.ValidationFailed, exception.Code);
    }

    private static CreateOrderRequest Request(
        Guid cartPublicId,
        CheckoutShippingInput shipping,
        CheckoutInvoiceInput invoice) =>
        new(
            cartPublicId,
            [1, 2, 3],
            new CheckoutBuyerInput("buyer@example.com", "Buyer", "0912345678"),
            shipping,
            PaymentMethod.CreditCard,
            " save10 ",
            invoice,
            new AcceptedPolicyVersions(3, 4, 5));
}

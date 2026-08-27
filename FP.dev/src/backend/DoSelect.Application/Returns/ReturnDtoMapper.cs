using DoSelect.Domain.Returns;

namespace DoSelect.Application.Returns;

/// <summary>Shared shipment-to-DTO mapping (masking rules) used by both the customer and admin
/// Return services, so recipient PII masking cannot drift between the two surfaces.</summary>
internal static class ReturnDtoMapper
{
    public static ReturnShipmentDto ToShipmentDto(ReturnShipment shipment, IReadOnlyList<ReturnShipmentEvent> events) =>
        new(
            shipment.PublicId,
            shipment.ShipmentNumber,
            shipment.Method,
            shipment.Status,
            shipment.CarrierCode,
            shipment.TrackingNumber,
            MaskName(shipment.RecipientName),
            MaskPhone(shipment.RecipientPhone),
            MaskAddress(shipment.AddressLine),
            shipment.StoreCode,
            shipment.StoreName,
            shipment.ShippedAtUtc,
            shipment.ReceivedAtUtc,
            [.. events
                .OrderBy(e => e.OccurredAtUtc)
                .ThenBy(e => e.Id)
                .Select(e => new ReturnShipmentEventSummaryDto(e.Source, e.EventType, e.OccurredAtUtc))],
            shipment.RowVersion);

    private static string? MaskName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        return name.Length <= 1 ? name : name[..1] + new string('*', name.Length - 1);
    }

    private static string? MaskPhone(string? phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length <= 3)
        {
            return phone;
        }

        return new string('*', phone.Length - 3) + phone[^3..];
    }

    private static string? MaskAddress(string? address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return address;
        }

        return address.Length <= 6 ? address : address[..6] + "…";
    }
}

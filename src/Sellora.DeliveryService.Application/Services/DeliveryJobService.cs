using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Application.Consumers.Events;
using Sellora.DeliveryService.Application.Events;
using Sellora.DeliveryService.Domain.Entities;

namespace Sellora.DeliveryService.Application.Services;

public interface IDeliveryJobService
{
    Task CreateFromOrderConfirmedAsync(OrderConfirmedEvent evt, CancellationToken ct);
}

public class DeliveryJobService(
    IDeliveryJobRepository repository,
    IOutboxWriter outboxWriter,
    IDeliveryReferenceGenerator referenceGenerator,
    ILogger<DeliveryJobService> logger)
    : IDeliveryJobService
{
    private const string DeliveryTopic = "sellora.delivery.v1";
    private const string ImmediateCashSale = "ImmediateCashSale";

    public async Task CreateFromOrderConfirmedAsync(OrderConfirmedEvent evt, CancellationToken ct)
    {
        var deliveryRef = referenceGenerator.Generate(evt.OccurredAt);

        DeliveryJob job;

        if (evt.FulfilmentType == ImmediateCashSale)
        {
            // DeliveredAt: the actual handover moment is checkIn; fall back to confirmedAt when
            // the van driver did not explicitly check in (e.g. offline sync edge case).
            var deliveredAt = evt.CheckoutLocation?.CheckedInAt ?? evt.ConfirmedAt;

            job = DeliveryJob.CreateDelivered(
                companyId: evt.CompanyId,
                deliveryReference: deliveryRef,
                orderId: evt.OrderId,
                orderReference: evt.OrderReference,
                fulfilmentType: evt.FulfilmentType,
                shopId: evt.ShopId,
                shopName: evt.ShopName,
                shopOwnerName: evt.ShopOwnerName,
                shopOwnerEmail: evt.ShopOwnerEmail,
                agencyId: evt.AgencyId,
                agencyName: evt.AgencyName,
                agencyEmail: evt.AgencyEmail,
                territoryId: evt.TerritoryId,
                provinceId: evt.ProvinceId,
                orderSalesRepId: evt.OrderSalesRepId,
                total: evt.Total,
                currency: evt.Currency,
                deliveredAt: deliveredAt,
                lines: MapLines(job: null, evt.CompanyId, evt.Lines),
                actorUserId: "system");
        }
        else
        {
            job = DeliveryJob.CreatePending(
                companyId: evt.CompanyId,
                deliveryReference: deliveryRef,
                orderId: evt.OrderId,
                orderReference: evt.OrderReference,
                fulfilmentType: evt.FulfilmentType,
                shopId: evt.ShopId,
                shopName: evt.ShopName,
                shopOwnerName: evt.ShopOwnerName,
                shopOwnerEmail: evt.ShopOwnerEmail,
                agencyId: evt.AgencyId,
                agencyName: evt.AgencyName,
                agencyEmail: evt.AgencyEmail,
                territoryId: evt.TerritoryId,
                provinceId: evt.ProvinceId,
                orderSalesRepId: evt.OrderSalesRepId,
                total: evt.Total,
                currency: evt.Currency,
                lines: MapLines(job: null, evt.CompanyId, evt.Lines));
        }

        var outboundEvent = DeliveryJobCreatedEvent.Create(
            companyId: job.CompanyId,
            deliveryJobId: job.DeliveryJobId,
            deliveryReference: job.DeliveryReference,
            orderId: job.OrderId,
            orderReference: job.OrderReference,
            fulfilmentType: job.FulfilmentType,
            agencyId: job.AgencyId,
            agencyName: job.AgencyName,
            shopId: job.ShopId,
            shopName: job.ShopName,
            status: job.Status.ToString(),
            total: job.Total,
            currency: job.Currency,
            deliveredAt: job.DeliveredAt,
            createdAt: job.CreatedAt);

        var outbox = new OutboxMessage(
            companyId: job.CompanyId,
            topic: DeliveryTopic,
            // keyed by order reference so all events for the same order land on the same partition
            messageKey: job.OrderReference,
            eventType: "DeliveryJobCreated",
            payload: JsonSerializer.Serialize(outboundEvent));

        await repository.AddAsync(job, ct);
        outboxWriter.Write(outbox);

        logger.LogInformation("DeliveryJobCreated {OrderReference} {FulfilmentType} {Status}",
            job.OrderReference, job.FulfilmentType, job.Status);
    }

    private static IEnumerable<DeliveryJobLine> MapLines(
        DeliveryJob? job,
        Guid companyId,
        IReadOnlyList<OrderLineDto> lines)
        => lines.Select(l => new DeliveryJobLine(
            // deliveryJobId is Guid.Empty here; EF Core sets the FK from the parent navigation
            deliveryJobId: job?.DeliveryJobId ?? Guid.Empty,
            companyId: companyId,
            productId: l.ProductId,
            productName: l.ProductName,
            quantity: l.Quantity,
            unitPrice: l.UnitPrice,
            lineTotal: l.LineTotal));
}

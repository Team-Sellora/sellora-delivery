namespace Sellora.DeliveryService.Domain.Entities;

public class DeliveryJobLine
{
    public Guid DeliveryJobLineId { get; private set; }
    public Guid DeliveryJobId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = default!;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }

    private DeliveryJobLine() { }

    public DeliveryJobLine(
        Guid deliveryJobId,
        Guid companyId,
        Guid productId,
        string productName,
        int quantity,
        decimal unitPrice,
        decimal lineTotal)
    {
        DeliveryJobLineId = Guid.NewGuid();
        DeliveryJobId = deliveryJobId;
        CompanyId = companyId;
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
        LineTotal = lineTotal;
    }
}

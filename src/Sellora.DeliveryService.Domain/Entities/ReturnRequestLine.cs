namespace Sellora.DeliveryService.Domain.Entities;

public class ReturnRequestLine
{
    public Guid ReturnRequestLineId { get; private set; }
    public Guid ReturnRequestId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = default!;
    public int Quantity { get; private set; }
    public string Reason { get; private set; } = default!;

    private ReturnRequestLine() { }

    public ReturnRequestLine(
        Guid returnRequestId,
        Guid companyId,
        Guid productId,
        string productName,
        int quantity,
        string reason)
    {
        ReturnRequestLineId = Guid.NewGuid();
        ReturnRequestId = returnRequestId;
        CompanyId = companyId;
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        Reason = reason;
    }
}

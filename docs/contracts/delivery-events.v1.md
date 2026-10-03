# Delivery Events Contract — v1

**Topic:** `sellora.delivery.v1`
**Schema version:** 1
**Owning service:** `sellora-delivery`

---

## Standard Kafka Message Headers

All events on this topic carry the following headers:

| Header | Type | Description |
|---|---|---|
| `x-event-type` | string | Matches the `EventType` field in the payload |
| `x-company-id` | UUID | Company (tenant) that owns the record |
| `x-correlation-id` | UUID | Outbox message ID; use for distributed tracing |
| `x-service-source` | string | Always `sellora-delivery` |

---

## Events

### `DeliveryJobCreated`

**Published when:** A delivery job is first created as a result of an `OrderConfirmed` event. Published for **both** `ScheduledDelivery` and `ImmediateCashSale` fulfilment types.

> **Note:** For `ImmediateCashSale` orders the job is created in `Delivered` state. Consumers that only care about open deliveries (e.g. the agency's dispatch queue) should filter by `Status != "Delivered"`.
>
> This event is intentionally **not** `DeliveryStatusChanged` — publishing a status-changed event for the initial creation of a cash-sale job would cause the Notification service to send a spurious "delivered" confirmation email.

**Message key:** `OrderReference` (ensures all events for the same order land on the same partition, preserving order for downstream consumers).

#### Payload schema

```json
{
  "eventId": "uuid",
  "eventType": "DeliveryJobCreated",
  "companyId": "uuid",
  "deliveryJobId": "uuid",
  "deliveryReference": "DL-yyMMdd-XXXXXX",
  "orderId": "uuid",
  "orderReference": "string",
  "fulfilmentType": "ScheduledDelivery | ImmediateCashSale",
  "agencyId": "uuid",
  "agencyName": "string",
  "shopId": "uuid",
  "shopName": "string",
  "status": "Pending | Delivered",
  "total": "decimal",
  "currency": "ISO 4217 3-char code",
  "deliveredAt": "ISO 8601 timestamp | null",
  "createdAt": "ISO 8601 timestamp"
}
```

#### Example — ScheduledDelivery (Pending)

```json
{
  "eventId": "a1b2c3d4-...",
  "eventType": "DeliveryJobCreated",
  "companyId": "9f8e7d6c-...",
  "deliveryJobId": "11223344-...",
  "deliveryReference": "DL-261002-A3F1B9",
  "orderId": "aabbccdd-...",
  "orderReference": "ORD-261002-XYZ123",
  "fulfilmentType": "ScheduledDelivery",
  "agencyId": "55667788-...",
  "agencyName": "Northern Agency",
  "shopId": "99aabbcc-...",
  "shopName": "Sunrise Shop",
  "status": "Pending",
  "total": 15000.00,
  "currency": "MWK",
  "deliveredAt": null,
  "createdAt": "2026-10-02T10:30:00Z"
}
```

#### Example — ImmediateCashSale (Delivered)

```json
{
  "eventId": "b2c3d4e5-...",
  "eventType": "DeliveryJobCreated",
  "companyId": "9f8e7d6c-...",
  "deliveryJobId": "22334455-...",
  "deliveryReference": "DL-261002-C7E2A0",
  "orderId": "bbccddee-...",
  "orderReference": "ORD-261002-ABC456",
  "fulfilmentType": "ImmediateCashSale",
  "agencyId": "55667788-...",
  "agencyName": "Northern Agency",
  "shopId": "aabbccdd-...",
  "shopName": "Sunset Kiosk",
  "status": "Delivered",
  "total": 4500.00,
  "currency": "MWK",
  "deliveredAt": "2026-10-02T08:15:33Z",
  "createdAt": "2026-10-02T08:16:01Z"
}
```

---

## Dead Letter Topic

**Topic:** `sellora.delivery.dead-letter.v1`

Messages that cannot be deserialized are routed here by the consumer without stalling the partition. The dead-letter payload contains:

```json
{
  "sourceTopic": "sellora.order.v1",
  "partition": 0,
  "offset": 12345,
  "originalKey": "string | null",
  "originalValue": "raw message bytes as string",
  "reason": "DeserialiseFailure: ...",
  "occurredAt": "ISO 8601 timestamp"
}
```

---

## Consumed Events

| Topic | Event type | Consumer group |
|---|---|---|
| `sellora.order.v1` | `OrderConfirmed` | `sellora.delivery.order.v1` |

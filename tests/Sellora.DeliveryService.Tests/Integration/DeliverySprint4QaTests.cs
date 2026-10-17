using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sellora.DeliveryService.Api.Controllers;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Application.Consumers.Events;
using Sellora.DeliveryService.Application.Dependencies;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;
using Sellora.DeliveryService.Infrastructure.Outbox;
using Sellora.DeliveryService.Infrastructure.Persistence;
using Sellora.DeliveryService.Infrastructure.Repositories;
using Sellora.DeliveryService.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sellora.DeliveryService.Tests.Integration;

/// <summary>
/// Sprint 4 QA - US-E6-1..US-E6-4 against a real PostgreSQL database (Testcontainers), through the controller,
/// repository, outbox writer and DbContext exactly as the API wires them. Organization is mocked.
///   CSP-261  cash sale record (DeliveredAt = check-in, DeliveryJobCreated only)
///   CSP-262  assignment validation order, Colombo date, concurrent assignment (xmin)
///   CSP-263  append-only history, status rules, stale version, event payload vs Notification's contract
///   CSP-264  scope widening
/// Drop into tests/Sellora.DeliveryService.Tests/Integration/ and run:
///   dotnet test --filter "FullyQualifiedName~Sprint4Qa" --logger "console;verbosity=normal"
/// Tests named ..._Spec_... assert the backlog and are expected to FAIL on dev as of 10 Oct 2026; see KNOWN-ISSUES.txt.
/// </summary>
[Collection("PostgreSql")]
public sealed class DeliverySprint4QaTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("delivery_sprint4_qa")
        .WithUsername("qa")
        .WithPassword("qa")
        .Build();

    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _agencyId = Guid.NewGuid();
    private readonly Guid _otherAgencyId = Guid.NewGuid();
    private readonly Guid _territoryId = Guid.NewGuid();
    private readonly Guid _otherTerritoryId = Guid.NewGuid();
    private readonly Guid _provinceId = Guid.NewGuid();
    private readonly Guid _shopId = Guid.NewGuid();
    private readonly Guid _otherShopId = Guid.NewGuid();
    private readonly Guid _repId = Guid.NewGuid();
    private readonly Guid _otherRepId = Guid.NewGuid();
    private DbContextOptions<DeliveryDbContext> _options = default!;

    private sealed class FixedTenant(Guid companyId) : ITenantContext
    {
        public Guid CompanyId { get; } = companyId;
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<DeliveryDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = NewDb();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private DeliveryDbContext NewDb() => new(_options, new FixedTenant(_companyId));

    private static DateOnly ColomboToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")));

    private IReadOnlyList<SalesRepSummary> Reps() => new List<SalesRepSummary>
    {
        new(_repId, "Rep A", "rep.a@qa.test", "Active", new TerritoryReference(_territoryId, "T-A", "Territory A")),
        new(_otherRepId, "Rep B", "rep.b@qa.test", "Active", new TerritoryReference(_otherTerritoryId, "T-B", "Territory B")),
    };

    private (DeliveriesController Controller, DeliveryDbContext Db) Controller(string role, CallerScope scope, string sub = "qa-user")
    {
        var db = NewDb();
        var organization = new Mock<IOrganizationClient>();
        organization.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(scope);
        organization.Setup(o => o.ListSalesRepsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Reps());
        var controller = new DeliveriesController(new DeliveryJobRepository(db), organization.Object,
            new EntityFrameworkOutboxWriter(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("sub", sub), new Claim("roles", role), new Claim("companyId", _companyId.ToString())
                    }, "qa"))
                }
            }
        };
        return (controller, db);
    }

    private CallerScope Operator(Guid? agency = null) => new(null, agency ?? _agencyId, null, Array.Empty<Guid>(), "Operator");
    private CallerScope Rep(Guid? rep = null) => new(rep ?? _repId, null, null, Array.Empty<Guid>(), "Rep");
    private CallerScope Shop(Guid? shop = null) => new(null, null, shop ?? _shopId, Array.Empty<Guid>(), "Shop");

    private async Task<Guid> SeedPending(string fulfilment = "ScheduledDelivery")
    {
        await using var db = NewDb();
        var job = DeliveryJob.CreatePending(_companyId, $"DL-261010-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
            Guid.NewGuid(), $"ORD-QA-{Guid.NewGuid().ToString()[..6]}", fulfilment, _shopId, "QA Shop", "QA Owner",
            "owner@qa.test", _agencyId, "QA Agency", "agency@qa.test", _territoryId, _provinceId, _repId, 2000m, "LKR",
            new[] { new DeliveryJobLine(Guid.Empty, _companyId, Guid.NewGuid(), "QA Product", 20, 100m, 2000m) });
        db.DeliveryJobs.Add(job);
        await db.SaveChangesAsync();
        return job.DeliveryJobId;
    }

    private async Task<Guid> SeedAssigned()
    {
        var id = await SeedPending();
        var (controller, db) = Controller("AgencyOperator", Operator(), "operator-sub");
        await using (db)
        {
            var result = await controller.AssignDeliveryJob(id,
                new AssignDeliveryRequest { SalesRepId = _repId, ScheduledDate = ColomboToday().AddDays(1) }, CancellationToken.None);
            Assert.IsType<NoContentResult>(result);
        }
        return id;
    }

    private async Task<DeliveryJob> Load(Guid id)
    {
        await using var db = NewDb();
        return await db.DeliveryJobs.AsNoTracking().Include(j => j.StatusHistory).FirstAsync(j => j.DeliveryJobId == id);
    }

    private async Task<IActionResult> MoveAsRep(Guid id, string status, string? reason = null, uint? version = null,
        Guid? rep = null)
    {
        var current = await Load(id);
        var (controller, db) = Controller("SalesRep", Rep(rep), "rep-sub");
        await using (db)
        {
            return await controller.UpdateStatus(id, new UpdateDeliveryStatusRequest
            {
                Status = status, Reason = reason, Version = version ?? current.Version
            }, CancellationToken.None);
        }
    }

    private static string Detail(IActionResult result) =>
        JsonSerializer.Serialize((result as ObjectResult)?.Value ?? new { });

    // ================= CSP-261 =================

    [Fact]
    public async Task Sprint4Qa_CSP261_Cash_sale_is_Delivered_at_check_in_and_publishes_only_DeliveryJobCreated()
    {
        var checkedIn = DateTimeOffset.UtcNow.AddMinutes(-7);
        var evt = EventFactory.BuildOrderConfirmedEvent("ImmediateCashSale",
            new CheckoutLocationDto(Guid.Empty, string.Empty, checkedIn)) with { CompanyId = _companyId };
        await using var db = NewDb();
        var reference = new Mock<IDeliveryReferenceGenerator>();
        reference.Setup(r => r.Generate(It.IsAny<DateTimeOffset>())).Returns("DL-261010-QA0001");
        var service = new DeliveryJobService(new DeliveryJobRepository(db), new EntityFrameworkOutboxWriter(db),
            reference.Object, NullLogger<DeliveryJobService>.Instance);

        await service.CreateFromOrderConfirmedAsync(evt, CancellationToken.None);
        await db.SaveChangesAsync();

        var job = await db.DeliveryJobs.Include(j => j.StatusHistory).SingleAsync(j => j.OrderId == evt.OrderId);
        Assert.Equal(DeliveryStatus.Delivered, job.Status);
        Assert.Equal(checkedIn.ToUnixTimeSeconds(), job.DeliveredAt!.Value.ToUnixTimeSeconds());
        Assert.Single(job.StatusHistory);
        Assert.Equal("System", job.StatusHistory.Single().ActorRole);
        var events = await db.OutboxMessages.Where(m => m.MessageKey == evt.OrderReference).ToListAsync();
        Assert.Single(events);
        Assert.Equal("DeliveryJobCreated", events[0].EventType);
        Assert.DoesNotContain(events, m => m.EventType == "DeliveryStatusChanged");
    }

    // ================= CSP-262 =================

    [Fact]
    public async Task Sprint4Qa_CSP262_Assignment_checks_run_in_backlog_order()
    {
        var id = await SeedPending();
        var tomorrow = ColomboToday().AddDays(1);

        var (other, otherDb) = Controller("AgencyOperator", Operator(_otherAgencyId));
        await using (otherDb)
            Assert.IsType<NotFoundResult>(await other.AssignDeliveryJob(id,
                new AssignDeliveryRequest { SalesRepId = _repId, ScheduledDate = tomorrow }, CancellationToken.None));

        var (op, db) = Controller("AgencyOperator", Operator());
        await using (db)
        {
            var yesterday = await op.AssignDeliveryJob(id,
                new AssignDeliveryRequest { SalesRepId = _repId, ScheduledDate = ColomboToday().AddDays(-1) }, CancellationToken.None);
            Assert.IsType<BadRequestObjectResult>(yesterday);

            var wrongTerritory = await op.AssignDeliveryJob(id,
                new AssignDeliveryRequest { SalesRepId = _otherRepId, ScheduledDate = tomorrow }, CancellationToken.None);
            Assert.IsType<BadRequestObjectResult>(wrongTerritory);
            Assert.Contains("territory", Detail(wrongTerritory), StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(DeliveryStatus.Pending, (await Load(id)).Status);

        var (today, todayDb) = Controller("AgencyOperator", Operator());
        await using (todayDb)
            Assert.IsType<NoContentResult>(await today.AssignDeliveryJob(id,
                new AssignDeliveryRequest { SalesRepId = _repId, ScheduledDate = ColomboToday() }, CancellationToken.None));

        var (again, againDb) = Controller("AgencyOperator", Operator());
        await using (againDb)
        {
            var conflict = await again.AssignDeliveryJob(id,
                new AssignDeliveryRequest { SalesRepId = _repId, ScheduledDate = tomorrow }, CancellationToken.None);
            Assert.IsType<ConflictObjectResult>(conflict);
            Assert.Contains("Assigned", Detail(conflict));
        }
        var assigned = await Load(id);
        Assert.Equal(DeliveryStatus.Assigned, assigned.Status);
        Assert.Equal(ColomboToday(), assigned.ScheduledDate);
    }

    [Fact]
    public async Task Sprint4Qa_CSP262_Spec_Concurrent_assignment_gives_one_success_and_one_409()
    {
        var id = await SeedPending();
        var request = new AssignDeliveryRequest { SalesRepId = _repId, ScheduledDate = ColomboToday().AddDays(1) };
        var (first, firstDb) = Controller("AgencyOperator", Operator());
        var (second, secondDb) = Controller("AgencyOperator", Operator());
        await using (firstDb)
        await using (secondDb)
        {
            // Both operators load the Pending job before either saves.
            await secondDb.DeliveryJobs.FirstAsync(j => j.DeliveryJobId == id);
            Assert.IsType<NoContentResult>(await first.AssignDeliveryJob(id, request, CancellationToken.None));

            IActionResult? result = null;
            var error = await Record.ExceptionAsync(async () => result = await second.AssignDeliveryJob(id, request, CancellationToken.None));
            Assert.Null(error); // an unhandled DbUpdateConcurrencyException surfaces as 500
            Assert.IsType<ConflictObjectResult>(result);
        }
        Assert.Single((await Load(id)).StatusHistory, h => h.ToStatus == DeliveryStatus.Assigned);
    }

    // ================= CSP-263 =================

    [Fact]
    public async Task Sprint4Qa_CSP263_History_is_append_only_in_PostgreSQL()
    {
        var id = await SeedAssigned();
        await using var db = NewDb();
        var row = await db.DeliveryStatusHistories.FirstAsync(h => h.DeliveryJobId == id);

        db.Entry(row).Property(h => h.Reason).CurrentValue = "rewritten";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.Entry(row).State = EntityState.Unchanged;
        db.DeliveryStatusHistories.Remove(row);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Sprint4Qa_CSP263_Status_rules_for_the_assigned_rep()
    {
        var id = await SeedAssigned();

        Assert.IsType<ForbidResult>(await MoveAsRep(id, "InTransit", rep: _otherRepId));
        Assert.IsType<BadRequestObjectResult>(await MoveAsRep(id, "Cancelled"));
        Assert.IsType<BadRequestObjectResult>(await MoveAsRep(id, "Assigned"));
        var skip = await MoveAsRep(id, "Delivered");
        Assert.IsType<ConflictObjectResult>(skip);
        Assert.Contains("Assigned", Detail(skip));
        Assert.Contains("Delivered", Detail(skip));

        Assert.IsType<NoContentResult>(await MoveAsRep(id, "InTransit"));
        Assert.IsType<BadRequestObjectResult>(await MoveAsRep(id, "Failed"));
        Assert.IsType<BadRequestObjectResult>(await MoveAsRep(id, "Failed", "   "));
        Assert.IsType<BadRequestObjectResult>(await MoveAsRep(id, "Failed", new string('x', 501)));
        Assert.IsType<NoContentResult>(await MoveAsRep(id, "Failed", "Shop closed"));

        var job = await Load(id);
        Assert.Equal(DeliveryStatus.Failed, job.Status);
        var last = job.StatusHistory.OrderBy(h => h.OccurredAt).Last();
        Assert.Equal("Shop closed", last.Reason);
        Assert.Equal("rep-sub", last.ActorUserId);
    }

    [Fact]
    public async Task Sprint4Qa_CSP263_Spec_Stale_version_answers_409_not_412()
    {
        var id = await SeedAssigned();
        var stale = (await Load(id)).Version;
        Assert.IsType<NoContentResult>(await MoveAsRep(id, "InTransit", version: stale));
        var result = await MoveAsRep(id, "Delivered", version: stale);
        Assert.IsType<ConflictObjectResult>(result); // the web client only handles 409 (US-E6-3-FE1)
    }

    [Fact]
    public async Task Sprint4Qa_CSP263_Spec_DeliveryStatusChanged_payload_matches_Notification_contract_exactly()
    {
        var id = await SeedAssigned();
        await using var db = NewDb();
        var job = await db.DeliveryJobs.FirstAsync(j => j.DeliveryJobId == id);
        var message = await db.OutboxMessages.SingleAsync(m => m.MessageKey == job.OrderReference && m.EventType == "DeliveryStatusChanged");
        using var payload = JsonDocument.Parse(message.Payload);
        var root = payload.RootElement;

        // sellora-notification/docs/contracts/delivery-events.v1.md: "JSON: camelCase"
        foreach (var name in new[] { "eventId", "eventType", "schemaVersion", "companyId", "deliveryId", "orderId",
                     "orderReference", "previousStatus", "status", "occurredAt", "correlationId", "reason", "scheduledFor",
                     "shop", "agency", "deliveryReference", "territoryId", "provinceId", "actor" })
            Assert.True(root.TryGetProperty(name, out _), $"missing '{name}' (payload keys: {string.Join(",", root.EnumerateObject().Select(p => p.Name))})");

        Assert.Equal("Pending", root.GetProperty("previousStatus").GetString());
        Assert.Equal("Assigned", root.GetProperty("status").GetString());
        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("operator-sub", root.GetProperty("actor").GetProperty("userId").GetString());
    }

    // ================= CSP-264 =================

    [Fact]
    public async Task Sprint4Qa_CSP264_Shop_owner_passing_another_shopId_gets_403()
    {
        await SeedPending();
        var (controller, db) = Controller("ShopOwner", Shop());
        await using (db)
            Assert.IsType<ForbidResult>(await controller.GetDeliveries(new DeliveryListFilter { ShopId = _otherShopId }));
    }

    [Fact]
    public async Task Sprint4Qa_CSP264_Spec_Operator_passing_another_agencys_rep_gets_403()
    {
        await SeedAssigned();
        var (controller, db) = Controller("AgencyOperator", Operator());
        await using (db)
            Assert.IsType<ForbidResult>(await controller.GetDeliveries(new DeliveryListFilter { SalesRepId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Sprint4Qa_CSP264_Operator_sees_only_own_agency_and_handovers_are_hidden()
    {
        var own = await SeedPending();
        var handover = await SeedPending("ImmediateCashSale");
        await using (var seed = NewDb())
        {
            seed.DeliveryJobs.Add(DeliveryJob.CreatePending(_companyId, "DL-261010-OTHER1", Guid.NewGuid(), "ORD-QA-OTHER",
                "ScheduledDelivery", _otherShopId, "Other", "Other", "o@qa.test", _otherAgencyId, "Other Agency",
                "oa@qa.test", _otherTerritoryId, _provinceId, _otherRepId, 10m, "LKR", Array.Empty<DeliveryJobLine>()));
            await seed.SaveChangesAsync();
        }
        var (controller, db) = Controller("AgencyOperator", Operator());
        await using (db)
        {
            var result = Assert.IsType<OkObjectResult>(await controller.GetDeliveries(new DeliveryListFilter { Status = new[] { "Pending" } }));
            var page = Assert.IsType<DeliveryListResponse>(result.Value);
            Assert.Contains(page.Items, i => i.Id == own);
            Assert.DoesNotContain(page.Items, i => i.Id == handover);
            Assert.All(page.Items, i => Assert.Equal("QA Agency", i.AgencyName));
        }
    }
}

/// <summary>US-E6-3-BE1: the transition table over all 36 from/to pairs, as written in the Sprint 4 backlog.</summary>
public sealed class DeliveryTransitionSprint4QaTests
{
    private static readonly HashSet<(DeliveryStatus, DeliveryStatus)> Backlog = new()
    {
        (DeliveryStatus.Pending, DeliveryStatus.Assigned),
        (DeliveryStatus.Assigned, DeliveryStatus.InTransit),
        (DeliveryStatus.InTransit, DeliveryStatus.Delivered),
        (DeliveryStatus.InTransit, DeliveryStatus.Failed),
        (DeliveryStatus.Pending, DeliveryStatus.Cancelled),
        (DeliveryStatus.Assigned, DeliveryStatus.Cancelled),
        (DeliveryStatus.InTransit, DeliveryStatus.Cancelled),
    };

    public static IEnumerable<object[]> AllPairs() =>
        from source in Enum.GetValues<DeliveryStatus>()
        from target in Enum.GetValues<DeliveryStatus>()
        select new object[] { source, target };

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Sprint4Qa_CSP263_Transition_table_matches_the_backlog(DeliveryStatus source, DeliveryStatus target) =>
        Assert.Equal(Backlog.Contains((source, target)), DeliveryTransitions.IsAllowed(source, target));

    [Fact]
    public void Sprint4Qa_CSP263_Table_has_exactly_36_pairs_and_7_allowed() =>
        Assert.Equal(7, AllPairs().Count(p => DeliveryTransitions.IsAllowed((DeliveryStatus)p[0], (DeliveryStatus)p[1])));
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.DeliveryService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "delivery_jobs",
                columns: table => new
                {
                    delivery_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    fulfilment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    shop_owner_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    shop_owner_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agency_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    agency_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    territory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    province_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_sales_rep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assigned_rep_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_rep_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    scheduled_date = table.Column<DateOnly>(type: "date", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_jobs", x => x.delivery_job_id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    outbox_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    message_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.outbox_message_id);
                });

            migrationBuilder.CreateTable(
                name: "processed_delivery_events",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_delivery_events", x => new { x.company_id, x.event_id });
                });

            migrationBuilder.CreateTable(
                name: "delivery_job_lines",
                columns: table => new
                {
                    delivery_job_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_job_lines", x => x.delivery_job_line_id);
                    table.ForeignKey(
                        name: "FK_delivery_job_lines_delivery_jobs_delivery_job_id",
                        column: x => x.delivery_job_id,
                        principalTable: "delivery_jobs",
                        principalColumn: "delivery_job_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "delivery_status_history",
                columns: table => new
                {
                    delivery_status_history_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    actor_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_status_history", x => x.delivery_status_history_id);
                    table.ForeignKey(
                        name: "FK_delivery_status_history_delivery_jobs_delivery_job_id",
                        column: x => x.delivery_job_id,
                        principalTable: "delivery_jobs",
                        principalColumn: "delivery_job_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_job_lines_job_id",
                table: "delivery_job_lines",
                column: "delivery_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_jobs_company_order",
                table: "delivery_jobs",
                columns: new[] { "company_id", "order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_jobs_delivery_reference",
                table: "delivery_jobs",
                column: "delivery_reference");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_status_history_job_id",
                table: "delivery_status_history",
                column: "delivery_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                table: "outbox_messages",
                columns: new[] { "processed_at", "created_at" },
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_job_lines");

            migrationBuilder.DropTable(
                name: "delivery_status_history");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "processed_delivery_events");

            migrationBuilder.DropTable(
                name: "delivery_jobs");
        }
    }
}

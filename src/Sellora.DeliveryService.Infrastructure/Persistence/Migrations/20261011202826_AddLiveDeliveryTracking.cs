using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.DeliveryService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveDeliveryTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "last_latitude",
                table: "delivery_jobs",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_location_at",
                table: "delivery_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "last_longitude",
                table: "delivery_jobs",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "delivery_locations",
                columns: table => new
                {
                    delivery_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    accuracy_meters = table.Column<double>(type: "double precision", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_locations", x => x.delivery_location_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_locations_company_id",
                table: "delivery_locations",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_locations_delivery_job_id_captured_at",
                table: "delivery_locations",
                columns: new[] { "delivery_job_id", "captured_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_locations");

            migrationBuilder.DropColumn(
                name: "last_latitude",
                table: "delivery_jobs");

            migrationBuilder.DropColumn(
                name: "last_location_at",
                table: "delivery_jobs");

            migrationBuilder.DropColumn(
                name: "last_longitude",
                table: "delivery_jobs");
        }
    }
}

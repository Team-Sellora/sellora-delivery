using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.DeliveryService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "return_requests",
                columns: table => new
                {
                    return_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    delivery_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    territory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    province_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requested_by_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decided_by_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    inventory_owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_requests", x => x.return_request_id);
                });

            migrationBuilder.CreateTable(
                name: "return_request_lines",
                columns: table => new
                {
                    return_request_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_request_lines", x => x.return_request_line_id);
                    table.ForeignKey(
                        name: "FK_return_request_lines_return_requests_return_request_id",
                        column: x => x.return_request_id,
                        principalTable: "return_requests",
                        principalColumn: "return_request_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_return_request_lines_product_id",
                table: "return_request_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_request_lines_return_id",
                table: "return_request_lines",
                column: "return_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_requests_agency_status",
                table: "return_requests",
                columns: new[] { "company_id", "agency_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_return_requests_company_delivery_job",
                table: "return_requests",
                columns: new[] { "company_id", "delivery_job_id" });

            migrationBuilder.CreateIndex(
                name: "ix_return_requests_reference",
                table: "return_requests",
                column: "return_reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "return_request_lines");

            migrationBuilder.DropTable(
                name: "return_requests");
        }
    }
}

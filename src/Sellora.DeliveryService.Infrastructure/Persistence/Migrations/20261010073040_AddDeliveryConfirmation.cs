using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.DeliveryService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliveryConfirmations",
                columns: table => new
                {
                    DeliveryConfirmationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SignatureUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryConfirmations", x => x.DeliveryConfirmationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryConfirmations_DeliveryJobId",
                table: "DeliveryConfirmations",
                column: "DeliveryJobId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryConfirmations");
        }
    }
}

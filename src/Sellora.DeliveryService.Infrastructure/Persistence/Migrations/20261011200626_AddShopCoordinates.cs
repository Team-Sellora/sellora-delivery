using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.DeliveryService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShopCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "shop_latitude",
                table: "delivery_jobs",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "shop_longitude",
                table: "delivery_jobs",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "shop_latitude",
                table: "delivery_jobs");

            migrationBuilder.DropColumn(
                name: "shop_longitude",
                table: "delivery_jobs");
        }
    }
}

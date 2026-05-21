using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrbanRoadworks.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RoadworkRoads_site_id",
                table: "RoadworkRoads",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "IX_RoadworkAssets_site_id",
                table: "RoadworkAssets",
                column: "site_id");

            migrationBuilder.AddForeignKey(
                name: "FK_RoadworkAssets_RoadworkSites_site_id",
                table: "RoadworkAssets",
                column: "site_id",
                principalTable: "RoadworkSites",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_RoadworkRoads_RoadworkSites_site_id",
                table: "RoadworkRoads",
                column: "site_id",
                principalTable: "RoadworkSites",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoadworkAssets_RoadworkSites_site_id",
                table: "RoadworkAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_RoadworkRoads_RoadworkSites_site_id",
                table: "RoadworkRoads");

            migrationBuilder.DropIndex(
                name: "IX_RoadworkRoads_site_id",
                table: "RoadworkRoads");

            migrationBuilder.DropIndex(
                name: "IX_RoadworkAssets_site_id",
                table: "RoadworkAssets");
        }
    }
}

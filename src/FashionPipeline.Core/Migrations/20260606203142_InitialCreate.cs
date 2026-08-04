using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionPipeline.Core.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accessories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    RawImageUri = table.Column<string>(type: "TEXT", nullable: false),
                    ImageHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExtractedFeatures = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accessories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GeneratedAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccessoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetType = table.Column<string>(type: "TEXT", nullable: false),
                    AssetUri = table.Column<string>(type: "TEXT", nullable: false),
                    PromptUsed = table.Column<string>(type: "TEXT", nullable: false),
                    IsApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedAssets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedAssets_AccessoryId_PromptUsed",
                table: "GeneratedAssets",
                columns: new[] { "AccessoryId", "PromptUsed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accessories");

            migrationBuilder.DropTable(
                name: "GeneratedAssets");
        }
    }
}

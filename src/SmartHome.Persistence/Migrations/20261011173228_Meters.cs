using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartHome.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Meters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MeterDays",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reading = table.Column<decimal>(type: "TEXT", nullable: false),
                    Entered = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeterDays", x => new { x.EntityId, x.Date });
                });

            migrationBuilder.CreateTable(
                name: "Meters",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "TEXT", nullable: false),
                    Reading = table.Column<decimal>(type: "TEXT", nullable: true),
                    Counter = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EnteredOn = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meters", x => x.EntityId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MeterDays");

            migrationBuilder.DropTable(
                name: "Meters");
        }
    }
}

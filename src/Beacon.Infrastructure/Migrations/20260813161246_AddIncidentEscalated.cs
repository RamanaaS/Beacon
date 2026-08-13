using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beacon.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentEscalated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EscalatedTo",
                table: "IncidentEvents",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EscalatedTo",
                table: "IncidentEvents");
        }
    }
}

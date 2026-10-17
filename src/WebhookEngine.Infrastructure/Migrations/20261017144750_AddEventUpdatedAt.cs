using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Events",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Events");
        }
    }
}

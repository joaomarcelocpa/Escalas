using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Escalas.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ExpedienteDia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Expedientes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Inicio = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    Fim = table.Column<TimeOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expedientes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expedientes_Data",
                table: "Expedientes",
                column: "Data",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Expedientes");
        }
    }
}

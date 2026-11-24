using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Escalas.Infrastructure.Persistencia.Migrations
{
    /// <summary>
    /// O expediente deixou de ser por data e passou a ser por dia da semana (definido na escala semanal).
    /// Horários por data não têm equivalente direto, então a tabela antiga é descartada e a nova começa vazia.
    /// </summary>
    public partial class ExpedientePorDiaSemana : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Expedientes");

            migrationBuilder.CreateTable(
                name: "ExpedientesSemana",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DiaSemana = table.Column<int>(type: "INTEGER", nullable: false),
                    Inicio = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    Fim = table.Column<TimeOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpedientesSemana", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpedientesSemana_DiaSemana",
                table: "ExpedientesSemana",
                column: "DiaSemana",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ExpedientesSemana");

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
    }
}

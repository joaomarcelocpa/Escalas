using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Escalas.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class FilialExtraCobertura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FilialExtra",
                table: "Funcionarios",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FilialCobertura",
                table: "EscalasPadrao",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilialExtra",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "FilialCobertura",
                table: "EscalasPadrao");
        }
    }
}

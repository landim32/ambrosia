using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ambrosio.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddMaisVendasCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "credentials_saved_at",
                table: "mais_vendas_accounts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "credentials_status",
                table: "mais_vendas_accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "login_encrypted",
                table: "mais_vendas_accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "password_changed_at",
                table: "mais_vendas_accounts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "password_encrypted",
                table: "mais_vendas_accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "kind",
                table: "import_runs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "started_via",
                table: "import_runs",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "credentials_saved_at",
                table: "mais_vendas_accounts");

            migrationBuilder.DropColumn(
                name: "credentials_status",
                table: "mais_vendas_accounts");

            migrationBuilder.DropColumn(
                name: "login_encrypted",
                table: "mais_vendas_accounts");

            migrationBuilder.DropColumn(
                name: "password_changed_at",
                table: "mais_vendas_accounts");

            migrationBuilder.DropColumn(
                name: "password_encrypted",
                table: "mais_vendas_accounts");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "import_runs");

            migrationBuilder.DropColumn(
                name: "started_via",
                table: "import_runs");
        }
    }
}

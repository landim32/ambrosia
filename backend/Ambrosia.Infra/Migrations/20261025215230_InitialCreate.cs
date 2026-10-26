using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ambrosia.Infra.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    email = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    provider_subject = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("users_pkey", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    customer_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cpf_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cpf_encrypted = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    cpf_masked = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("customers_pkey", x => x.customer_id);
                    table.ForeignKey(
                        name: "fk_user_customer",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    order_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    order_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    order_date = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    origin = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    original_sale_on = table.Column<DateOnly>(type: "date", nullable: true),
                    cancelled_on = table.Column<DateOnly>(type: "date", nullable: true),
                    external_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("orders_pkey", x => x.order_id);
                    table.ForeignKey(
                        name: "fk_customer_order",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "fk_user_order",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    order_item_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sku_ref = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    size = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    price = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("order_items_pkey", x => x.order_item_id);
                    table.ForeignKey(
                        name: "fk_order_order_item",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "order_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_customers_user",
                table: "customers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_user_name",
                table: "customers",
                columns: new[] { "user_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ux_customers_user_cpf",
                table: "customers",
                columns: new[] { "user_id", "cpf_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_items_order",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_sku",
                table: "order_items",
                column: "sku_ref");

            migrationBuilder.CreateIndex(
                name: "ux_order_items_order_line",
                table: "order_items",
                columns: new[] { "order_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer",
                table: "orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_user",
                table: "orders",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_user_customer",
                table: "orders",
                columns: new[] { "user_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_user_date",
                table: "orders",
                columns: new[] { "user_id", "order_date" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_orders_user_number_date_kind",
                table: "orders",
                columns: new[] { "user_id", "order_number", "order_date", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_email",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}

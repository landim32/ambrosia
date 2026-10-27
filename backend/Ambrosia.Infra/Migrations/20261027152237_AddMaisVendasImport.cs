using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ambrosia.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddMaisVendasImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "captured_at",
                table: "orders",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "source_changed_at",
                table: "orders",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_hash",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "image_id",
                table: "order_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "import_runs",
                columns: table => new
                {
                    import_run_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    block_days = table.Column<int>(type: "integer", nullable: false),
                    blocks_total = table.Column<int>(type: "integer", nullable: false),
                    cancel_requested = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    requested_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    inserted_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    updated_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    unchanged_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    missing_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    failed_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    conflict_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    customers_created_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    images_stored_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    images_failed_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    collector_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("import_runs_pkey", x => x.import_run_id);
                    table.ForeignKey(
                        name: "fk_user_import_run",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "mais_vendas_accounts",
                columns: table => new
                {
                    mais_vendas_account_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    account_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    linked_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    failed_login_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    locked_until = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("mais_vendas_accounts_pkey", x => x.mais_vendas_account_id);
                    table.ForeignKey(
                        name: "fk_user_mais_vendas_account",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "import_run_blocks",
                columns: table => new
                {
                    import_run_block_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    import_run_id = table.Column<long>(type: "bigint", nullable: false),
                    block_no = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    inserted_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    updated_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    unchanged_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    missing_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    failed_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    conflict_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    customers_created_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    images_stored_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    images_failed_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("import_run_blocks_pkey", x => x.import_run_block_id);
                    table.ForeignKey(
                        name: "fk_import_run_import_run_block",
                        column: x => x.import_run_id,
                        principalTable: "import_runs",
                        principalColumn: "import_run_id");
                });

            migrationBuilder.CreateTable(
                name: "import_run_changes",
                columns: table => new
                {
                    import_run_change_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    import_run_id = table.Column<long>(type: "bigint", nullable: false),
                    import_run_block_id = table.Column<long>(type: "bigint", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: true),
                    order_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    order_date = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    origin = table.Column<int>(type: "integer", nullable: true),
                    action = table.Column<int>(type: "integer", nullable: false),
                    changed_fields = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("import_run_changes_pkey", x => x.import_run_change_id);
                    table.ForeignKey(
                        name: "fk_import_run_block_import_run_change",
                        column: x => x.import_run_block_id,
                        principalTable: "import_run_blocks",
                        principalColumn: "import_run_block_id");
                    table.ForeignKey(
                        name: "fk_import_run_import_run_change",
                        column: x => x.import_run_id,
                        principalTable: "import_runs",
                        principalColumn: "import_run_id");
                    table.ForeignKey(
                        name: "fk_order_import_run_change",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "order_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_orders_user_source_date",
                table: "orders",
                columns: new[] { "user_id", "source", "order_date" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_order_items_image",
                table: "order_items",
                column: "image_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_run_blocks_run",
                table: "import_run_blocks",
                column: "import_run_id");

            migrationBuilder.CreateIndex(
                name: "ux_import_run_blocks_run_no",
                table: "import_run_blocks",
                columns: new[] { "import_run_id", "block_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_run_changes_block",
                table: "import_run_changes",
                column: "import_run_block_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_run_changes_order",
                table: "import_run_changes",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_run_changes_run",
                table: "import_run_changes",
                column: "import_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_runs_user_period",
                table: "import_runs",
                columns: new[] { "user_id", "start_date", "end_date", "finished_at" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_import_runs_user_requested",
                table: "import_runs",
                columns: new[] { "user_id", "requested_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_import_runs_user_active",
                table: "import_runs",
                column: "user_id",
                unique: true,
                filter: "status IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ux_mais_vendas_accounts_user",
                table: "mais_vendas_accounts",
                column: "user_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_stored_image_order_item",
                table: "order_items",
                column: "image_id",
                principalTable: "stored_images",
                principalColumn: "stored_image_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_stored_image_order_item",
                table: "order_items");

            migrationBuilder.DropTable(
                name: "import_run_changes");

            migrationBuilder.DropTable(
                name: "mais_vendas_accounts");

            migrationBuilder.DropTable(
                name: "import_run_blocks");

            migrationBuilder.DropTable(
                name: "import_runs");

            migrationBuilder.DropIndex(
                name: "ix_orders_user_source_date",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_order_items_image",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "captured_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "source",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "source_changed_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "source_hash",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "image_id",
                table: "order_items");
        }
    }
}

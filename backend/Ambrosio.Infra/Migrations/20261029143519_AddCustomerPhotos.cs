using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ambrosio.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "profile_image_id",
                table: "customers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "profile_photo_id",
                table: "customers",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customer_photos",
                columns: table => new
                {
                    customer_photo_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    image_id = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_favorite = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("customer_photos_pkey", x => x.customer_photo_id);
                    table.ForeignKey(
                        name: "fk_customer_customer_photo",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "fk_stored_image_customer_photo",
                        column: x => x.image_id,
                        principalTable: "stored_images",
                        principalColumn: "stored_image_id");
                    table.ForeignKey(
                        name: "fk_user_customer_photo",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_customers_profile_image",
                table: "customers",
                column: "profile_image_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_profile_photo",
                table: "customers",
                column: "profile_photo_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_photos_customer",
                table: "customer_photos",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_photos_favorites",
                table: "customer_photos",
                columns: new[] { "user_id", "customer_id", "created_at", "customer_photo_id" },
                descending: new[] { false, false, true, true },
                filter: "is_favorite");

            migrationBuilder.CreateIndex(
                name: "ix_customer_photos_image",
                table: "customer_photos",
                column: "image_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_photos_user",
                table: "customer_photos",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_customer_photos_customer_image",
                table: "customer_photos",
                columns: new[] { "customer_id", "image_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_customer_photo_customer",
                table: "customers",
                column: "profile_photo_id",
                principalTable: "customer_photos",
                principalColumn: "customer_photo_id");

            migrationBuilder.AddForeignKey(
                name: "fk_profile_image_customer",
                table: "customers",
                column: "profile_image_id",
                principalTable: "stored_images",
                principalColumn: "stored_image_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_customer_photo_customer",
                table: "customers");

            migrationBuilder.DropForeignKey(
                name: "fk_profile_image_customer",
                table: "customers");

            migrationBuilder.DropTable(
                name: "customer_photos");

            migrationBuilder.DropIndex(
                name: "ix_customers_profile_image",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_customers_profile_photo",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "profile_image_id",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "profile_photo_id",
                table: "customers");
        }
    }
}

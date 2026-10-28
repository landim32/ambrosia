using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ambrosio.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddOAuthServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mcp_audit_entries",
                columns: table => new
                {
                    mcp_audit_entry_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    tool_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    client_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    arguments = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    result_summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("mcp_audit_entries_pkey", x => x.mcp_audit_entry_id);
                    table.ForeignKey(
                        name: "fk_user_mcp_audit_entry",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "oauth_applications",
                columns: table => new
                {
                    oauth_application_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    registration_source = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    metadata_fetched_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    application_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    client_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    client_secret = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    client_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    consent_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    json_web_key_set = table.Column<string>(type: "text", nullable: true),
                    permissions = table.Column<string>(type: "text", nullable: true),
                    post_logout_redirect_uris = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redirect_uris = table.Column<string>(type: "text", nullable: true),
                    requirements = table.Column<string>(type: "text", nullable: true),
                    settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("oauth_applications_pkey", x => x.oauth_application_id);
                });

            migrationBuilder.CreateTable(
                name: "oauth_scopes",
                columns: table => new
                {
                    oauth_scope_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    descriptions = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    properties = table.Column<string>(type: "text", nullable: true),
                    resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("oauth_scopes_pkey", x => x.oauth_scope_id);
                });

            migrationBuilder.CreateTable(
                name: "oauth_authorizations",
                columns: table => new
                {
                    oauth_authorization_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    oauth_application_id = table.Column<long>(type: "bigint", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    scopes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("oauth_authorizations_pkey", x => x.oauth_authorization_id);
                    table.ForeignKey(
                        name: "fk_oauth_application_oauth_authorization",
                        column: x => x.oauth_application_id,
                        principalTable: "oauth_applications",
                        principalColumn: "oauth_application_id");
                });

            migrationBuilder.CreateTable(
                name: "oauth_tokens",
                columns: table => new
                {
                    oauth_token_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    oauth_application_id = table.Column<long>(type: "bigint", nullable: true),
                    oauth_authorization_id = table.Column<long>(type: "bigint", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    expiration_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    payload = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redemption_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    reference_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("oauth_tokens_pkey", x => x.oauth_token_id);
                    table.ForeignKey(
                        name: "fk_oauth_application_oauth_token",
                        column: x => x.oauth_application_id,
                        principalTable: "oauth_applications",
                        principalColumn: "oauth_application_id");
                    table.ForeignKey(
                        name: "fk_oauth_authorization_oauth_token",
                        column: x => x.oauth_authorization_id,
                        principalTable: "oauth_authorizations",
                        principalColumn: "oauth_authorization_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_mcp_audit_entries_user_created",
                table: "mcp_audit_entries",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_oauth_applications_client_id",
                table: "oauth_applications",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_oauth_authorizations_application",
                table: "oauth_authorizations",
                column: "oauth_application_id");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_authorizations_creation_date",
                table: "oauth_authorizations",
                column: "creation_date");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_authorizations_subject_status_type",
                table: "oauth_authorizations",
                columns: new[] { "subject", "status", "type" });

            migrationBuilder.CreateIndex(
                name: "ux_oauth_scopes_name",
                table: "oauth_scopes",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_oauth_tokens_application",
                table: "oauth_tokens",
                column: "oauth_application_id");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_tokens_authorization",
                table: "oauth_tokens",
                column: "oauth_authorization_id");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_tokens_expiration_date",
                table: "oauth_tokens",
                column: "expiration_date");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_tokens_reference_id",
                table: "oauth_tokens",
                column: "reference_id");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_tokens_status_type",
                table: "oauth_tokens",
                columns: new[] { "status", "type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mcp_audit_entries");

            migrationBuilder.DropTable(
                name: "oauth_scopes");

            migrationBuilder.DropTable(
                name: "oauth_tokens");

            migrationBuilder.DropTable(
                name: "oauth_authorizations");

            migrationBuilder.DropTable(
                name: "oauth_applications");
        }
    }
}

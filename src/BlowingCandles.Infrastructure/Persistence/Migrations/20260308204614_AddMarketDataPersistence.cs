using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BlowingCandles.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketDataPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "market_data_refresh_run",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    requested_as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requested_symbol_count = table.Column<int>(type: "integer", nullable: false),
                    persisted_symbol_count = table.Column<int>(type: "integer", nullable: false),
                    missing_symbol_count = table.Column<int>(type: "integer", nullable: false),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error_message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_data_refresh_run", x => x.id);
                    table.CheckConstraint("ck_market_data_refresh_run_missing_symbol_count_non_negative", "\"missing_symbol_count\" >= 0");
                    table.CheckConstraint("ck_market_data_refresh_run_persisted_symbol_count_non_negative", "\"persisted_symbol_count\" >= 0");
                    table.CheckConstraint("ck_market_data_refresh_run_requested_symbol_count_non_negative", "\"requested_symbol_count\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "market_data_snapshot",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    refresh_run_id = table.Column<long>(type: "bigint", nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    freshness_ttl_seconds = table.Column<int>(type: "integer", nullable: false),
                    fresh_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    first_quote_date = table.Column<DateOnly>(type: "date", nullable: false),
                    last_quote_date = table.Column<DateOnly>(type: "date", nullable: false),
                    quote_row_count = table.Column<int>(type: "integer", nullable: false),
                    covered_symbol_count = table.Column<int>(type: "integer", nullable: false),
                    missing_symbol_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_data_snapshot", x => x.id);
                    table.CheckConstraint("ck_market_data_snapshot_covered_symbol_count_non_negative", "\"covered_symbol_count\" >= 0");
                    table.CheckConstraint("ck_market_data_snapshot_freshness_ttl_seconds_positive", "\"freshness_ttl_seconds\" > 0");
                    table.CheckConstraint("ck_market_data_snapshot_missing_symbol_count_non_negative", "\"missing_symbol_count\" >= 0");
                    table.CheckConstraint("ck_market_data_snapshot_quote_row_count_non_negative", "\"quote_row_count\" >= 0");
                    table.ForeignKey(
                        name: "FK_market_data_snapshot_market_data_refresh_run_refresh_run_id",
                        column: x => x.refresh_run_id,
                        principalTable: "market_data_refresh_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "market_data_snapshot_missing_symbol",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    snapshot_id = table.Column<long>(type: "bigint", nullable: false),
                    symbol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    detail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    is_retryable = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_data_snapshot_missing_symbol", x => x.id);
                    table.ForeignKey(
                        name: "FK_market_data_snapshot_missing_symbol_market_data_snapshot_sn~",
                        column: x => x.snapshot_id,
                        principalTable: "market_data_snapshot",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "market_data_snapshot_quote",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    snapshot_id = table.Column<long>(type: "bigint", nullable: false),
                    symbol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quote_date = table.Column<DateOnly>(type: "date", nullable: false),
                    market_timestamp_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    open = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    high = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    low = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    close = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    volume = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_data_snapshot_quote", x => x.id);
                    table.CheckConstraint("ck_market_data_snapshot_quote_volume_non_negative", "\"volume\" >= 0");
                    table.ForeignKey(
                        name: "FK_market_data_snapshot_quote_market_data_snapshot_snapshot_id",
                        column: x => x.snapshot_id,
                        principalTable: "market_data_snapshot",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_market_data_refresh_run_requested_as_of_date_started_at_utc",
                table: "market_data_refresh_run",
                columns: new[] { "requested_as_of_date", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_market_data_refresh_run_status_started_at_utc",
                table: "market_data_refresh_run",
                columns: new[] { "status", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_market_data_snapshot_as_of_date_captured_at_utc",
                table: "market_data_snapshot",
                columns: new[] { "as_of_date", "captured_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_market_data_snapshot_fresh_until_utc",
                table: "market_data_snapshot",
                column: "fresh_until_utc");

            migrationBuilder.CreateIndex(
                name: "ix_market_data_snapshot_refresh_run_id",
                table: "market_data_snapshot",
                column: "refresh_run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_market_data_snapshot_missing_symbol_snapshot_id_symbol",
                table: "market_data_snapshot_missing_symbol",
                columns: new[] { "snapshot_id", "symbol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_market_data_snapshot_quote_snapshot_id_symbol_quote_date",
                table: "market_data_snapshot_quote",
                columns: new[] { "snapshot_id", "symbol", "quote_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_market_data_snapshot_quote_symbol_quote_date",
                table: "market_data_snapshot_quote",
                columns: new[] { "symbol", "quote_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_data_snapshot_missing_symbol");

            migrationBuilder.DropTable(
                name: "market_data_snapshot_quote");

            migrationBuilder.DropTable(
                name: "market_data_snapshot");

            migrationBuilder.DropTable(
                name: "market_data_refresh_run");
        }
    }
}

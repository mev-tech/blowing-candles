using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BlowingCandles.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignalRunPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "signal_run",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_simulation = table.Column<bool>(type: "boolean", nullable: false),
                    ticker_count = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signal_run", x => x.id);
                    table.CheckConstraint("ck_signal_run_ticker_count_non_negative", "\"ticker_count\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "trade_governor_state",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    day = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    buys_today = table.Column<int>(type: "integer", nullable: false),
                    last_buy_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_governor_state", x => x.id);
                    table.CheckConstraint("ck_trade_governor_state_buys_today_non_negative", "\"buys_today\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "signal_run_result",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_id = table.Column<long>(type: "bigint", nullable: false),
                    ticker = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    news_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    market_action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signal_run_result", x => x.id);
                    table.ForeignKey(
                        name: "FK_signal_run_result_signal_run_run_id",
                        column: x => x.run_id,
                        principalTable: "signal_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_signal_run_is_simulation_completed_at_utc",
                table: "signal_run",
                columns: new[] { "is_simulation", "completed_at_utc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_signal_run_run_type_as_of_date",
                table: "signal_run",
                columns: new[] { "run_type", "as_of_date" });

            migrationBuilder.CreateIndex(
                name: "ix_signal_run_status_started_at_utc",
                table: "signal_run",
                columns: new[] { "status", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_signal_run_result_run_id",
                table: "signal_run_result",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_signal_run_result_run_id_ticker",
                table: "signal_run_result",
                columns: new[] { "run_id", "ticker" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trade_governor_state_mode",
                table: "trade_governor_state",
                column: "mode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "signal_run_result");

            migrationBuilder.DropTable(
                name: "trade_governor_state");

            migrationBuilder.DropTable(
                name: "signal_run");
        }
    }
}

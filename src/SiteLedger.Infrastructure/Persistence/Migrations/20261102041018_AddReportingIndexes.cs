using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiteLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Expenses_ProjectId",
                table: "Expenses");

            migrationBuilder.CreateIndex(
                name: "IX_Timesheets_ProjectId_Status",
                table: "Timesheets",
                columns: new[] { "ProjectId", "Status" })
                .Annotation("SqlServer:Include", new[] { "LaborCostUsd" });

            migrationBuilder.CreateIndex(
                name: "IX_Timesheets_ProjectId_SubmittedAtUtc",
                table: "Timesheets",
                columns: new[] { "ProjectId", "SubmittedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Status_ProjectCode",
                table: "Projects",
                columns: new[] { "Status", "ProjectCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ProjectId_ExpenseDate",
                table: "Expenses",
                columns: new[] { "ProjectId", "ExpenseDate" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ProjectId_Status",
                table: "Expenses",
                columns: new[] { "ProjectId", "Status" })
                .Annotation("SqlServer:Include", new[] { "AmountUsd" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Timesheets_ProjectId_Status",
                table: "Timesheets");

            migrationBuilder.DropIndex(
                name: "IX_Timesheets_ProjectId_SubmittedAtUtc",
                table: "Timesheets");

            migrationBuilder.DropIndex(
                name: "IX_Projects_Status_ProjectCode",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_ProjectId_ExpenseDate",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_ProjectId_Status",
                table: "Expenses");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ProjectId",
                table: "Expenses",
                column: "ProjectId");
        }
    }
}

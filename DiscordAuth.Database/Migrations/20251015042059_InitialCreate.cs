using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordAuth.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Divisions",
                columns: table => new
                {
                    Snowflake = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Division = table.Column<string>(type: "text", nullable: false),
                    MemberRole = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    StaffRole = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    HqStaffRole = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    GcaRole = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    VisitorRole = table.Column<decimal>(type: "numeric(20,0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Divisions", x => x.Snowflake);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Vid = table.Column<string>(type: "text", nullable: false),
                    Snowflake = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Vid);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Divisions");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}

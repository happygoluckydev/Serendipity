using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Serendipity.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DomainRules",
                columns: table => new
                {
                    Domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DomainRules", x => x.Domain);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MarkId = table.Column<Guid>(type: "uuid", nullable: true),
                    PopId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReporterKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Contact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Weight = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Saves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    MarkId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Saves", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Trust = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsKidsMode = table.Column<bool>(type: "boolean", nullable: false),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsKidsMode = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.KeyHash);
                    table.ForeignKey(
                        name: "FK_Devices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Marks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Shape = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Genre = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LinkUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PendingReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Weight = table.Column<double>(type: "double precision", nullable: false),
                    SeedX = table.Column<double>(type: "double precision", nullable: false),
                    SeedY = table.Column<double>(type: "double precision", nullable: false),
                    HasPr = table.Column<bool>(type: "boolean", nullable: false),
                    LinkBroken = table.Column<bool>(type: "boolean", nullable: false),
                    ShopId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastEngagedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GoneAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Marks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Marks_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Shops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    MarkId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shops_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Adjacencies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MarkAId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarkBId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Adjacencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Adjacencies_Marks_MarkAId",
                        column: x => x.MarkAId,
                        principalTable: "Marks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Adjacencies_Marks_MarkBId",
                        column: x => x.MarkBId,
                        principalTable: "Marks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Engagements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MarkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Engagements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Engagements_Marks_MarkId",
                        column: x => x.MarkId,
                        principalTable: "Marks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarkId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PhotoKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    HasPr = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pops_Marks_MarkId",
                        column: x => x.MarkId,
                        principalTable: "Marks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Pops_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Previews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SiteName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ImageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HasPr = table.Column<bool>(type: "boolean", nullable: false),
                    FetchError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Previews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Previews_Marks_MarkId",
                        column: x => x.MarkId,
                        principalTable: "Marks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShopItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaveId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShopItems_Saves_SaveId",
                        column: x => x.SaveId,
                        principalTable: "Saves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShopItems_Shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "Shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Adjacencies_MarkAId",
                table: "Adjacencies",
                column: "MarkAId");

            migrationBuilder.CreateIndex(
                name: "IX_Adjacencies_MarkAId_MarkBId",
                table: "Adjacencies",
                columns: new[] { "MarkAId", "MarkBId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Adjacencies_MarkBId",
                table: "Adjacencies",
                column: "MarkBId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_UserId",
                table: "Devices",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Engagements_ActorKey_MarkId_Kind_CreatedAt",
                table: "Engagements",
                columns: new[] { "ActorKey", "MarkId", "Kind", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Engagements_MarkId_CreatedAt",
                table: "Engagements",
                columns: new[] { "MarkId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Marks_OwnerUserId",
                table: "Marks",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Marks_SeedX_SeedY",
                table: "Marks",
                columns: new[] { "SeedX", "SeedY" });

            migrationBuilder.CreateIndex(
                name: "IX_Marks_Status",
                table: "Marks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Pops_AuthorUserId",
                table: "Pops",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Pops_MarkId",
                table: "Pops",
                column: "MarkId");

            migrationBuilder.CreateIndex(
                name: "IX_Pops_Status",
                table: "Pops",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Previews_MarkId",
                table: "Previews",
                column: "MarkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reports_MarkId",
                table: "Reports",
                column: "MarkId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Status",
                table: "Reports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Saves_DeviceKeyHash_MarkId",
                table: "Saves",
                columns: new[] { "DeviceKeyHash", "MarkId" },
                unique: true,
                filter: "\"DeviceKeyHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Saves_UserId_MarkId",
                table: "Saves",
                columns: new[] { "UserId", "MarkId" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShopItems_SaveId",
                table: "ShopItems",
                column: "SaveId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopItems_ShopId_SaveId",
                table: "ShopItems",
                columns: new[] { "ShopId", "SaveId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shops_OwnerUserId",
                table: "Shops",
                column: "OwnerUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Adjacencies");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "DomainRules");

            migrationBuilder.DropTable(
                name: "Engagements");

            migrationBuilder.DropTable(
                name: "Pops");

            migrationBuilder.DropTable(
                name: "Previews");

            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropTable(
                name: "ShopItems");

            migrationBuilder.DropTable(
                name: "Marks");

            migrationBuilder.DropTable(
                name: "Saves");

            migrationBuilder.DropTable(
                name: "Shops");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}

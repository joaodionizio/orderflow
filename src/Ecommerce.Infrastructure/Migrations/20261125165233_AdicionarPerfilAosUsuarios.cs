using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarPerfilAosUsuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Perfil",
                table: "Usuarios",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Usuarios_Perfil",
                table: "Usuarios",
                sql: "[Perfil] COLLATE Latin1_General_100_BIN2 IN (N'Admin', N'Operator') AND DATALENGTH([Perfil]) = DATALENGTH(RTRIM([Perfil]))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Usuarios_Perfil",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Perfil",
                table: "Usuarios");
        }
    }
}

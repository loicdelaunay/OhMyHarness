using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OhMyHarness.Core.Migrations
{
    /// <inheritdoc />
    public partial class PromptTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                });
            migrationBuilder.InsertData(table: "Templates", columns: new[] { "Id", "Name", "Content" }, values: new object[] { 1, "Web app", "Crée de zéro une application web autonome dans un seul fichier index.html.\n\nObjectif de l’application : [décris ton idée]\nPublic cible : [à préciser]\nFonctionnalités essentielles :\n- [fonctionnalité 1]\n- [fonctionnalité 2]\n- [fonctionnalité 3]\n\nContraintes :\n- Tout le HTML, le CSS et le JavaScript dans un seul fichier, sans CDN, dépendance externe, compilation ni backend.\n- Fonctionnement hors ligne en ouvrant directement index.html dans un navigateur.\n- Interface moderne, responsive, accessible au clavier ; états vides et erreurs explicites.\n- Sauvegarde locale si nécessaire avec localStorage, avec gestion de son indisponibilité.\n- Aucune clé API ni secret dans le fichier.\n- Fonctionnalités réellement utilisables, sans boutons factices.\n\nPose les questions indispensables si le besoin est incomplet. Fournis ensuite le fichier complet. Si un dossier source est associé et l’écriture autorisée, crée index.html dedans. Demande l’autorisation avant d’ouvrir le fichier dans le navigateur intégré pour le vérifier." });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Templates");
        }
    }
}

using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Command board lane requirements are unit-type and personnel-role based; the certification
	/// requirement concept was removed (never functional). Drops the orphaned link table.
	/// </summary>
	[Migration(87)]
	public class M0087_RemoveCommandDefinitionRoleCertsPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("commanddefinitionrolecerts").Exists())
			{
				Delete.Table("commanddefinitionrolecerts");
			}
		}

		public override void Down()
		{
			// Intentionally not recreated; the certification requirement feature was removed.
		}
	}
}

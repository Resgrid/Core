using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Command board lane requirements are unit-type and personnel-role based; the certification
	/// requirement concept was removed (never functional). Drops the orphaned link table.
	/// </summary>
	[Migration(87)]
	public class M0087_RemoveCommandDefinitionRoleCerts : Migration
	{
		public override void Up()
		{
			if (Schema.Table("CommandDefinitionRoleCerts").Exists())
			{
				Delete.Table("CommandDefinitionRoleCerts");
			}
		}

		public override void Down()
		{
			// Intentionally not recreated; the certification requirement feature was removed.
		}
	}
}

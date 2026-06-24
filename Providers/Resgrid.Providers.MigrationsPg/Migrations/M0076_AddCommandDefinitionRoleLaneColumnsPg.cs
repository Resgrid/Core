using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(76)]
	public class M0076_AddCommandDefinitionRoleLaneColumnsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("commanddefinitionroles").Column("lanetype").Exists())
			{
				Alter.Table("commanddefinitionroles")
					.AddColumn("lanetype").AsInt32().NotNullable().WithDefaultValue(0);
			}

			if (!Schema.Table("commanddefinitionroles").Column("sortorder").Exists())
			{
				Alter.Table("commanddefinitionroles")
					.AddColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0);
			}
		}

		public override void Down()
		{
			Delete.Column("lanetype").FromTable("commanddefinitionroles");
			Delete.Column("sortorder").FromTable("commanddefinitionroles");
		}
	}
}

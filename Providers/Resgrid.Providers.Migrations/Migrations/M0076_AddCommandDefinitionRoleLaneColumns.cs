using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(76)]
	public class M0076_AddCommandDefinitionRoleLaneColumns : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("CommandDefinitionRoles").Column("LaneType").Exists())
			{
				Alter.Table("CommandDefinitionRoles")
					.AddColumn("LaneType").AsInt32().NotNullable().WithDefaultValue(0);
			}

			if (!Schema.Table("CommandDefinitionRoles").Column("SortOrder").Exists())
			{
				Alter.Table("CommandDefinitionRoles")
					.AddColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0);
			}
		}

		public override void Down()
		{
			Delete.Column("LaneType").FromTable("CommandDefinitionRoles");
			Delete.Column("SortOrder").FromTable("CommandDefinitionRoles");
		}
	}
}

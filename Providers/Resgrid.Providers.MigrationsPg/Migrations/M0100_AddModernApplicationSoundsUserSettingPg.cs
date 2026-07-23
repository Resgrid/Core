using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(100)]
	public class M0100_AddModernApplicationSoundsUserSettingPg : Migration
	{
		public override void Up()
		{
			Alter.Table("UserProfiles".ToLower())
				.AddColumn("EnableModernApplicationSounds".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			Delete.Column("EnableModernApplicationSounds".ToLower()).FromTable("UserProfiles".ToLower());
		}
	}
}

using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(100)]
	public class M0100_AddModernApplicationSoundsUserSetting : Migration
	{
		public override void Up()
		{
			Alter.Table("UserProfiles")
				.AddColumn("EnableModernApplicationSounds").AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			Delete.Column("EnableModernApplicationSounds").FromTable("UserProfiles");
		}
	}
}

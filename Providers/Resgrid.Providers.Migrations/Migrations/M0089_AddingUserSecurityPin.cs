using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(89)]
	public class M0089_AddingUserSecurityPin : Migration
	{
		public override void Up()
		{
			Alter.Table("UserProfiles")
				.AddColumn("SecurityPin").AsString(512).Nullable()
				.AddColumn("SecurityPinEnabled").AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			Delete.Column("SecurityPin").FromTable("UserProfiles");
			Delete.Column("SecurityPinEnabled").FromTable("UserProfiles");
		}
	}
}

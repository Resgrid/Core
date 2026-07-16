using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(89)]
	public class M0089_AddingUserSecurityPinPg : Migration
	{
		public override void Up()
		{
			Alter.Table("UserProfiles".ToLower())
				.AddColumn("SecurityPin".ToLower()).AsCustom("citext").Nullable()
				.AddColumn("SecurityPinEnabled".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			Delete.Column("SecurityPin".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("SecurityPinEnabled".ToLower()).FromTable("UserProfiles".ToLower());
		}
	}
}

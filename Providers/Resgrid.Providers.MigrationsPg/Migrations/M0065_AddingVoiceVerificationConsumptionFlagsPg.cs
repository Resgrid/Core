using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(65)]
	public class M0065_AddingVoiceVerificationConsumptionFlagsPg : Migration
	{
		public override void Up()
		{
			Alter.Table("UserProfiles".ToLower())
				.AddColumn("MobileVerificationVoiceCodeConsumed".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
				.AddColumn("HomeVerificationVoiceCodeConsumed".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			Delete.Column("MobileVerificationVoiceCodeConsumed".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("HomeVerificationVoiceCodeConsumed".ToLower()).FromTable("UserProfiles".ToLower());
		}
	}
}

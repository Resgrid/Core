using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(48)]
	public class M0048_AddingVoiceVerificationConsumptionFlags : Migration
	{
		public override void Up()
		{
			Alter.Table("UserProfiles")
				.AddColumn("MobileVerificationVoiceCodeConsumed").AsBoolean().NotNullable().WithDefaultValue(false)
				.AddColumn("HomeVerificationVoiceCodeConsumed").AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			Delete.Column("MobileVerificationVoiceCodeConsumed").FromTable("UserProfiles");
			Delete.Column("HomeVerificationVoiceCodeConsumed").FromTable("UserProfiles");
		}
	}
}

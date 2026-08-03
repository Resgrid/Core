using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(110)]
	public class M0110_AddingVerificationSendWindowsPg : Migration
	{
		public override void Up()
		{
			Alter.Table("UserProfiles".ToLower())
				.AddColumn("EmailVerificationSendCount".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("MobileVerificationSendCount".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("HomeVerificationSendCount".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("EmailVerificationSendWindowStart".ToLower()).AsDateTime().Nullable()
				.AddColumn("MobileVerificationSendWindowStart".ToLower()).AsDateTime().Nullable()
				.AddColumn("HomeVerificationSendWindowStart".ToLower()).AsDateTime().Nullable();
		}

		public override void Down()
		{
			Delete.Column("EmailVerificationSendCount".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("MobileVerificationSendCount".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("HomeVerificationSendCount".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("EmailVerificationSendWindowStart".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("MobileVerificationSendWindowStart".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("HomeVerificationSendWindowStart".ToLower()).FromTable("UserProfiles".ToLower());
		}
	}
}

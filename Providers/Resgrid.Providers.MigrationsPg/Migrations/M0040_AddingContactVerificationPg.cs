using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(40)]
	public class M0040_AddingContactVerificationPg : Migration
	{
		public override void Up()
		{
			Alter.Table("UserProfiles".ToLower())
				.AddColumn("EmailVerified".ToLower()).AsBoolean().Nullable()
				.AddColumn("MobileNumberVerified".ToLower()).AsBoolean().Nullable()
				.AddColumn("HomeNumberVerified".ToLower()).AsBoolean().Nullable()
				.AddColumn("EmailVerificationCode".ToLower()).AsCustom("citext").Nullable()
				.AddColumn("MobileVerificationCode".ToLower()).AsCustom("citext").Nullable()
				.AddColumn("HomeVerificationCode".ToLower()).AsCustom("citext").Nullable()
				.AddColumn("EmailVerificationCodeExpiry".ToLower()).AsDateTime().Nullable()
				.AddColumn("MobileVerificationCodeExpiry".ToLower()).AsDateTime().Nullable()
				.AddColumn("HomeVerificationCodeExpiry".ToLower()).AsDateTime().Nullable()
				.AddColumn("EmailVerificationAttempts".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("MobileVerificationAttempts".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("HomeVerificationAttempts".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("EmailVerificationAttemptsResetDate".ToLower()).AsDateTime().Nullable()
				.AddColumn("MobileVerificationAttemptsResetDate".ToLower()).AsDateTime().Nullable()
				.AddColumn("HomeVerificationAttemptsResetDate".ToLower()).AsDateTime().Nullable();
		}

		public override void Down()
		{
			Delete.Column("EmailVerified".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("MobileNumberVerified".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("HomeNumberVerified".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("EmailVerificationCode".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("MobileVerificationCode".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("HomeVerificationCode".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("EmailVerificationCodeExpiry".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("MobileVerificationCodeExpiry".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("HomeVerificationCodeExpiry".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("EmailVerificationAttempts".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("MobileVerificationAttempts".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("HomeVerificationAttempts".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("EmailVerificationAttemptsResetDate".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("MobileVerificationAttemptsResetDate".ToLower()).FromTable("UserProfiles".ToLower());
			Delete.Column("HomeVerificationAttemptsResetDate".ToLower()).FromTable("UserProfiles".ToLower());
		}
	}
}


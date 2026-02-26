using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(40)]
	public class M0040_AddingContactVerification : Migration
	{
		public override void Up()
		{
			Alter.Table("UserProfiles")
				.AddColumn("EmailVerified").AsBoolean().Nullable()
				.AddColumn("MobileNumberVerified").AsBoolean().Nullable()
				.AddColumn("HomeNumberVerified").AsBoolean().Nullable()
				.AddColumn("EmailVerificationCode").AsString(16).Nullable()
				.AddColumn("MobileVerificationCode").AsString(16).Nullable()
				.AddColumn("HomeVerificationCode").AsString(16).Nullable()
				.AddColumn("EmailVerificationCodeExpiry").AsDateTime().Nullable()
				.AddColumn("MobileVerificationCodeExpiry").AsDateTime().Nullable()
				.AddColumn("HomeVerificationCodeExpiry").AsDateTime().Nullable()
				.AddColumn("EmailVerificationAttempts").AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("MobileVerificationAttempts").AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("HomeVerificationAttempts").AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("EmailVerificationAttemptsResetDate").AsDateTime().Nullable()
				.AddColumn("MobileVerificationAttemptsResetDate").AsDateTime().Nullable()
				.AddColumn("HomeVerificationAttemptsResetDate").AsDateTime().Nullable();
		}

		public override void Down()
		{
			Delete.Column("EmailVerified").FromTable("UserProfiles");
			Delete.Column("MobileNumberVerified").FromTable("UserProfiles");
			Delete.Column("HomeNumberVerified").FromTable("UserProfiles");
			Delete.Column("EmailVerificationCode").FromTable("UserProfiles");
			Delete.Column("MobileVerificationCode").FromTable("UserProfiles");
			Delete.Column("HomeVerificationCode").FromTable("UserProfiles");
			Delete.Column("EmailVerificationCodeExpiry").FromTable("UserProfiles");
			Delete.Column("MobileVerificationCodeExpiry").FromTable("UserProfiles");
			Delete.Column("HomeVerificationCodeExpiry").FromTable("UserProfiles");
			Delete.Column("EmailVerificationAttempts").FromTable("UserProfiles");
			Delete.Column("MobileVerificationAttempts").FromTable("UserProfiles");
			Delete.Column("HomeVerificationAttempts").FromTable("UserProfiles");
			Delete.Column("EmailVerificationAttemptsResetDate").FromTable("UserProfiles");
			Delete.Column("MobileVerificationAttemptsResetDate").FromTable("UserProfiles");
			Delete.Column("HomeVerificationAttemptsResetDate").FromTable("UserProfiles");
		}
	}
}


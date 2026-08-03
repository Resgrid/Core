using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(110)]
	public class M0110_AddingVerificationSendWindows : Migration
	{
		public override void Up()
		{
			Alter.Table("UserProfiles")
				.AddColumn("EmailVerificationSendCount").AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("MobileVerificationSendCount").AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("HomeVerificationSendCount").AsInt32().NotNullable().WithDefaultValue(0)
				.AddColumn("EmailVerificationSendWindowStart").AsDateTime().Nullable()
				.AddColumn("MobileVerificationSendWindowStart").AsDateTime().Nullable()
				.AddColumn("HomeVerificationSendWindowStart").AsDateTime().Nullable();
		}

		public override void Down()
		{
			Delete.Column("EmailVerificationSendCount").FromTable("UserProfiles");
			Delete.Column("MobileVerificationSendCount").FromTable("UserProfiles");
			Delete.Column("HomeVerificationSendCount").FromTable("UserProfiles");
			Delete.Column("EmailVerificationSendWindowStart").FromTable("UserProfiles");
			Delete.Column("MobileVerificationSendWindowStart").FromTable("UserProfiles");
			Delete.Column("HomeVerificationSendWindowStart").FromTable("UserProfiles");
		}
	}
}

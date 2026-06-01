using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(69)]
	public class M0069_ChatbotLinkingCodes : Migration
	{
		public override void Up()
		{
			// ChatbotLinkingCodes - Short-lived codes for linking platform accounts
			Create.Table("ChatbotLinkingCodes")
				.WithColumn("Id").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("UserId").AsString(450).NotNullable()
				.WithColumn("Code").AsString(10).NotNullable()
				.WithColumn("Platform").AsInt32().Nullable()
				.WithColumn("PlatformUserId").AsString(256).Nullable()
				.WithColumn("IsUsed").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
				.WithColumn("ExpiresAt").AsDateTime2().NotNullable()
				.WithColumn("UsedAt").AsDateTime2().Nullable();

			Create.Index("IX_ChatbotLinkingCodes_Code")
				.OnTable("ChatbotLinkingCodes")
				.OnColumn("Code").Ascending();

			Create.Index("IX_ChatbotLinkingCodes_UserId")
				.OnTable("ChatbotLinkingCodes")
				.OnColumn("UserId").Ascending();

			Create.Index("IX_ChatbotLinkingCodes_ExpiresAt")
				.OnTable("ChatbotLinkingCodes")
				.OnColumn("ExpiresAt").Ascending();
		}

		public override void Down()
		{
			Delete.Table("ChatbotLinkingCodes");
		}
	}
}

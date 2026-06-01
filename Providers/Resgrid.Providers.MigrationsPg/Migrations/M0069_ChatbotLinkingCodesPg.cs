using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(69)]
	public class M0069_ChatbotLinkingCodesPg : Migration
	{
		public override void Up()
		{
			// ChatbotLinkingCodes - Short-lived codes for linking platform accounts
			Create.Table("ChatbotLinkingCodes".ToLower())
				.WithColumn("Id".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("UserId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Code".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Platform".ToLower()).AsInt32().Nullable()
				.WithColumn("PlatformUserId".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("IsUsed".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("CreatedAt".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
				.WithColumn("ExpiresAt".ToLower()).AsDateTime2().NotNullable()
				.WithColumn("UsedAt".ToLower()).AsDateTime2().Nullable();

			Create.Index("IX_ChatbotLinkingCodes_Code".ToLower())
				.OnTable("ChatbotLinkingCodes".ToLower())
				.OnColumn("Code".ToLower()).Ascending();

			Create.Index("IX_ChatbotLinkingCodes_UserId".ToLower())
				.OnTable("ChatbotLinkingCodes".ToLower())
				.OnColumn("UserId".ToLower()).Ascending();

			Create.Index("IX_ChatbotLinkingCodes_ExpiresAt".ToLower())
				.OnTable("ChatbotLinkingCodes".ToLower())
				.OnColumn("ExpiresAt".ToLower()).Ascending();
		}

		public override void Down()
		{
			Delete.Table("ChatbotLinkingCodes".ToLower());
		}
	}
}

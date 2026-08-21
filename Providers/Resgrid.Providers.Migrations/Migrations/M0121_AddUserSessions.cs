using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(121)]
	public class M0121_AddUserSessions : Migration
	{
		public override void Up()
		{
			if (Schema.Table("UserSessions").Exists())
				return;

			Create.Table("UserSessions")
				.WithColumn("UserSessionId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("UserId").AsString(128).NotNullable()
				.WithColumn("DepartmentId").AsInt32().Nullable()
				.WithColumn("AuthenticationGeneration").AsInt64().NotNullable().WithDefaultValue(0L)
				.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("StateVersion").AsInt64().NotNullable().WithDefaultValue(0L)
				.WithColumn("ClientApplication").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("ClientInstanceIdHash").AsString(128).Nullable()
				.WithColumn("DeviceName").AsString(256).Nullable()
				.WithColumn("DeviceType").AsString(128).Nullable()
				.WithColumn("OperatingSystem").AsString(128).Nullable()
				.WithColumn("Browser").AsString(128).Nullable()
				.WithColumn("ApplicationVersion").AsString(64).Nullable()
				.WithColumn("AuthenticationMethod").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("DepartmentSsoConfigId").AsString(128).Nullable()
				.WithColumn("OpenIddictAuthorizationId").AsString(128).Nullable()
				.WithColumn("WebCookieTicketKey").AsString(512).Nullable()
				.WithColumn("CreatedOn").AsDateTime2().NotNullable()
				.WithColumn("LastActiveOn").AsDateTime2().NotNullable()
				.WithColumn("ExpiresOn").AsDateTime2().NotNullable()
				.WithColumn("FirstIpAddress").AsString(64).Nullable()
				.WithColumn("LastIpAddress").AsString(64).Nullable()
				.WithColumn("LastCountry").AsString(128).Nullable()
				.WithColumn("LastRegion").AsString(128).Nullable()
				.WithColumn("LastCity").AsString(128).Nullable()
				.WithColumn("UserAgent").AsString(1024).Nullable()
				.WithColumn("IsLegacyAdopted").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("RevokedOn").AsDateTime2().Nullable()
				.WithColumn("RevokedByUserId").AsString(128).Nullable()
				.WithColumn("RevocationReason").AsInt32().Nullable();

			Create.Index("IX_UserSessions_User_State_Expiry_Activity")
				.OnTable("UserSessions")
				.OnColumn("UserId").Ascending()
				.OnColumn("State").Ascending()
				.OnColumn("ExpiresOn").Ascending()
				.OnColumn("LastActiveOn").Descending();
			Create.Index("IX_UserSessions_Department_User_State")
				.OnTable("UserSessions")
				.OnColumn("DepartmentId").Ascending()
				.OnColumn("UserId").Ascending()
				.OnColumn("State").Ascending();
			Create.Index("IX_UserSessions_Revoked_Expiry")
				.OnTable("UserSessions")
				.OnColumn("RevokedOn").Ascending()
				.OnColumn("ExpiresOn").Ascending();
			Execute.Sql("CREATE UNIQUE INDEX [UX_UserSessions_OpenIddictAuthorizationId] ON [UserSessions] ([OpenIddictAuthorizationId]) WHERE [OpenIddictAuthorizationId] IS NOT NULL;");
		}

		public override void Down()
		{
			if (Schema.Table("UserSessions").Exists())
				Delete.Table("UserSessions");
		}
	}
}

using FluentMigrator;
using FluentMigrator.SqlServer;

namespace Resgrid.Providers.Migrations.Migrations
{
	// ONLINE index operations should not be wrapped in a long migration transaction because their
	// final schema locks can otherwise be retained until commit. Every statement is guarded for retry.
	// ONLINE is not supported by every SQL Server edition; unsupported deployments must schedule a
	// maintenance window and use an explicitly reviewed offline variant rather than silently blocking.
	[Migration(121, TransactionBehavior.None)]
	public class M0121_AddUserSessions : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("UserSessions").Exists())
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

			if (!Schema.Table("UserSessions").Index("IX_UserSessions_User_State_Expiry_Activity").Exists())
				Create.Index("IX_UserSessions_User_State_Expiry_Activity")
				.OnTable("UserSessions")
				.OnColumn("UserId").Ascending()
				.OnColumn("State").Ascending()
				.OnColumn("ExpiresOn").Ascending()
				.OnColumn("LastActiveOn").Descending()
				.WithOptions().Online();

			if (!Schema.Table("UserSessions").Index("IX_UserSessions_Department_User_State").Exists())
				Create.Index("IX_UserSessions_Department_User_State")
				.OnTable("UserSessions")
				.OnColumn("DepartmentId").Ascending()
				.OnColumn("UserId").Ascending()
				.OnColumn("State").Ascending()
				.WithOptions().Online();

			if (!Schema.Table("UserSessions").Index("IX_UserSessions_Revoked_Expiry").Exists())
				Create.Index("IX_UserSessions_Revoked_Expiry")
				.OnTable("UserSessions")
				.OnColumn("RevokedOn").Ascending()
				.OnColumn("ExpiresOn").Ascending()
				.WithOptions().Online();

			if (!Schema.Table("UserSessions").Index("UX_UserSessions_OpenIddictAuthorizationId").Exists())
				Execute.Sql("CREATE UNIQUE INDEX [UX_UserSessions_OpenIddictAuthorizationId] ON [UserSessions] ([OpenIddictAuthorizationId]) WHERE [OpenIddictAuthorizationId] IS NOT NULL WITH (ONLINE = ON);");
		}

		public override void Down()
		{
			// SQL Server has no online DROP TABLE. Rollback requires a schema-modification lock;
			// schedule it during maintenance on editions or workloads where that lock is unsafe.
			if (Schema.Table("UserSessions").Exists())
				Delete.Table("UserSessions");
		}
	}
}

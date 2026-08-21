using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	// ONLINE index operations should not be wrapped in a long migration transaction because their
	// final schema locks can otherwise be retained until commit. Every statement is guarded for retry.
	// SqlServerOnlineIndex resolves ONLINE support per edition at execution time, so an edition without
	// online builds gets the same indexes offline instead of failing the migration part-way through.
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

			Execute.Sql(SqlServerOnlineIndex.Create("IX_UserSessions_User_State_Expiry_Activity", "UserSessions",
				new[] { "[UserId] ASC", "[State] ASC", "[ExpiresOn] ASC", "[LastActiveOn] DESC" }));

			Execute.Sql(SqlServerOnlineIndex.Create("IX_UserSessions_Department_User_State", "UserSessions",
				new[] { "[DepartmentId] ASC", "[UserId] ASC", "[State] ASC" }));

			Execute.Sql(SqlServerOnlineIndex.Create("IX_UserSessions_Revoked_Expiry", "UserSessions",
				new[] { "[RevokedOn] ASC", "[ExpiresOn] ASC" }));

			// Rollout / failure / rollback characteristics of this index, made explicit:
			//   * ONLINE = ON where the edition supports it: the table stays readable and writable for the
			//     duration of the build, and the only blocking window is the short SCH-M lock taken at the
			//     start and end. Editions without online builds get the same index offline, which holds its
			//     lock for the whole build - see SqlServerOnlineIndex.
			//   * SORT_IN_TEMPDB = ON: the intermediate sort runs in tempdb instead of the user database,
			//     so the build does not grow the primary filegroup or spike its log. Cost: tempdb needs
			//     free space roughly equal to the finished index size.
			//   * Failure handling: this runs outside a migration transaction (TransactionBehavior.None),
			//     so a failure is contained to this one statement - SQL Server discards the partially built
			//     index itself and the migration aborts without recording version 121. Re-running the
			//     migration is safe: every step above is existence-guarded, so already-created objects are
			//     skipped and only this index is retried.
			//   * Rollback: dropping the index is metadata-only and needs no data movement -
			//     DROP INDEX [UX_UserSessions_OpenIddictAuthorizationId] ON [UserSessions];
			//     Full rollback is Down(), which drops the table - see the note there.
			Execute.Sql(SqlServerOnlineIndex.Create("UX_UserSessions_OpenIddictAuthorizationId", "UserSessions",
				new[] { "[OpenIddictAuthorizationId]" }, unique: true,
				filter: "[OpenIddictAuthorizationId] IS NOT NULL", sortInTempDb: true));
		}

		public override void Down()
		{
			// SQL Server has no online DROP TABLE. Rollback requires a schema-modification lock;
			// schedule it during maintenance on editions or workloads where that lock is unsafe.
			// If only the filtered unique index needs to be backed out (for example a duplicate
			// OpenIddictAuthorizationId blocks the build), drop that index on its own instead of running
			// this Down() - the drop is metadata-only and leaves session data intact.
			if (Schema.Table("UserSessions").Exists())
				Delete.Table("UserSessions");
		}
	}
}

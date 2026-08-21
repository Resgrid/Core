using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	// ONLINE index operations should not be wrapped in a long migration transaction because their
	// final schema locks can otherwise be retained until commit. Every statement is guarded for retry.
	// SqlServerOnlineIndex resolves ONLINE support per edition at execution time, so an edition without
	// online builds gets the same indexes offline instead of failing the migration part-way through.
	[Migration(122, TransactionBehavior.None)]
	public class M0122_AddUserExternalIdentityLinks : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("UserExternalIdentityLinks").Exists())
				Create.Table("UserExternalIdentityLinks")
				.WithColumn("UserExternalIdentityLinkId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("UserId").AsString(128).NotNullable()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("DepartmentMemberId").AsInt32().NotNullable()
				.WithColumn("DepartmentSsoConfigId").AsString(128).NotNullable()
				.WithColumn("ProviderType").AsInt32().NotNullable()
				.WithColumn("Issuer").AsString(1024).NotNullable()
				.WithColumn("ExternalSubject").AsString(512).NotNullable()
				.WithColumn("LinkMethod").AsInt32().NotNullable()
				.WithColumn("EmailAtLink").AsString(512).Nullable()
				.WithColumn("IsEmailExternallyManaged").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("LinkedOn").AsDateTime2().NotNullable()
				.WithColumn("LastLoginOn").AsDateTime2().Nullable()
				.WithColumn("UnlinkedOn").AsDateTime2().Nullable()
				.WithColumn("UnlinkedByUserId").AsString(128).Nullable();

			// Both unique indexes are scoped to live links so a soft-unlinked row cannot permanently
			// reserve the subject, matching the isactive predicate every read in the repository uses.
			Execute.Sql(SqlServerOnlineIndex.Create("UX_UserExternalIdentityLinks_Config_Subject", "UserExternalIdentityLinks",
				new[] { "[DepartmentSsoConfigId] ASC", "[ExternalSubject] ASC" }, unique: true,
				filter: "[IsActive] = 1"));

			Execute.Sql(SqlServerOnlineIndex.Create("UX_UserExternalIdentityLinks_User_Config", "UserExternalIdentityLinks",
				new[] { "[UserId] ASC", "[DepartmentSsoConfigId] ASC" }, unique: true,
				filter: "[IsActive] = 1"));

			Execute.Sql(SqlServerOnlineIndex.Create("IX_UserExternalIdentityLinks_Department_Member", "UserExternalIdentityLinks",
				new[] { "[DepartmentId] ASC", "[DepartmentMemberId] ASC" }));
		}

		public override void Down()
		{
			// SQL Server has no online DROP TABLE. Rollback requires a schema-modification lock;
			// schedule it during maintenance on editions or workloads where that lock is unsafe.
			if (Schema.Table("UserExternalIdentityLinks").Exists())
				Delete.Table("UserExternalIdentityLinks");
		}
	}
}

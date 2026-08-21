using FluentMigrator;
using FluentMigrator.SqlServer;

namespace Resgrid.Providers.Migrations.Migrations
{
	// ONLINE index operations should not be wrapped in a long migration transaction because their
	// final schema locks can otherwise be retained until commit. Every statement is guarded for retry.
	// ONLINE is not supported by every SQL Server edition; unsupported deployments must schedule a
	// maintenance window and use an explicitly reviewed offline variant rather than silently blocking.
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

			if (!Schema.Table("UserExternalIdentityLinks").Index("UX_UserExternalIdentityLinks_Config_Subject").Exists())
				Create.Index("UX_UserExternalIdentityLinks_Config_Subject")
				.OnTable("UserExternalIdentityLinks")
				.OnColumn("DepartmentSsoConfigId").Ascending()
				.OnColumn("ExternalSubject").Ascending()
				.WithOptions().Unique()
				.WithOptions().Online();

			if (!Schema.Table("UserExternalIdentityLinks").Index("UX_UserExternalIdentityLinks_User_Config").Exists())
				Create.Index("UX_UserExternalIdentityLinks_User_Config")
				.OnTable("UserExternalIdentityLinks")
				.OnColumn("UserId").Ascending()
				.OnColumn("DepartmentSsoConfigId").Ascending()
				.WithOptions().Unique()
				.WithOptions().Online();

			if (!Schema.Table("UserExternalIdentityLinks").Index("IX_UserExternalIdentityLinks_Department_Member").Exists())
				Create.Index("IX_UserExternalIdentityLinks_Department_Member")
				.OnTable("UserExternalIdentityLinks")
				.OnColumn("DepartmentId").Ascending()
				.OnColumn("DepartmentMemberId").Ascending()
				.WithOptions().Online();
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

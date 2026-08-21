using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(122)]
	public class M0122_AddUserExternalIdentityLinks : Migration
	{
		public override void Up()
		{
			if (Schema.Table("UserExternalIdentityLinks").Exists())
				return;

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

			Create.Index("UX_UserExternalIdentityLinks_Config_Subject")
				.OnTable("UserExternalIdentityLinks")
				.OnColumn("DepartmentSsoConfigId").Ascending()
				.OnColumn("ExternalSubject").Ascending()
				.WithOptions().Unique();
			Create.Index("UX_UserExternalIdentityLinks_User_Config")
				.OnTable("UserExternalIdentityLinks")
				.OnColumn("UserId").Ascending()
				.OnColumn("DepartmentSsoConfigId").Ascending()
				.WithOptions().Unique();
			Create.Index("IX_UserExternalIdentityLinks_Department_Member")
				.OnTable("UserExternalIdentityLinks")
				.OnColumn("DepartmentId").Ascending()
				.OnColumn("DepartmentMemberId").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("UserExternalIdentityLinks").Exists())
				Delete.Table("UserExternalIdentityLinks");
		}
	}
}

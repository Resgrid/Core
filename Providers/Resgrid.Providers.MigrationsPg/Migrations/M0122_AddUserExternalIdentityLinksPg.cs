using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(122)]
	public class M0122_AddUserExternalIdentityLinksPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("userexternalidentitylinks").Exists())
				return;

			Create.Table("userexternalidentitylinks")
				.WithColumn("userexternalidentitylinkid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("userid").AsCustom("citext").NotNullable()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("departmentmemberid").AsInt32().NotNullable()
				.WithColumn("departmentssoconfigid").AsCustom("citext").NotNullable()
				.WithColumn("providertype").AsInt32().NotNullable()
				.WithColumn("issuer").AsCustom("citext").NotNullable()
				.WithColumn("externalsubject").AsCustom("citext").NotNullable()
				.WithColumn("linkmethod").AsInt32().NotNullable()
				.WithColumn("emailatlink").AsCustom("citext").Nullable()
				.WithColumn("isemailexternallymanaged").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("linkedon").AsDateTime2().NotNullable()
				.WithColumn("lastloginon").AsDateTime2().Nullable()
				.WithColumn("unlinkedon").AsDateTime2().Nullable()
				.WithColumn("unlinkedbyuserid").AsCustom("citext").Nullable();

			Create.Index("ux_userexternalidentitylinks_config_subject")
				.OnTable("userexternalidentitylinks")
				.OnColumn("departmentssoconfigid").Ascending()
				.OnColumn("externalsubject").Ascending()
				.WithOptions().Unique();
			Create.Index("ux_userexternalidentitylinks_user_config")
				.OnTable("userexternalidentitylinks")
				.OnColumn("userid").Ascending()
				.OnColumn("departmentssoconfigid").Ascending()
				.WithOptions().Unique();
			Create.Index("ix_userexternalidentitylinks_department_member")
				.OnTable("userexternalidentitylinks")
				.OnColumn("departmentid").Ascending()
				.OnColumn("departmentmemberid").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("userexternalidentitylinks").Exists())
				Delete.Table("userexternalidentitylinks");
		}
	}
}

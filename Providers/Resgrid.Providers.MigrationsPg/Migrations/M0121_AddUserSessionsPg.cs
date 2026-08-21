using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(121)]
	public class M0121_AddUserSessionsPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("usersessions").Exists())
				return;

			Create.Table("usersessions")
				.WithColumn("usersessionid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("userid").AsCustom("citext").NotNullable()
				.WithColumn("departmentid").AsInt32().Nullable()
				.WithColumn("authenticationgeneration").AsInt64().NotNullable().WithDefaultValue(0L)
				.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("stateversion").AsInt64().NotNullable().WithDefaultValue(0L)
				.WithColumn("clientapplication").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("clientinstanceidhash").AsCustom("citext").Nullable()
				.WithColumn("devicename").AsCustom("citext").Nullable()
				.WithColumn("devicetype").AsCustom("citext").Nullable()
				.WithColumn("operatingsystem").AsCustom("citext").Nullable()
				.WithColumn("browser").AsCustom("citext").Nullable()
				.WithColumn("applicationversion").AsCustom("citext").Nullable()
				.WithColumn("authenticationmethod").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("departmentssoconfigid").AsCustom("citext").Nullable()
				.WithColumn("openiddictauthorizationid").AsCustom("citext").Nullable()
				.WithColumn("webcookieticketkey").AsCustom("citext").Nullable()
				.WithColumn("createdon").AsDateTime2().NotNullable()
				.WithColumn("lastactiveon").AsDateTime2().NotNullable()
				.WithColumn("expireson").AsDateTime2().NotNullable()
				.WithColumn("firstipaddress").AsCustom("citext").Nullable()
				.WithColumn("lastipaddress").AsCustom("citext").Nullable()
				.WithColumn("lastcountry").AsCustom("citext").Nullable()
				.WithColumn("lastregion").AsCustom("citext").Nullable()
				.WithColumn("lastcity").AsCustom("citext").Nullable()
				.WithColumn("useragent").AsCustom("citext").Nullable()
				.WithColumn("islegacyadopted").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("revokedon").AsDateTime2().Nullable()
				.WithColumn("revokedbyuserid").AsCustom("citext").Nullable()
				.WithColumn("revocationreason").AsInt32().Nullable();

			Create.Index("ix_usersessions_user_state_expiry_activity")
				.OnTable("usersessions")
				.OnColumn("userid").Ascending()
				.OnColumn("state").Ascending()
				.OnColumn("expireson").Ascending()
				.OnColumn("lastactiveon").Descending();
			Create.Index("ix_usersessions_department_user_state")
				.OnTable("usersessions")
				.OnColumn("departmentid").Ascending()
				.OnColumn("userid").Ascending()
				.OnColumn("state").Ascending();
			Create.Index("ix_usersessions_revoked_expiry")
				.OnTable("usersessions")
				.OnColumn("revokedon").Ascending()
				.OnColumn("expireson").Ascending();
			Execute.Sql("CREATE UNIQUE INDEX ux_usersessions_openiddictauthorizationid ON usersessions (openiddictauthorizationid) WHERE openiddictauthorizationid IS NOT NULL;");
		}

		public override void Down()
		{
			if (Schema.Table("usersessions").Exists())
			{
				Execute.Sql("DROP INDEX IF EXISTS ux_usersessions_openiddictauthorizationid;");
				Delete.Table("usersessions");
			}
		}
	}
}

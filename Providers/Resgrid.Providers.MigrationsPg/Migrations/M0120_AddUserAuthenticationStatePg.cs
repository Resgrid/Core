using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(120)]
	public class M0120_AddUserAuthenticationStatePg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("aspnetusers").Column("authenticationgeneration").Exists())
				Alter.Table("aspnetusers").AddColumn("authenticationgeneration").AsInt64().NotNullable().WithDefaultValue(0L);

			if (!Schema.Table("aspnetusers").Column("credentialsvalidafterutc").Exists())
				Alter.Table("aspnetusers").AddColumn("credentialsvalidafterutc").AsDateTime2().Nullable();

			if (!Schema.Table("aspnetusers").Column("authenticationstatechangedon").Exists())
				Alter.Table("aspnetusers").AddColumn("authenticationstatechangedon").AsDateTime2().Nullable();
		}

		public override void Down()
		{
			if (Schema.Table("aspnetusers").Column("authenticationstatechangedon").Exists())
				Delete.Column("authenticationstatechangedon").FromTable("aspnetusers");
			if (Schema.Table("aspnetusers").Column("credentialsvalidafterutc").Exists())
				Delete.Column("credentialsvalidafterutc").FromTable("aspnetusers");
			if (Schema.Table("aspnetusers").Column("authenticationgeneration").Exists())
				Delete.Column("authenticationgeneration").FromTable("aspnetusers");
		}
	}
}

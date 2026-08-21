using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(120)]
	public class M0120_AddUserAuthenticationState : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("AspNetUsers").Column("AuthenticationGeneration").Exists())
				Alter.Table("AspNetUsers").AddColumn("AuthenticationGeneration").AsInt64().NotNullable().WithDefaultValue(0L);

			if (!Schema.Table("AspNetUsers").Column("CredentialsValidAfterUtc").Exists())
				Alter.Table("AspNetUsers").AddColumn("CredentialsValidAfterUtc").AsDateTime2().Nullable();

			if (!Schema.Table("AspNetUsers").Column("AuthenticationStateChangedOn").Exists())
				Alter.Table("AspNetUsers").AddColumn("AuthenticationStateChangedOn").AsDateTime2().Nullable();
		}

		public override void Down()
		{
			if (Schema.Table("AspNetUsers").Column("AuthenticationStateChangedOn").Exists())
				Delete.Column("AuthenticationStateChangedOn").FromTable("AspNetUsers");
			if (Schema.Table("AspNetUsers").Column("CredentialsValidAfterUtc").Exists())
				Delete.Column("CredentialsValidAfterUtc").FromTable("AspNetUsers");
			if (Schema.Table("AspNetUsers").Column("AuthenticationGeneration").Exists())
				Delete.Column("AuthenticationGeneration").FromTable("AspNetUsers");
		}
	}
}

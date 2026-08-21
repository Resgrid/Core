using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(123)]
	public class M0123_AddAuthenticationAuditContextPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("systemaudits").Column("targetuserid").Exists())
				Alter.Table("systemaudits").AddColumn("targetuserid").AsCustom("citext").Nullable();
			if (!Schema.Table("systemaudits").Column("sessionid").Exists())
				Alter.Table("systemaudits").AddColumn("sessionid").AsCustom("citext").Nullable();
			if (!Schema.Table("systemaudits").Column("correlationid").Exists())
				Alter.Table("systemaudits").AddColumn("correlationid").AsCustom("citext").Nullable();
		}

		public override void Down()
		{
			if (Schema.Table("systemaudits").Column("correlationid").Exists())
				Delete.Column("correlationid").FromTable("systemaudits");
			if (Schema.Table("systemaudits").Column("sessionid").Exists())
				Delete.Column("sessionid").FromTable("systemaudits");
			if (Schema.Table("systemaudits").Column("targetuserid").Exists())
				Delete.Column("targetuserid").FromTable("systemaudits");
		}
	}
}

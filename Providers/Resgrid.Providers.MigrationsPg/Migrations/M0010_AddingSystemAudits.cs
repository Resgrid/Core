using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(10)]
	public class M0010_AddingSystemAudits : Migration
	{
		public override void Up()
		{
			Create.Table("SystemAudits")
			   .WithColumn("SystemAuditId").AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().Nullable()
			   .WithColumn("Type").AsInt32().NotNullable()
			   .WithColumn("System").AsInt32().NotNullable()
			   .WithColumn("UserId").AsCustom("citext").Nullable()
			   .WithColumn("Username").AsCustom("citext").Nullable()
			   .WithColumn("IpAddress").AsCustom("citext")
			   .WithColumn("Data").AsCustom("citext")
			   .WithColumn("Successful").AsBoolean().NotNullable()
			   .WithColumn("ServerName").AsCustom("citext")
			   .WithColumn("LoggedOn").AsDateTime2().NotNullable();
		}

		public override void Down()
		{

		}
	}
}

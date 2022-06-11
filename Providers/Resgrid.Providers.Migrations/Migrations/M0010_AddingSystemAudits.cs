using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(10)]
	public class M0010_AddingSystemAudits : Migration
	{
		public override void Up()
		{
			Create.Table("SystemAudits")
			   .WithColumn("SystemAuditId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().Nullable()
			   .WithColumn("Type").AsInt32().NotNullable()
			   .WithColumn("System").AsInt32().NotNullable()
			   .WithColumn("UserId").AsString(128).Nullable()
			   .WithColumn("Username").AsString(512).Nullable()
			   .WithColumn("IpAddress").AsString(512)
			   .WithColumn("Data").AsString()
			   .WithColumn("Successful").AsBoolean().NotNullable()
			   .WithColumn("ServerName").AsString(512)
			   .WithColumn("LoggedOn").AsDateTime2().NotNullable();
		}

		public override void Down()
		{

		}
	}
}

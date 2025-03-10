using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(10)]
	public class M0010_AddingSystemAudits : Migration
	{
		public override void Up()
		{
			Create.Table("SystemAudits".ToLower())
			   .WithColumn("SystemAuditId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().Nullable()
			   .WithColumn("Type".ToLower()).AsInt32().NotNullable()
			   .WithColumn("System".ToLower()).AsInt32().NotNullable()
			   .WithColumn("UserId".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("Username".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("IpAddress".ToLower()).AsCustom("citext")
			   .WithColumn("Data".ToLower()).AsCustom("citext")
			   .WithColumn("Successful".ToLower()).AsBoolean().NotNullable()
			   .WithColumn("ServerName".ToLower()).AsCustom("citext")
			   .WithColumn("LoggedOn".ToLower()).AsDateTime2().NotNullable();
		}

		public override void Down()
		{

		}
	}
}

using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(17)]
	public class M0017_AddingToAuditTable : Migration
	{
		public override void Up()
		{
			Alter.Table("AuditLogs").AddColumn("IpAddress").AsCustom("citext").Nullable();
			Alter.Table("AuditLogs").AddColumn("Successful").AsBoolean().Nullable();
			Alter.Table("AuditLogs").AddColumn("ObjectId").AsCustom("citext").Nullable();
			Alter.Table("AuditLogs").AddColumn("ObjectDepartmentId").AsInt32().Nullable();
			Alter.Table("AuditLogs").AddColumn("UserAgent").AsCustom("citext").Nullable();
			Alter.Table("AuditLogs").AddColumn("ServerName").AsCustom("citext").Nullable();
		}

		public override void Down()
		{

		}
	}
}

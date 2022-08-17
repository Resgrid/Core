using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(17)]
	public class M0017_AddingToAuditTable : Migration
	{
		public override void Up()
		{
			Alter.Table("AuditLogs").AddColumn("IpAddress").AsString(512).Nullable();
			Alter.Table("AuditLogs").AddColumn("Successful").AsBoolean().Nullable();
			Alter.Table("AuditLogs").AddColumn("ObjectId").AsString(128).Nullable();
			Alter.Table("AuditLogs").AddColumn("ObjectDepartmentId").AsInt32().Nullable();
			Alter.Table("AuditLogs").AddColumn("UserAgent").AsString().Nullable();
			Alter.Table("AuditLogs").AddColumn("ServerName").AsString(512).Nullable();
		}

		public override void Down()
		{

		}
	}
}

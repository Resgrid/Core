using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(17)]
	public class M0017_AddingToAuditTable : Migration
	{
		public override void Up()
		{
			Alter.Table("AuditLogs".ToLower()).AddColumn("IpAddress".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("AuditLogs".ToLower()).AddColumn("Successful".ToLower()).AsBoolean().Nullable();
			Alter.Table("AuditLogs".ToLower()).AddColumn("ObjectId".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("AuditLogs".ToLower()).AddColumn("ObjectDepartmentId".ToLower()).AsInt32().Nullable();
			Alter.Table("AuditLogs".ToLower()).AddColumn("UserAgent".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("AuditLogs".ToLower()).AddColumn("ServerName".ToLower()).AsCustom("citext").Nullable();
		}

		public override void Down()
		{

		}
	}
}

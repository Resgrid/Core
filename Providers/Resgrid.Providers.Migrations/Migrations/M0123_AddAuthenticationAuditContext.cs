using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(123)]
	public class M0123_AddAuthenticationAuditContext : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("SystemAudits").Column("TargetUserId").Exists())
				Alter.Table("SystemAudits").AddColumn("TargetUserId").AsString(128).Nullable();
			if (!Schema.Table("SystemAudits").Column("SessionId").Exists())
				Alter.Table("SystemAudits").AddColumn("SessionId").AsString(128).Nullable();
			if (!Schema.Table("SystemAudits").Column("CorrelationId").Exists())
				Alter.Table("SystemAudits").AddColumn("CorrelationId").AsString(128).Nullable();
		}

		public override void Down()
		{
			if (Schema.Table("SystemAudits").Column("CorrelationId").Exists())
				Delete.Column("CorrelationId").FromTable("SystemAudits");
			if (Schema.Table("SystemAudits").Column("SessionId").Exists())
				Delete.Column("SessionId").FromTable("SystemAudits");
			if (Schema.Table("SystemAudits").Column("TargetUserId").Exists())
				Delete.Column("TargetUserId").FromTable("SystemAudits");
		}
	}
}

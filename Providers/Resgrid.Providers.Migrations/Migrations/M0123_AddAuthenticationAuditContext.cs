using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	// Keep the SQL Server audit investigation path aligned with PostgreSQL. ONLINE index creation
	// avoids blocking audit writes; SqlServerOnlineIndex resolves whether this edition supports it at
	// execution time and falls back to the same index built offline where it does not.
	[Migration(123, TransactionBehavior.None)]
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

			Execute.Sql(SqlServerOnlineIndex.Create("IX_SystemAudits_TargetUserId_LoggedOn", "SystemAudits",
				new[] { "[TargetUserId] ASC", "[LoggedOn] DESC", "[SystemAuditId] DESC" },
				filter: "[TargetUserId] IS NOT NULL"));
		}

		public override void Down()
		{
			// Index removal and the following column drops require schema-modification locks;
			// schedule rollback during a maintenance window on a busy audit table.
			if (Schema.Table("SystemAudits").Index("IX_SystemAudits_TargetUserId_LoggedOn").Exists())
				Delete.Index("IX_SystemAudits_TargetUserId_LoggedOn").OnTable("SystemAudits");

			if (Schema.Table("SystemAudits").Column("CorrelationId").Exists())
				Delete.Column("CorrelationId").FromTable("SystemAudits");
			if (Schema.Table("SystemAudits").Column("SessionId").Exists())
				Delete.Column("SessionId").FromTable("SystemAudits");
			if (Schema.Table("SystemAudits").Column("TargetUserId").Exists())
				Delete.Column("TargetUserId").FromTable("SystemAudits");
		}
	}
}

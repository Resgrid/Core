using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// RMS-5 prevention and investigation audits (registry M0188): indexes RmsAccessAudits by correlation. These
	/// aggregates are not Records, so RecordsPreventionGate.AuditAsync leaves RecordId null and rides the aggregate id
	/// on CorrelationId; the case access-audit view reads back on that column and the M0155 indexes do not cover it.
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(188)]
	public class M0188_AddRmsAccessAuditCorrelationIndex : Migration
	{
		public override void Up()
		{
			if (Schema.Table("RmsAccessAudits").Exists() && !Schema.Table("RmsAccessAudits").Index("IX_RmsAccessAudits_Department_Correlation_Occurred").Exists())
				Create.Index("IX_RmsAccessAudits_Department_Correlation_Occurred").OnTable("RmsAccessAudits")
					.OnColumn("DepartmentId").Ascending().OnColumn("CorrelationId").Ascending().OnColumn("OccurredOn").Descending();
		}

		public override void Down()
		{
			if (Schema.Table("RmsAccessAudits").Exists() && Schema.Table("RmsAccessAudits").Index("IX_RmsAccessAudits_Department_Correlation_Occurred").Exists())
				Delete.Index("IX_RmsAccessAudits_Department_Correlation_Occurred").OnTable("RmsAccessAudits");
		}
	}
}

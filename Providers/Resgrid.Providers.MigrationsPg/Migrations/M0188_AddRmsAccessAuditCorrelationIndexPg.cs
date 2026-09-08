using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// RMS-5 prevention and investigation audits (registry M0188): indexes rmsaccessaudits by correlation. These
	/// aggregates are not Records, so RecordsPreventionGate.AuditAsync leaves RecordId null and rides the aggregate id
	/// on CorrelationId; the case access-audit view reads back on that column and the M0155 indexes do not cover it.
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(188)]
	public class M0188_AddRmsAccessAuditCorrelationIndexPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("rmsaccessaudits").Exists() && !Schema.Table("rmsaccessaudits").Index("IX_RmsAccessAudits_Department_Correlation_Occurred").Exists())
				Create.Index("IX_RmsAccessAudits_Department_Correlation_Occurred").OnTable("rmsaccessaudits")
					.OnColumn("departmentid").Ascending().OnColumn("correlationid").Ascending().OnColumn("occurredon").Descending();
		}

		public override void Down()
		{
			if (Schema.Table("rmsaccessaudits").Exists() && Schema.Table("rmsaccessaudits").Index("IX_RmsAccessAudits_Department_Correlation_Occurred").Exists())
				Delete.Index("IX_RmsAccessAudits_Department_Correlation_Occurred").OnTable("rmsaccessaudits");
		}
	}
}

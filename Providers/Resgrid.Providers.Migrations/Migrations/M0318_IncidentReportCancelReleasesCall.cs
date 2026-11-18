using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// A cancelled incident report no longer holds its Call. M0164 made UX_RmsIncidentReports_Call_Entity unique on
	/// (DepartmentId, CallId, ReportingEntityId, DefinitionKey) for every row not soft-deleted, so a report started by
	/// mistake and then cancelled blocked the Call for good: starting again returned the cancelled report and the index
	/// refused a second row. The filter now also leaves out State 8 (RmsRecordState.Cancelled), matching operational
	/// Records, whose cancel and void clear their cardinality key. A voided report keeps the Call: it was finalized and
	/// may have been filed, so a replacement goes through amendment or correction, not a second authoritative report.
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(318)]
	public class M0318_IncidentReportCancelReleasesCall : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsIncidentReports").Exists())
				return;

			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_RmsIncidentReports_Call_Entity' AND object_id = OBJECT_ID('RmsIncidentReports')) DROP INDEX [UX_RmsIncidentReports_Call_Entity] ON [RmsIncidentReports];");
			Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX [UX_RmsIncidentReports_Call_Entity] ON [RmsIncidentReports] ([DepartmentId], [CallId], [ReportingEntityId], [DefinitionKey]) WHERE [DeletedOn] IS NULL AND [State] <> 8;");
		}

		public override void Down()
		{
			if (!Schema.Table("RmsIncidentReports").Exists())
				return;

			// The M0164 filter can only come back while no Call holds a cancelled report next to its replacement; otherwise
			// the index build would fail, so the M0318 filter stays rather than any report being touched.
			Execute.Sql(@"IF NOT EXISTS (SELECT 1 FROM [RmsIncidentReports] WHERE [DeletedOn] IS NULL GROUP BY [DepartmentId], [CallId], [ReportingEntityId], [DefinitionKey] HAVING COUNT(*) > 1)
BEGIN
	IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_RmsIncidentReports_Call_Entity' AND object_id = OBJECT_ID('RmsIncidentReports')) DROP INDEX [UX_RmsIncidentReports_Call_Entity] ON [RmsIncidentReports];
	CREATE UNIQUE NONCLUSTERED INDEX [UX_RmsIncidentReports_Call_Entity] ON [RmsIncidentReports] ([DepartmentId], [CallId], [ReportingEntityId], [DefinitionKey]) WHERE [DeletedOn] IS NULL;
END");
		}
	}
}

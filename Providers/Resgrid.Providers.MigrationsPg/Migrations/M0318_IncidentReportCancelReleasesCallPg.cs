using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>A cancelled incident report no longer holds its Call; see the SQL Server M0318_IncidentReportCancelReleasesCall.</summary>
	[Migration(318)]
	public class M0318_IncidentReportCancelReleasesCallPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsincidentreports").Exists())
				return;

			Execute.Sql("DROP INDEX IF EXISTS ux_rmsincidentreports_call_entity;");
			// State 8 = RmsRecordState.Cancelled.
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsincidentreports_call_entity ON rmsincidentreports (departmentid, callid, reportingentityid, definitionkey) WHERE deletedon IS NULL AND state <> 8;");
		}

		public override void Down()
		{
			if (!Schema.Table("rmsincidentreports").Exists())
				return;

			// Restore the M0164 filter only while no Call holds a cancelled report next to its replacement.
			Execute.Sql(@"DO $$
BEGIN
	IF NOT EXISTS (SELECT 1 FROM rmsincidentreports WHERE deletedon IS NULL GROUP BY departmentid, callid, reportingentityid, definitionkey HAVING COUNT(*) > 1) THEN
		DROP INDEX IF EXISTS ux_rmsincidentreports_call_entity;
		CREATE UNIQUE INDEX ux_rmsincidentreports_call_entity ON rmsincidentreports (departmentid, callid, reportingentityid, definitionkey) WHERE deletedon IS NULL;
	END IF;
END $$;");
		}
	}
}

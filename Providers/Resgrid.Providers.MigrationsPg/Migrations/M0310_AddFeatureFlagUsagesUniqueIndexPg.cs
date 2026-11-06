using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// One featureflagusages row per (featureflagid, departmentid, usagedate): every host flushes its own in-memory
	/// evaluation counters, and FeatureFlagUsageRepository.AddUsageAsync upserts them into that row with
	/// INSERT ... ON CONFLICT DO UPDATE. The index is on COALESCE(departmentid, 0) because PostgreSQL treats NULLs as
	/// distinct, which would let department-less rows duplicate. Collapses any duplicate rows the earlier append-only
	/// writes left (summing their counts) before adding the index.
	/// </summary>
	[Migration(310)]
	public class M0310_AddFeatureFlagUsagesUniqueIndexPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
WITH dupes AS (
	SELECT MIN(featureflagusageid) AS keepid, SUM(evaluationcount) AS evaluationcount, SUM(enabledcount) AS enabledcount, SUM(disabledcount) AS disabledcount
	FROM featureflagusages
	GROUP BY featureflagid, COALESCE(departmentid, 0), usagedate
	HAVING COUNT(*) > 1)
UPDATE featureflagusages u SET evaluationcount = d.evaluationcount, enabledcount = d.enabledcount, disabledcount = d.disabledcount
FROM dupes d WHERE u.featureflagusageid = d.keepid;

DELETE FROM featureflagusages u USING featureflagusages k
WHERE k.featureflagid = u.featureflagid AND k.usagedate = u.usagedate
	AND COALESCE(k.departmentid, 0) = COALESCE(u.departmentid, 0)
	AND k.featureflagusageid < u.featureflagusageid;

CREATE UNIQUE INDEX IF NOT EXISTS uq_featureflagusages_flag_dept_date ON featureflagusages (featureflagid, (COALESCE(departmentid, 0)), usagedate);");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS uq_featureflagusages_flag_dept_date;");
		}
	}
}

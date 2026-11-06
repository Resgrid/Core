using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// One FeatureFlagUsages row per (FeatureFlagId, DepartmentId, UsageDate): every host flushes its own in-memory
	/// evaluation counters, and FeatureFlagUsageRepository.AddUsageAsync MERGEs them into that row. Collapses any
	/// duplicate rows the earlier append-only writes left (summing their counts) before adding the unique index.
	/// SQL Server unique indexes treat NULLs as equal, so the department-less row is unique per flag and day too.
	/// </summary>
	[Migration(310)]
	public class M0310_AddFeatureFlagUsagesUniqueIndex : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("FeatureFlagUsages").Index("UQ_FeatureFlagUsages_Flag_Dept_Date").Exists())
			{
				Execute.Sql(@"
;WITH Dupes AS (
	SELECT MIN(FeatureFlagUsageId) AS KeepId, SUM(EvaluationCount) AS EvaluationCount, SUM(EnabledCount) AS EnabledCount, SUM(DisabledCount) AS DisabledCount
	FROM FeatureFlagUsages
	GROUP BY FeatureFlagId, DepartmentId, UsageDate
	HAVING COUNT(*) > 1)
UPDATE u SET EvaluationCount = d.EvaluationCount, EnabledCount = d.EnabledCount, DisabledCount = d.DisabledCount
FROM FeatureFlagUsages u INNER JOIN Dupes d ON u.FeatureFlagUsageId = d.KeepId;

DELETE u FROM FeatureFlagUsages u
WHERE EXISTS (SELECT 1 FROM FeatureFlagUsages k
	WHERE k.FeatureFlagId = u.FeatureFlagId AND k.UsageDate = u.UsageDate
		AND (k.DepartmentId = u.DepartmentId OR (k.DepartmentId IS NULL AND u.DepartmentId IS NULL))
		AND k.FeatureFlagUsageId < u.FeatureFlagUsageId);");

				Create.Index("UQ_FeatureFlagUsages_Flag_Dept_Date")
					.OnTable("FeatureFlagUsages")
					.OnColumn("FeatureFlagId").Ascending()
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("UsageDate").Ascending()
					.WithOptions().Unique();
			}
		}

		public override void Down()
		{
			if (Schema.Table("FeatureFlagUsages").Index("UQ_FeatureFlagUsages_Flag_Dept_Date").Exists())
				Delete.Index("UQ_FeatureFlagUsages_Flag_Dept_Date").OnTable("FeatureFlagUsages");
		}
	}
}

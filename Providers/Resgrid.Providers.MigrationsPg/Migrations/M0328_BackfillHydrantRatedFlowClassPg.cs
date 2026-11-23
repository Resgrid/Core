using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0328: one-time backfill of the NFPA 291 flow class from the rated flow at 20 psi residual,
	/// Q_R = Q_F × ((S − 20) / (S − R))^0.54, on flow tests and hydrants that have an observed flow, a static pressure above 20 psi
	/// and a residual above 0 and below the static. The rated flow is rounded as numeric (halves away from zero, as the calculator
	/// does), and the arithmetic sits inside a CASE on the same guard so no other row is ever computed. Same number, lower-case
	/// identifiers; data only; re-running sets the same values.
	/// </summary>
	[Migration(328)]
	public class M0328_BackfillHydrantRatedFlowClassPg : Migration
	{
		private const string Eligible = "flowgpm > 0 AND staticpressurepsi > 20 AND residualpressurepsi > 0 AND residualpressurepsi < staticpressurepsi";

		public override void Up()
		{
			foreach (var (table, key) in new[] { ("rmshydrantflowtests", "rmshydrantflowtestid"), ("rmshydrants", "rmshydrantid") })
			{
				Execute.Sql(
					"WITH rated AS (" +
					"SELECT " + key + " AS id, CASE WHEN " + Eligible + " THEN ROUND((flowgpm::float8 * POWER((staticpressurepsi - 20)::float8 / (staticpressurepsi - residualpressurepsi)::float8, 0.54) / 10.0)::numeric, 0) * 10 END AS gpm " +
					"FROM " + table + " WHERE " + Eligible + "), " +
					"classed AS (SELECT id, CASE WHEN gpm <= 0 THEN 0 WHEN gpm >= 1500 THEN 1 WHEN gpm >= 1000 THEN 2 WHEN gpm >= 500 THEN 3 ELSE 4 END AS cls FROM rated WHERE gpm IS NOT NULL) " +
					"UPDATE " + table + " t SET flowclass = c.cls FROM classed c WHERE t." + key + " = c.id AND t.flowclass <> c.cls;");
			}
		}

		public override void Down()
		{
		}
	}
}

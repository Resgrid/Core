using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// NFPA 291 classes a hydrant by its rated flow at 20 psi residual, Q_R = Q_F × ((S − 20) / (S − R))^0.54, not by the observed
	/// flow (2026-11-22 fix; HydrantFlowCalculator). New flow tests and hydrant saves class from the rated flow; this one-time
	/// backfill corrects the FlowClass already stored on flow tests and hydrants that have an observed flow, a static pressure above
	/// 20 psi and a residual above 0 and below the static. Rows without those (imports, hand entries without pressures, class taken
	/// from another system's cap colour) are left alone. Rounding matches the calculator (nearest 10 gpm, halves away from zero) and
	/// the class bands are AA ≥ 1500, A ≥ 1000, B ≥ 500, C below (Unknown when the rated flow rounds to 0). The arithmetic sits
	/// inside a CASE on the same guard, because SQL Server may evaluate an APPLY expression before the WHERE filters the row (a
	/// zero divisor or a negative POWER base would fail the migration). Data only, no schema; re-running sets the same values. Down
	/// does nothing: the observed-flow class was the defect.
	/// </summary>
	[Migration(328)]
	public class M0328_BackfillHydrantRatedFlowClass : Migration
	{
		private const string Eligible =
			"t.[FlowGpm] > 0 AND t.[StaticPressurePsi] > 20 AND t.[ResidualPressurePsi] > 0 AND t.[ResidualPressurePsi] < t.[StaticPressurePsi]";

		private const string Rated =
			"CASE WHEN " + Eligible + " THEN ROUND(CAST(t.[FlowGpm] AS float) * POWER(CAST(t.[StaticPressurePsi] - 20 AS float) / CAST(t.[StaticPressurePsi] - t.[ResidualPressurePsi] AS float), 0.54) / 10.0, 0) * 10 END";

		private const string Class =
			"CASE WHEN r.[Gpm] <= 0 THEN 0 WHEN r.[Gpm] >= 1500 THEN 1 WHEN r.[Gpm] >= 1000 THEN 2 WHEN r.[Gpm] >= 500 THEN 3 ELSE 4 END";

		public override void Up()
		{
			foreach (var table in new[] { "RmsHydrantFlowTests", "RmsHydrants" })
			{
				Execute.Sql(
					"UPDATE t SET t.[FlowClass] = " + Class + " " +
					"FROM [" + table + "] t CROSS APPLY (SELECT " + Rated + " AS [Gpm]) r " +
					"WHERE " + Eligible + " AND r.[Gpm] IS NOT NULL AND t.[FlowClass] <> " + Class + ";");
			}
		}

		public override void Down()
		{
		}
	}
}

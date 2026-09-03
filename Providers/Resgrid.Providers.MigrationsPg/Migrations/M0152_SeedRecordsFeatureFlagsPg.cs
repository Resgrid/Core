using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Seeds the Records (RMS) feature flags, all OFF (registry section 3.6, RMS plan section 4.1):
	/// "Records.System" and the four dependent Field Records flags. PostgreSQL twin of the SQL Server
	/// migration; the identity PK is omitted so Postgres assigns it.
	/// </summary>
	[Migration(152)]
	public class M0152_SeedRecordsFeatureFlagsPg : Migration
	{
		// Keep in sync with Resgrid.Model.FeatureFlagKeys.Records*.
		private static readonly string[][] Flags =
		{
			new[] { "Records.System", "Records Module", "Records Management System: the feature-flagged successor to Logs. Off leaves Logs unchanged; on shows Records in the Logs sidebar position and never both. Department activation is a separate audited cutover. Seeded off; enable per department to roll out.", "Records" },
			new[] { "Records.Field.Responder", "Field Records - Responder", "Field Records surface in the Responder app. Requires Records.System, a compatible app version and a published Field-ready definition. Seeded off.", "Records" },
			new[] { "Records.Field.Unit", "Field Records - Unit", "Field Records surface in the Unit app (Unit Activity replaces Unit Logs). Requires Records.System, a compatible app version and a published Field-ready definition. Seeded off.", "Records" },
			new[] { "Records.Field.IncidentCommand", "Field Records - Incident Command", "Field Records surface in the IC app. Requires Records.System, a compatible app version and a published Field-ready definition. Seeded off.", "Records" },
			new[] { "Records.Field.Dispatch", "Field Records - Dispatch", "Field Records surface in the Dispatch app. Requires Records.System, a compatible app version and a published Field-ready definition. Seeded off.", "Records" }
		};

		public override void Up()
		{
			foreach (var flag in Flags)
			{
				Execute.Sql(
					"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
					"SELECT '" + flag[0] + "', '" + flag[1] + "', '" + flag[2] + "', '" + flag[3] + "', false " +
					"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = '" + flag[0] + "');");
			}
		}

		public override void Down()
		{
			foreach (var flag in Flags)
				Delete.FromTable("featureflags").Row(new { flagkey = flag[0] });
		}
	}
}

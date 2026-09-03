using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Seeds the Records (RMS) feature flags, all OFF (registry section 3.6, RMS plan section 4.1):
	/// "Records.System" gates the Records module (department-targetable; flag on shows Records in the
	/// Logs sidebar position, never both), and the four dependent Field Records flags gate the per-app
	/// surfaces delivered in RMS-1D. Flag state never blocks a legacy Log write by itself; the
	/// department cutover row (M0154) does that.
	/// </summary>
	[Migration(152)]
	public class M0152_SeedRecordsFeatureFlags : Migration
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
			// Guarded with IF NOT EXISTS so re-running the migration does not violate the unique FlagKey
			// index. FlagType, IsArchived, IsPermanent and CreatedOn fall back to their table defaults.
			foreach (var flag in Flags)
			{
				Execute.Sql(
					"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = '" + flag[0] + "') " +
					"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
					"VALUES ('" + flag[0] + "', '" + flag[1] + "', '" + flag[2] + "', '" + flag[3] + "', 0);");
			}
		}

		public override void Down()
		{
			foreach (var flag in Flags)
				Delete.FromTable("FeatureFlags").Row(new { FlagKey = flag[0] });
		}
	}
}

using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// RMS-6 records analytics (RMS plan section 6, RMS-6; registry M0187): seeds the Records.Analytics feature flag off.
	/// The dashboards are computed on demand over the existing Records, revision, due-state and prevention tables, so
	/// no table or index is added. Guarded for safe retry.
	/// </summary>
	[Migration(187)]
	public class M0187_AddRmsAnalyticsFlagPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'Records.Analytics', 'Records Analytics', 'RMS-6 response-performance, workload, executive, accreditation and community-risk dashboards over finalized Records. Requires Records.System. Seeded off.', 'Records', false WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Records.Analytics');");
		}

		public override void Down()
		{
			Delete.FromTable("featureflags").Row(new { flagkey = "Records.Analytics" });
		}
	}
}

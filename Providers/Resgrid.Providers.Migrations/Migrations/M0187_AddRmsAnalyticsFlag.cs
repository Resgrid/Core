using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// RMS-6 records analytics (RMS plan section 6, RMS-6; registry M0187): seeds the Records.Analytics feature flag off.
	/// The dashboards are computed on demand over the existing Records, revision, due-state and prevention tables, so
	/// no table or index is added. Guarded for safe retry.
	/// </summary>
	[Migration(187)]
	public class M0187_AddRmsAnalyticsFlag : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Records.Analytics') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('Records.Analytics', 'Records Analytics', 'RMS-6 response-performance, workload, executive, accreditation and community-risk dashboards over finalized Records. Requires Records.System. Seeded off.', 'Records', 0);");
		}

		public override void Down()
		{
			Delete.FromTable("FeatureFlags").Row(new { FlagKey = "Records.Analytics" });
		}
	}
}

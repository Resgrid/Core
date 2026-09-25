using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0238 (enhanced-ai-addon-plan.md): Enhanced AI add-on catalog row, aibillingaccounts, Admin
	/// Assist free-allowance columns on aiusageledger, and the six Ai.* capability flags with prerequisites on Ai.Enhanced.
	/// Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(238)]
	public class M0238_AddEnhancedAiAddonPg : Migration
	{
		private const string EnhancedAiAddonId = "e8bb4e6a-d61b-4654-844f-1afb25b60e93";
		private const string StripePriceId = "price_0UJZXtqJFDZJcnkVbvetyYlx";

		private static readonly (string Key, string Name, string Description)[] CapabilityFlags =
		{
			("Ai.Assistant", "AI assistant", "Enhanced AI: conversational fallback and incident questions over the live command board. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off."),
			("Ai.Narratives", "AI drafts", "Enhanced AI: incident report, after-action, ICS-201 briefing and call-brief drafts a person reviews before use. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off."),
			("Ai.Knowledge", "AI knowledge answers", "Enhanced AI: department knowledge answers with citations over documents, protocols, notes and pre-plans. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off."),
			("Ai.Search", "AI semantic search", "Enhanced AI Phase 2: hybrid semantic search beside the keyword index; needs the ai-dispatch GPU. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off."),
			("Ai.Voice", "AI transcription", "Enhanced AI Phase 2: dispatch audio and push-to-talk transcription; needs the ai-dispatch GPU. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off."),
			("Ai.Analytics", "AI analytics questions", "Enhanced AI: plain-language questions over records analytics, answered through typed queries. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off.")
		};

		public override void Up()
		{
			Execute.Sql(
				"INSERT INTO planaddons (planaddonid, addontype, cost, externalid, testexternalid) " +
				"SELECT '" + EnhancedAiAddonId + "', 5, 95, '" + StripePriceId + "', '' " +
				"WHERE NOT EXISTS (SELECT 1 FROM planaddons WHERE planaddonid = '" + EnhancedAiAddonId + "' OR addontype = 5);");
			// A row seeded before the Stripe price existed carries an empty externalid; fill it without touching an operator's value.
			Execute.Sql(
				"UPDATE planaddons SET externalid = '" + StripePriceId + "' " +
				"WHERE planaddonid = '" + EnhancedAiAddonId + "' AND (externalid IS NULL OR externalid = '');");

			if (!Schema.Table("aibillingaccounts").Exists())
			{
				Create.Table("aibillingaccounts")
					.WithColumn("departmentid").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("provider").AsString(20).NotNullable()
					.WithColumn("customerid").AsString(100).NotNullable()
					.WithColumn("planaddonid").AsString(36).NotNullable()
					.WithColumn("priceid").AsString(100).NotNullable()
					.WithColumn("subscriptionid").AsString(100).Nullable()
					.WithColumn("checkoutid").AsString(100).Nullable()
					.WithColumn("checkouturl").AsString(2048).Nullable()
					.WithColumn("checkoutexpireson").AsDateTime2().Nullable()
					.WithColumn("checkoutattempt").AsString(36).Nullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable();
			}

			// Ledger rows written before this migration are paid Admin Assist reservations; NULL feature/tier reads as that.
			if (!Schema.Table("aiusageledger").Column("feature").Exists())
				Alter.Table("aiusageledger").AddColumn("feature").AsString(40).Nullable();
			if (!Schema.Table("aiusageledger").Column("tier").Exists())
				Alter.Table("aiusageledger").AddColumn("tier").AsString(20).Nullable();
			if (!Schema.Table("aiusageledger").Column("createdonutc").Exists())
				Alter.Table("aiusageledger").AddColumn("createdonutc").AsDateTime().Nullable();
			if (!Schema.Table("aiusageledger").Index("ix_aiusageledger_allowance").Exists())
				Create.Index("ix_aiusageledger_allowance").OnTable("aiusageledger")
					.OnColumn("departmentid").Ascending().OnColumn("tier").Ascending().OnColumn("createdonutc").Ascending();

			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
				"SELECT 'Ai.Enhanced', 'Enhanced AI', 'Operator rollout; entitlement is checked separately.', 'AI', false " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Ai.Enhanced');");

			foreach (var flag in CapabilityFlags)
			{
				Execute.Sql(
					"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
					"SELECT '" + flag.Key + "', '" + flag.Name + "', '" + flag.Description + "', 'AI', false " +
					"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = '" + flag.Key + "');");

				Execute.Sql(
					"INSERT INTO featureflagprerequisites (featureflagid, requiredfeatureflagid, requiredvalue) " +
					"SELECT f.featureflagid, r.featureflagid, NULL FROM featureflags f CROSS JOIN featureflags r " +
					"WHERE f.flagkey = '" + flag.Key + "' AND r.flagkey = 'Ai.Enhanced' " +
					"AND NOT EXISTS (SELECT 1 FROM featureflagprerequisites p WHERE p.featureflagid = f.featureflagid AND p.requiredfeatureflagid = r.featureflagid);");
			}
		}

		public override void Down()
		{
			Execute.Sql("DO $guard$ BEGIN IF to_regclass('aibillingaccounts') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM aibillingaccounts) THEN DROP TABLE aibillingaccounts; END IF; END $guard$;");
		}
	}
}

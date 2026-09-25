using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0240 (AI dispatch, Enrich mode): metadata-only aidispatchaudits with its unique (departmentid, callid)
	/// claim index, and the Dispatch.AiTemplate flag seeded off with a prerequisite on Ai.Enhanced. Same number, lower-case
	/// identifiers, guarded for safe retry.
	/// </summary>
	[Migration(240)]
	public class M0240_AddAiDispatchEnrichmentPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("aidispatchaudits").Exists())
			{
				Create.Table("aidispatchaudits")
					.WithColumn("aidispatchauditid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().NotNullable()
					.WithColumn("mode").AsString(20).NotNullable()
					.WithColumn("outcome").AsString(40).NotNullable()
					.WithColumn("confidence").AsDecimal(5, 4).Nullable()
					.WithColumn("appliedfields").AsString(200).Nullable()
					.WithColumn("rejectedcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("relatedcallid").AsInt32().Nullable()
					.WithColumn("promptversion").AsString(64).Nullable()
					.WithColumn("modelname").AsString(128).Nullable()
					.WithColumn("modelrevision").AsString(64).Nullable()
					.WithColumn("runtimedigest").AsString(80).Nullable()
					.WithColumn("inputtokens").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("outputtokens").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("latencyms").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdonutc").AsDateTime2().NotNullable()
					.WithColumn("completedonutc").AsDateTime2().Nullable();
			}

			if (!Schema.Table("aidispatchaudits").Index("ux_aidispatchaudits_call").Exists())
				Create.Index("ux_aidispatchaudits_call").OnTable("aidispatchaudits")
					.OnColumn("departmentid").Ascending().OnColumn("callid").Ascending().WithOptions().Unique();
			if (!Schema.Table("aidispatchaudits").Index("ix_aidispatchaudits_recent").Exists())
				Create.Index("ix_aidispatchaudits_recent").OnTable("aidispatchaudits")
					.OnColumn("departmentid").Ascending().OnColumn("createdonutc").Descending();

			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
				"SELECT 'Dispatch.AiTemplate', 'AI dispatch', 'AI enrichment of calls created from AI-format dispatch emails (Enrich mode: the call is created and dispatched first). Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off.', 'AI', false " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Dispatch.AiTemplate');");

			Execute.Sql(
				"INSERT INTO featureflagprerequisites (featureflagid, requiredfeatureflagid, requiredvalue) " +
				"SELECT f.featureflagid, r.featureflagid, NULL FROM featureflags f CROSS JOIN featureflags r " +
				"WHERE f.flagkey = 'Dispatch.AiTemplate' AND r.flagkey = 'Ai.Enhanced' " +
				"AND NOT EXISTS (SELECT 1 FROM featureflagprerequisites p WHERE p.featureflagid = f.featureflagid AND p.requiredfeatureflagid = r.featureflagid);");
		}

		public override void Down()
		{
			Execute.Sql("DO $guard$ BEGIN IF to_regclass('aidispatchaudits') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM aidispatchaudits) THEN DROP TABLE aidispatchaudits; END IF; END $guard$;");
		}
	}
}

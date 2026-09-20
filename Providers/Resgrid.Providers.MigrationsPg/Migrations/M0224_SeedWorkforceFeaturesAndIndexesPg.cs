using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0224 (Workforce &amp; Business Operations plan, Phase E). Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(224)]
	public class M0224_SeedWorkforceFeaturesAndIndexesPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'Workforce.InternalCosting', 'Workforce internal costing', 'Protected employee compensation profiles, resource cost profiles and internal field-cost / margin runs for bids, calls and deployments (Workforce & Business Operations plan, Phase E). Requires Business.Operations and an Advanced Data Protection enrollment. Seeded off.', 'Business', FALSE WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Workforce.InternalCosting');");
			Execute.Sql("INSERT INTO featureflagprerequisites (featureflagid, requiredfeatureflagid, requiredvalue) SELECT f.featureflagid, r.featureflagid, NULL FROM featureflags f CROSS JOIN featureflags r WHERE f.flagkey = 'Workforce.InternalCosting' AND r.flagkey = 'Business.Operations' AND NOT EXISTS (SELECT 1 FROM featureflagprerequisites p WHERE p.featureflagid = f.featureflagid AND p.requiredfeatureflagid = r.featureflagid);");
			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'Compliance.CaliforniaPayDataReporting', 'California pay data reporting', 'California CRD (Government Code 12999) Payroll Employee and Labor Contractor Employee report preparation and export (Workforce & Business Operations plan, Phase E). Requires Business.Operations and an Enabled Advanced Data Protection enrollment; the user files through the CRD portal. Seeded off.', 'Business', FALSE WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Compliance.CaliforniaPayDataReporting');");
			Execute.Sql("INSERT INTO featureflagprerequisites (featureflagid, requiredfeatureflagid, requiredvalue) SELECT f.featureflagid, r.featureflagid, NULL FROM featureflags f CROSS JOIN featureflags r WHERE f.flagkey = 'Compliance.CaliforniaPayDataReporting' AND r.flagkey = 'Business.Operations' AND NOT EXISTS (SELECT 1 FROM featureflagprerequisites p WHERE p.featureflagid = f.featureflagid AND p.requiredfeatureflagid = r.featureflagid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_workforceworkentries_employment ON workforceworkentries (workforceemploymentid, workdate);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_workforceannualpayfacts_version ON workforceannualpayfacts (workforceemploymentid, reportingyear, reporttype, COALESCE(clientallocationkey, ''), version) WHERE isdeleted = FALSE;");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_paydatareportingdemographics_worker ON paydatareportingdemographics (workforceworkerid, effectiveon) WHERE isdeleted = FALSE;");
		}

		public override void Down()
		{
			Execute.Sql("DELETE FROM featureflagprerequisites WHERE featureflagid IN (SELECT featureflagid FROM featureflags WHERE flagkey = 'Workforce.InternalCosting');");
			Execute.Sql("DELETE FROM featureflags WHERE flagkey = 'Workforce.InternalCosting';");
			Execute.Sql("DELETE FROM featureflagprerequisites WHERE featureflagid IN (SELECT featureflagid FROM featureflags WHERE flagkey = 'Compliance.CaliforniaPayDataReporting');");
			Execute.Sql("DELETE FROM featureflags WHERE flagkey = 'Compliance.CaliforniaPayDataReporting';");
		}
	}
}

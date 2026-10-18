using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Department Import dormancy and rollout flags (see the SQL Server M0277): departmentmembers.importactivationstate and
	/// the Onboarding.* flags, seeded off; Onboarding.ManagedMigration is operator-only and permanent.
	/// </summary>
	[Migration(279)]
	public class M0279_AddMemberImportActivationStatePg : Migration
	{
		private const string MasterFlag = "Onboarding.MigrationWizard";

		private static readonly (string Key, string Name, string Description, bool Permanent, string[] Requires)[] Flags =
		{
			(MasterFlag, "Import & Migration", "Department Import & Migration workspace (self-service import of configuration, people and apparatus). Master switch; seeded off.", false, new string[0]),
			("Onboarding.OperationsData", "Import: operations data", "Import & Migration wave 2B: dispatch configuration, shifts, calendar, geography, contacts and Records prevention data. Requires Onboarding.MigrationWizard. Seeded off.", false, new[] { MasterFlag }),
			("Onboarding.ReadinessData", "Import: readiness and business data", "Import & Migration wave 2C: inventory, checklists, maintenance plans, open work orders and Business Ops profiles. Requires Onboarding.MigrationWizard. Seeded off.", false, new[] { MasterFlag }),
			("Onboarding.OperationalHistory", "Import: operational history", "Import & Migration Phase 3: closed calls, training results and certification history, excluded from reports by default. Requires Onboarding.MigrationWizard. Seeded off.", false, new[] { MasterFlag }),
			("Onboarding.HistoricalRecords", "Import: historical Records", "Import & Migration Phase 3R: third-party incident history into Records as ImportedHistorical. Requires Onboarding.MigrationWizard and Records.System. Seeded off.", false, new[] { MasterFlag, "Records.System" }),
			("Onboarding.VendorPacks", "Import: vendor packs", "Import & Migration Phase 3B: sample-validated vendor export packs. Requires Onboarding.MigrationWizard. Seeded off.", false, new[] { MasterFlag }),
			("Onboarding.DepartmentPackage", "Import: department package", "Import & Migration Phase 4: Resgrid department package export, cluster moves and configuration cloning. Requires Onboarding.MigrationWizard. Seeded off.", false, new[] { MasterFlag }),
			("Onboarding.ManagedMigration", "Import: managed migration", "Operator-only: enables the Operate support grant (Resgrid staff run an import end to end with two-person control) for a department with an agreed engagement. Never enabled globally; set per department by BackOffice. Requires Onboarding.MigrationWizard.", true, new[] { MasterFlag })
		};

		public override void Up()
		{
			if (!Schema.Table("departmentmembers").Column("importactivationstate").Exists())
				Alter.Table("departmentmembers").AddColumn("importactivationstate").AsInt32().Nullable();

			foreach (var flag in Flags)
			{
				Execute.Sql(
					"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally, ispermanent) " +
					"SELECT '" + flag.Key + "', '" + flag.Name + "', '" + flag.Description + "', 'Onboarding', false, " + (flag.Permanent ? "true" : "false") + " " +
					"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = '" + flag.Key + "');");

				foreach (var required in flag.Requires)
				{
					Execute.Sql(
						"INSERT INTO featureflagprerequisites (featureflagid, requiredfeatureflagid, requiredvalue) " +
						"SELECT f.featureflagid, r.featureflagid, NULL FROM featureflags f CROSS JOIN featureflags r " +
						"WHERE f.flagkey = '" + flag.Key + "' AND r.flagkey = '" + required + "' " +
						"AND NOT EXISTS (SELECT 1 FROM featureflagprerequisites p WHERE p.featureflagid = f.featureflagid AND p.requiredfeatureflagid = r.featureflagid);");
				}
			}
		}

		public override void Down()
		{
			if (Schema.Table("departmentmembers").Column("importactivationstate").Exists())
				Delete.Column("importactivationstate").FromTable("departmentmembers");
		}
	}
}

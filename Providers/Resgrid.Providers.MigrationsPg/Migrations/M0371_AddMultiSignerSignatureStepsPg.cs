using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0371_AddMultiSignerSignatureSteps (e-Signature S3): signaturerequestsigners gains signerkind,
	/// requiredlevel, eligibilityjson, steplabel, declinereason, turnstartedon and the reminder columns, plus the member-steps and
	/// due-reminder indexes. Guarded; Down removes requests holding member steps first, then the indexes and columns.
	/// </summary>
	[Migration(371)]
	public class M0371_AddMultiSignerSignatureStepsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE signaturerequestsigners ADD COLUMN IF NOT EXISTS signerkind integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE signaturerequestsigners ADD COLUMN IF NOT EXISTS requiredlevel integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE signaturerequestsigners ADD COLUMN IF NOT EXISTS eligibilityjson text NULL;");
			Execute.Sql("ALTER TABLE signaturerequestsigners ADD COLUMN IF NOT EXISTS steplabel citext NULL;");
			Execute.Sql("ALTER TABLE signaturerequestsigners ADD COLUMN IF NOT EXISTS declinereason citext NULL;");
			Execute.Sql("ALTER TABLE signaturerequestsigners ADD COLUMN IF NOT EXISTS turnstartedon timestamp NULL;");
			Execute.Sql("ALTER TABLE signaturerequestsigners ADD COLUMN IF NOT EXISTS nextreminderon timestamp NULL;");
			Execute.Sql("ALTER TABLE signaturerequestsigners ADD COLUMN IF NOT EXISTS lastremindedon timestamp NULL;");
			Execute.Sql("ALTER TABLE signaturerequestsigners ADD COLUMN IF NOT EXISTS remindercount integer NOT NULL DEFAULT 0;");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_signaturerequestsigners_member ON signaturerequestsigners (departmentid, userid, status) WHERE userid IS NOT NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_signaturerequestsigners_reminder ON signaturerequestsigners (nextreminderon) WHERE nextreminderon IS NOT NULL;");
		}

		public override void Down()
		{
			Execute.Sql(@"
DO $$
BEGIN
	IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'signaturerequestsigners' AND column_name = 'signerkind') THEN
		DELETE FROM signaturecaptures c WHERE EXISTS (SELECT 1 FROM signaturerequestsigners s WHERE s.signaturerequestid = c.signaturerequestid AND s.signerkind = 1);
		DELETE FROM signaturerequests r WHERE EXISTS (SELECT 1 FROM signaturerequestsigners s WHERE s.signaturerequestid = r.signaturerequestid AND s.signerkind = 1);
		DELETE FROM signaturerequestsigners s WHERE NOT EXISTS (SELECT 1 FROM signaturerequests r WHERE r.signaturerequestid = s.signaturerequestid);
	END IF;
END $$;");

			Execute.Sql("DROP INDEX IF EXISTS ix_signaturerequestsigners_reminder;");
			Execute.Sql("DROP INDEX IF EXISTS ix_signaturerequestsigners_member;");
			Execute.Sql("ALTER TABLE signaturerequestsigners DROP COLUMN IF EXISTS remindercount;");
			Execute.Sql("ALTER TABLE signaturerequestsigners DROP COLUMN IF EXISTS lastremindedon;");
			Execute.Sql("ALTER TABLE signaturerequestsigners DROP COLUMN IF EXISTS nextreminderon;");
			Execute.Sql("ALTER TABLE signaturerequestsigners DROP COLUMN IF EXISTS turnstartedon;");
			Execute.Sql("ALTER TABLE signaturerequestsigners DROP COLUMN IF EXISTS declinereason;");
			Execute.Sql("ALTER TABLE signaturerequestsigners DROP COLUMN IF EXISTS steplabel;");
			Execute.Sql("ALTER TABLE signaturerequestsigners DROP COLUMN IF EXISTS eligibilityjson;");
			Execute.Sql("ALTER TABLE signaturerequestsigners DROP COLUMN IF EXISTS requiredlevel;");
			Execute.Sql("ALTER TABLE signaturerequestsigners DROP COLUMN IF EXISTS signerkind;");
		}
	}
}

using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0345_TrainingT1Columns: trainings (category foreign key, set null; parity fields),
	/// calendaritemtypes (countsastraining, defaultcategoryid) and personnelcertificationcredits (learningrecordid).
	/// </summary>
	[Migration(345)]
	public class M0345_TrainingT1ColumnsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE trainings ADD COLUMN IF NOT EXISTS methodoftraining integer NULL;");
			Execute.Sql("ALTER TABLE trainings ADD COLUMN IF NOT EXISTS objectives character varying(2000) NULL;");
			Execute.Sql("ALTER TABLE trainings ADD COLUMN IF NOT EXISTS tags character varying(500) NULL;");
			Execute.Sql("ALTER TABLE trainings ADD COLUMN IF NOT EXISTS externalcode character varying(64) NULL;");

			Execute.Sql("ALTER TABLE calendaritemtypes ADD COLUMN IF NOT EXISTS countsastraining boolean NOT NULL DEFAULT false;");
			Execute.Sql("ALTER TABLE calendaritemtypes ADD COLUMN IF NOT EXISTS defaultcategoryid integer NULL;");

			Execute.Sql("ALTER TABLE personnelcertificationcredits ADD COLUMN IF NOT EXISTS learningrecordid integer NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_personnelcertificationcredits_learningrecord ON personnelcertificationcredits (learningrecordid) WHERE learningrecordid IS NOT NULL;");

			// T0 stored a bare number in trainings.categoryid; anything that is not one of the department's categories is cleared
			// before the key is added.
			Execute.Sql(
				"DO $$ BEGIN " +
				"IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_trainings_trainingcategories') THEN " +
				"UPDATE trainings t SET categoryid = NULL WHERE t.categoryid IS NOT NULL AND NOT EXISTS (SELECT 1 FROM trainingcategories c WHERE c.trainingcategoryid = t.categoryid AND c.departmentid = t.departmentid); " +
				"ALTER TABLE trainings ADD CONSTRAINT fk_trainings_trainingcategories FOREIGN KEY (categoryid) REFERENCES trainingcategories (trainingcategoryid) ON DELETE SET NULL; " +
				"END IF; END $$;");

			Execute.Sql(
				"DO $$ BEGIN " +
				"IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_calendaritemtypes_trainingcategories') THEN " +
				"ALTER TABLE calendaritemtypes ADD CONSTRAINT fk_calendaritemtypes_trainingcategories FOREIGN KEY (defaultcategoryid) REFERENCES trainingcategories (trainingcategoryid) ON DELETE SET NULL; " +
				"END IF; END $$;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE calendaritemtypes DROP CONSTRAINT IF EXISTS fk_calendaritemtypes_trainingcategories;");
			Execute.Sql("ALTER TABLE trainings DROP CONSTRAINT IF EXISTS fk_trainings_trainingcategories;");
			Execute.Sql("DROP INDEX IF EXISTS ix_personnelcertificationcredits_learningrecord;");
			Execute.Sql("ALTER TABLE personnelcertificationcredits DROP COLUMN IF EXISTS learningrecordid;");
			Execute.Sql("ALTER TABLE calendaritemtypes DROP COLUMN IF EXISTS defaultcategoryid;");
			Execute.Sql("ALTER TABLE calendaritemtypes DROP COLUMN IF EXISTS countsastraining;");
			Execute.Sql("ALTER TABLE trainings DROP COLUMN IF EXISTS externalcode;");
			Execute.Sql("ALTER TABLE trainings DROP COLUMN IF EXISTS tags;");
			Execute.Sql("ALTER TABLE trainings DROP COLUMN IF EXISTS objectives;");
			Execute.Sql("ALTER TABLE trainings DROP COLUMN IF EXISTS methodoftraining;");
		}
	}
}

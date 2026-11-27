using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0338_TrainingT0Columns: Training T0 columns on trainings, trainingquestions and trainingusers, the
	/// duplicate-assignee clean-up (most complete row kept), the unique index on (trainingid, userid) and the one-time
	/// Passed / AssignedOn / NotifiedOn backfills. categoryid carries no foreign key until T1 adds trainingcategories.
	/// userid is citext, so duplicates that differ only by case are folded too.
	/// </summary>
	[Migration(338)]
	public class M0338_TrainingT0ColumnsPg : Migration
	{
		private static readonly string[][] TrainingColumns =
		{
			new[] { "allowselfenroll", "boolean NOT NULL DEFAULT false" },
			new[] { "startson", "timestamp without time zone NULL" },
			new[] { "duedays", "integer NULL" },
			new[] { "closedtonewassignees", "boolean NOT NULL DEFAULT false" },
			new[] { "repeatmonths", "integer NULL" },
			new[] { "nextrepeaton", "timestamp without time zone NULL" },
			new[] { "quizattemptlimit", "integer NULL" },
			new[] { "onequestionperpage", "boolean NOT NULL DEFAULT false" },
			new[] { "reminderleaddayscsv", "citext NULL" },
			new[] { "sendoverduenotices", "boolean NOT NULL DEFAULT false" },
			new[] { "overduerepeatdays", "integer NULL" },
			new[] { "remindermessage", "citext NULL" },
			new[] { "hours", "numeric(9,2) NULL" },
			new[] { "cehours", "numeric(9,2) NULL" },
			new[] { "ceprovider", "citext NULL" },
			new[] { "cecoursenumber", "citext NULL" },
			new[] { "categoryid", "integer NULL" },
			new[] { "archivedon", "timestamp without time zone NULL" },
			new[] { "archivedbyuserid", "citext NULL" },
			new[] { "updatedon", "timestamp without time zone NULL" },
			new[] { "updatedbyuserid", "citext NULL" }
		};

		private static readonly string[][] TrainingUserColumns =
		{
			new[] { "passed", "boolean NULL" },
			new[] { "assignedon", "timestamp without time zone NULL" },
			new[] { "dueon", "timestamp without time zone NULL" },
			new[] { "assignmentsource", "integer NOT NULL DEFAULT 0" },
			new[] { "notifiedon", "timestamp without time zone NULL" },
			new[] { "remindedon", "timestamp without time zone NULL" },
			new[] { "lastreminderleaddays", "integer NULL" },
			new[] { "overduenotifiedon", "timestamp without time zone NULL" },
			new[] { "removedon", "timestamp without time zone NULL" },
			new[] { "removedbyuserid", "citext NULL" },
			new[] { "originaldueon", "timestamp without time zone NULL" },
			new[] { "extensionreason", "citext NULL" },
			new[] { "extendedbyuserid", "citext NULL" },
			new[] { "extendedon", "timestamp without time zone NULL" },
			new[] { "exempton", "timestamp without time zone NULL" },
			new[] { "exemptreason", "citext NULL" },
			new[] { "exemptbyuserid", "citext NULL" },
			new[] { "exemptuntil", "timestamp without time zone NULL" },
			new[] { "attemptcount", "integer NOT NULL DEFAULT 0" },
			new[] { "cycle", "integer NOT NULL DEFAULT 0" }
		};

		public override void Up()
		{
			foreach (var column in TrainingColumns)
				Execute.Sql($"ALTER TABLE trainings ADD COLUMN IF NOT EXISTS {column[0]} {column[1]};");

			foreach (var column in TrainingUserColumns)
				Execute.Sql($"ALTER TABLE trainingusers ADD COLUMN IF NOT EXISTS {column[0]} {column[1]};");

			Execute.Sql("ALTER TABLE trainingquestions ADD COLUMN IF NOT EXISTS type integer NOT NULL DEFAULT 0;");

			Execute.Sql(@"
DELETE FROM trainingusers WHERE traininguserid IN (
	SELECT traininguserid FROM (
		SELECT traininguserid, ROW_NUMBER() OVER (PARTITION BY trainingid, userid
			ORDER BY complete DESC, (CASE WHEN passed THEN 1 ELSE 0 END) DESC, viewed DESC, score DESC, completedon ASC NULLS LAST, traininguserid ASC) AS rn
		FROM trainingusers) ranked
	WHERE ranked.rn > 1)
AND NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'ux_trainingusers_trainingid_userid');");

			Execute.Sql(@"
UPDATE trainingusers tu SET passed = (tu.score >= t.minimumscore)
FROM trainings t
WHERE t.trainingid = tu.trainingid AND tu.passed IS NULL AND tu.complete = true
AND EXISTS (SELECT 1 FROM trainingquestions q WHERE q.trainingid = t.trainingid);");

			Execute.Sql("UPDATE trainingusers SET complete = false, completedon = NULL WHERE passed = false AND complete = true;");

			Execute.Sql(@"
UPDATE trainingusers tu SET assignedon = t.createdon
FROM trainings t
WHERE t.trainingid = tu.trainingid AND tu.assignedon IS NULL;");

			Execute.Sql(@"
UPDATE trainingusers tu SET notifiedon = t.notified
FROM trainings t
WHERE t.trainingid = tu.trainingid AND tu.notifiedon IS NULL AND t.notified IS NOT NULL;");

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_trainingusers_trainingid_userid ON trainingusers (trainingid, userid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_trainingusers_complete_dueon ON trainingusers (complete, dueon) INCLUDE (trainingid, notifiedon);");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS ix_trainingusers_complete_dueon;");
			Execute.Sql("DROP INDEX IF EXISTS ux_trainingusers_trainingid_userid;");
			Execute.Sql(@"DO $$ BEGIN
IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'trainingusers' AND column_name = 'passed') THEN
	UPDATE trainingusers SET complete = true WHERE passed = false AND complete = false;
END IF;
END $$;");

			Execute.Sql("ALTER TABLE trainingquestions DROP COLUMN IF EXISTS type;");
			foreach (var column in TrainingUserColumns)
				Execute.Sql($"ALTER TABLE trainingusers DROP COLUMN IF EXISTS {column[0]};");
			foreach (var column in TrainingColumns)
				Execute.Sql($"ALTER TABLE trainings DROP COLUMN IF EXISTS {column[0]};");
		}
	}
}

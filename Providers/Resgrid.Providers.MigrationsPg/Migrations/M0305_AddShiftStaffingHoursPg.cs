using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// On-shift hours and static shift staffing. PostgreSQL twin of the SQL Server migration: workshiftfills.filledbyid
	/// (created integer in M0014) holds a user id, so it becomes citext like the other user-id columns; plus the
	/// department/day index on shiftstaffings and the day index on workshiftfills.
	/// </summary>
	[Migration(305)]
	public class M0305_AddShiftStaffingHoursPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
				DO $$
				BEGIN
					IF EXISTS (SELECT 1 FROM information_schema.columns
					           WHERE table_name = 'workshiftfills' AND column_name = 'filledbyid' AND data_type = 'integer') THEN
						ALTER TABLE workshiftfills ALTER COLUMN filledbyid TYPE citext USING filledbyid::text::citext;
					END IF;
				END $$;");

			Execute.Sql("CREATE INDEX IF NOT EXISTS \"IX_ShiftStaffings_Department_ShiftDay\" ON shiftstaffings (departmentid, shiftday);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS \"IX_WorkshiftFills_WorkshiftDayId\" ON workshiftfills (workshiftdayid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS \"IX_WorkshiftFills_WorkshiftDayId\";");
			Execute.Sql("DROP INDEX IF EXISTS \"IX_ShiftStaffings_Department_ShiftDay\";");

			// filledbyid stays citext: the model has always been a string, and narrowing back to integer would fail on every
			// user id saved since. Intentionally irreversible.
		}
	}
}

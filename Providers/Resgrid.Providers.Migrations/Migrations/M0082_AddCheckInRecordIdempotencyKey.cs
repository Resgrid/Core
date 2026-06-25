using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Offline-first action idempotency: a client-supplied key on check-in records so a replayed offline check-in
	/// dedups instead of creating a duplicate row. See docs/architecture/offline-first-architecture.md.
	/// </summary>
	[Migration(82)]
	public class M0082_AddCheckInRecordIdempotencyKey : Migration
	{
		public override void Up()
		{
			if (Schema.Table("CheckInRecords").Exists() && !Schema.Table("CheckInRecords").Column("IdempotencyKey").Exists())
			{
				Alter.Table("CheckInRecords").AddColumn("IdempotencyKey").AsString(128).Nullable();

				// At most one check-in per (department, idempotency key). Filtered so the many NULL-key (live UI)
				// check-ins don't collide. Backstops the check-then-insert race in
				// CheckInTimerService.PerformCheckInAsync (which adopts the winner on violation).
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_CheckInRecords_Department_IdempotencyKey ON CheckInRecords (DepartmentId, IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;");
			}
		}

		public override void Down()
		{
			if (Schema.Table("CheckInRecords").Exists() && Schema.Table("CheckInRecords").Column("IdempotencyKey").Exists())
			{
				Execute.Sql("DROP INDEX IF EXISTS UX_CheckInRecords_Department_IdempotencyKey ON CheckInRecords;");
				Delete.Column("IdempotencyKey").FromTable("CheckInRecords");
			}
		}
	}
}

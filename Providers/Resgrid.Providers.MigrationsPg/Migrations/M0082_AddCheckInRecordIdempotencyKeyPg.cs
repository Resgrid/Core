using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Offline-first action idempotency (PostgreSQL): a client-supplied key on check-in records so a replayed offline
	/// check-in dedups instead of creating a duplicate row. See docs/architecture/offline-first-architecture.md.
	/// </summary>
	[Migration(82)]
	public class M0082_AddCheckInRecordIdempotencyKeyPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("CheckInRecords".ToLower()).Exists() && !Schema.Table("CheckInRecords".ToLower()).Column("IdempotencyKey".ToLower()).Exists())
			{
				Alter.Table("CheckInRecords".ToLower()).AddColumn("IdempotencyKey".ToLower()).AsCustom("citext").Nullable();

				// At most one check-in per (department, idempotency key). Partial so the many NULL-key (live UI)
				// check-ins don't collide. Backstops the check-then-insert race in PerformCheckInAsync.
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_checkinrecords_department_idempotencykey ON checkinrecords (departmentid, idempotencykey) WHERE idempotencykey IS NOT NULL;");
			}
		}

		public override void Down()
		{
			if (Schema.Table("CheckInRecords".ToLower()).Exists() && Schema.Table("CheckInRecords".ToLower()).Column("IdempotencyKey".ToLower()).Exists())
			{
				Execute.Sql("DROP INDEX IF EXISTS ux_checkinrecords_department_idempotencykey;");
				Delete.Column("IdempotencyKey".ToLower()).FromTable("CheckInRecords".ToLower());
			}
		}
	}
}

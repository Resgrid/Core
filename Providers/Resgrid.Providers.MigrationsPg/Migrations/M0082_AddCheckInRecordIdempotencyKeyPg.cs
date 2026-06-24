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
			}
		}

		public override void Down()
		{
			if (Schema.Table("CheckInRecords".ToLower()).Exists() && Schema.Table("CheckInRecords".ToLower()).Column("IdempotencyKey".ToLower()).Exists())
			{
				Delete.Column("IdempotencyKey".ToLower()).FromTable("CheckInRecords".ToLower());
			}
		}
	}
}

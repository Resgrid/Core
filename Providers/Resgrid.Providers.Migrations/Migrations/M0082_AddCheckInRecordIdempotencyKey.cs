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
			}
		}

		public override void Down()
		{
			if (Schema.Table("CheckInRecords").Exists() && Schema.Table("CheckInRecords").Column("IdempotencyKey").Exists())
			{
				Delete.Column("IdempotencyKey").FromTable("CheckInRecords");
			}
		}
	}
}

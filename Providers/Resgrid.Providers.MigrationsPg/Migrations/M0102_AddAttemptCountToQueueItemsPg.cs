using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(102)]
	public class M0102_AddAttemptCountToQueueItemsPg : Migration
	{
		public override void Up()
		{
			// Bounded-retry counter for system queue items (e.g. department deletion) so a
			// transient execution failure can be retried without looping forever.
			Alter.Table("QueueItems".ToLower()).AddColumn("AttemptCount".ToLower()).AsInt32().NotNullable().WithDefaultValue(0);
		}

		public override void Down()
		{

		}
	}
}

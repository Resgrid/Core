using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(102)]
	public class M0102_AddAttemptCountToQueueItems : Migration
	{
		public override void Up()
		{
			// Bounded-retry counter for system queue items (e.g. department deletion) so a
			// transient execution failure can be retried without looping forever.
			Alter.Table("QueueItems").AddColumn("AttemptCount").AsInt32().NotNullable().WithDefaultValue(0);
		}

		public override void Down()
		{

		}
	}
}

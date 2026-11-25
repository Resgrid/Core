using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0334: trainings older than the notifier's 7-day "new" window are stamped as announced, so the
	/// training notifier (which never sent anything until it loaded assignees) does not announce every old training on its
	/// first working run. No Down: the stamps cannot be told from real ones.
	/// </summary>
	[Migration(334)]
	public class M0334_MarkOldTrainingsNotifiedPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("UPDATE trainings SET notified = createdon WHERE notified IS NULL AND createdon < (now() AT TIME ZONE 'utc') - interval '7 days';");
		}

		public override void Down()
		{
		}
	}
}

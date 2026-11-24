using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// The training notifier never sent anything: it loaded trainings without their assignees and skipped every one,
	/// including the step that stamps Notified. So every training still looks unannounced. Now that the notifier works,
	/// the first run would tell every assignee about every training the department ever made. Trainings older than the
	/// notifier's own "new" window (TrainingNoticePlanner.NewNoticeMaxAgeDays, 7 days) are stamped as announced at their
	/// creation time; newer ones get the notice they were owed. The notifier applies the same limit in code, so an
	/// install that misses this migration is still safe. No Down: the stamps cannot be told from real ones.
	/// </summary>
	[Migration(334)]
	public class M0334_MarkOldTrainingsNotified : Migration
	{
		public override void Up()
		{
			Execute.Sql("UPDATE [Trainings] SET [Notified] = [CreatedOn] WHERE [Notified] IS NULL AND [CreatedOn] < DATEADD(day, -7, SYSUTCDATETIME());");
		}

		public override void Down()
		{
		}
	}
}

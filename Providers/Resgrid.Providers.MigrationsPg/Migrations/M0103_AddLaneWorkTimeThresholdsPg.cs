using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Per-lane work-time (crew fatigue) indicator thresholds: minutes before the lane work light
	/// turns amber/red (0 = disabled; clients fall back to their own defaults). Denormalized onto
	/// runtime nodes from the template role at seeding, like the other lane limits.
	/// </summary>
	[Migration(103)]
	public class M0103_AddLaneWorkTimeThresholdsPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("commandstructurenodes").Exists())
			{
				foreach (var column in new[] { "worktimeamberminutes", "worktimeredminutes" })
				{
					if (!Schema.Table("commandstructurenodes").Column(column).Exists())
					{
						Alter.Table("commandstructurenodes")
							.AddColumn(column).AsInt32().NotNullable().WithDefaultValue(0);
					}
				}
			}

			if (Schema.Table("commanddefinitionroles").Exists())
			{
				foreach (var column in new[] { "worktimeamberminutes", "worktimeredminutes" })
				{
					if (!Schema.Table("commanddefinitionroles").Column(column).Exists())
					{
						Alter.Table("commanddefinitionroles")
							.AddColumn(column).AsInt32().NotNullable().WithDefaultValue(0);
					}
				}
			}
		}

		public override void Down()
		{
			if (Schema.Table("commandstructurenodes").Exists())
			{
				foreach (var column in new[] { "worktimeamberminutes", "worktimeredminutes" })
				{
					if (Schema.Table("commandstructurenodes").Column(column).Exists())
						Delete.Column(column).FromTable("commandstructurenodes");
				}
			}

			if (Schema.Table("commanddefinitionroles").Exists())
			{
				foreach (var column in new[] { "worktimeamberminutes", "worktimeredminutes" })
				{
					if (Schema.Table("commanddefinitionroles").Column(column).Exists())
						Delete.Column(column).FromTable("commanddefinitionroles");
				}
			}
		}
	}
}

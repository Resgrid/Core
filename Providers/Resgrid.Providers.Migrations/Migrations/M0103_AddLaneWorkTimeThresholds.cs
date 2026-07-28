using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Per-lane work-time (crew fatigue) indicator thresholds: minutes before the lane work light
	/// turns amber/red (0 = disabled; clients fall back to their own defaults). Denormalized onto
	/// runtime nodes from the template role at seeding, like the other lane limits.
	/// </summary>
	[Migration(103)]
	public class M0103_AddLaneWorkTimeThresholds : Migration
	{
		public override void Up()
		{
			if (Schema.Table("CommandStructureNodes").Exists())
			{
				foreach (var column in new[] { "WorkTimeAmberMinutes", "WorkTimeRedMinutes" })
				{
					if (!Schema.Table("CommandStructureNodes").Column(column).Exists())
					{
						Alter.Table("CommandStructureNodes")
							.AddColumn(column).AsInt32().NotNullable().WithDefaultValue(0);
					}
				}
			}

			if (Schema.Table("CommandDefinitionRoles").Exists())
			{
				foreach (var column in new[] { "WorkTimeAmberMinutes", "WorkTimeRedMinutes" })
				{
					if (!Schema.Table("CommandDefinitionRoles").Column(column).Exists())
					{
						Alter.Table("CommandDefinitionRoles")
							.AddColumn(column).AsInt32().NotNullable().WithDefaultValue(0);
					}
				}
			}
		}

		public override void Down()
		{
			if (Schema.Table("CommandStructureNodes").Exists())
			{
				foreach (var column in new[] { "WorkTimeAmberMinutes", "WorkTimeRedMinutes" })
				{
					if (Schema.Table("CommandStructureNodes").Column(column).Exists())
						Delete.Column(column).FromTable("CommandStructureNodes");
				}
			}

			if (Schema.Table("CommandDefinitionRoles").Exists())
			{
				foreach (var column in new[] { "WorkTimeAmberMinutes", "WorkTimeRedMinutes" })
				{
					if (Schema.Table("CommandDefinitionRoles").Column(column).Exists())
						Delete.Column(column).FromTable("CommandDefinitionRoles");
				}
			}
		}
	}
}

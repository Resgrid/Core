using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Close-out outcome (Successful/Partial/Unsuccessful) and an optional completion note on tactical
	/// objectives, recorded when an objective is completed.
	/// </summary>
	[Migration(96)]
	public class M0096_AddObjectiveOutcome : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("TacticalObjectives").Exists())
				return;

			if (!Schema.Table("TacticalObjectives").Column("Outcome").Exists())
				Alter.Table("TacticalObjectives").AddColumn("Outcome").AsInt32().NotNullable().WithDefaultValue(0);

			if (!Schema.Table("TacticalObjectives").Column("CompletionNote").Exists())
				Alter.Table("TacticalObjectives").AddColumn("CompletionNote").AsString(2000).Nullable();
		}

		public override void Down()
		{
			if (!Schema.Table("TacticalObjectives").Exists())
				return;

			foreach (var column in new[] { "Outcome", "CompletionNote" })
			{
				if (Schema.Table("TacticalObjectives").Column(column).Exists())
					Delete.Column(column).FromTable("TacticalObjectives");
			}
		}
	}
}

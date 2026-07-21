using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Command-level incident needs (tracked to fulfillment), objective progress/priority/description,
	/// lane links to objectives/needs, optional primary/secondary lane leads (Resgrid user or external
	/// contact), and command-level estimated end / important information.
	/// </summary>
	[Migration(93)]
	public class M0093_AddIncidentNeedsLaneLeadsAndObjectiveProgress : Migration
	{
		public override void Up()
		{
			if (Schema.Table("IncidentCommands").Exists())
			{
				if (!Schema.Table("IncidentCommands").Column("EstimatedEndOn").Exists())
					Alter.Table("IncidentCommands").AddColumn("EstimatedEndOn").AsDateTime2().Nullable();

				if (!Schema.Table("IncidentCommands").Column("ImportantInformation").Exists())
					Alter.Table("IncidentCommands").AddColumn("ImportantInformation").AsString(int.MaxValue).Nullable();
			}

			if (Schema.Table("TacticalObjectives").Exists())
			{
				if (!Schema.Table("TacticalObjectives").Column("Description").Exists())
					Alter.Table("TacticalObjectives").AddColumn("Description").AsString(2000).Nullable();

				if (!Schema.Table("TacticalObjectives").Column("ProgressPercent").Exists())
					Alter.Table("TacticalObjectives").AddColumn("ProgressPercent").AsInt32().NotNullable().WithDefaultValue(0);

				if (!Schema.Table("TacticalObjectives").Column("Priority").Exists())
					Alter.Table("TacticalObjectives").AddColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0);

				if (!Schema.Table("TacticalObjectives").Column("TargetCompleteOn").Exists())
					Alter.Table("TacticalObjectives").AddColumn("TargetCompleteOn").AsDateTime2().Nullable();
			}

			if (Schema.Table("CommandStructureNodes").Exists())
			{
				foreach (var column in new[] { "PrimaryObjectiveId", "SecondaryObjectiveId", "LinkedNeedId" })
				{
					if (!Schema.Table("CommandStructureNodes").Column(column).Exists())
						Alter.Table("CommandStructureNodes").AddColumn(column).AsString(128).Nullable();
				}

				foreach (var column in new[] { "PrimaryLeadUserId", "SecondaryLeadUserId" })
				{
					if (!Schema.Table("CommandStructureNodes").Column(column).Exists())
						Alter.Table("CommandStructureNodes").AddColumn(column).AsString(450).Nullable();
				}

				foreach (var column in new[] { "PrimaryLeadName", "PrimaryLeadEmail", "SecondaryLeadName", "SecondaryLeadEmail" })
				{
					if (!Schema.Table("CommandStructureNodes").Column(column).Exists())
						Alter.Table("CommandStructureNodes").AddColumn(column).AsString(256).Nullable();
				}

				foreach (var column in new[] { "PrimaryLeadPhone", "SecondaryLeadPhone" })
				{
					if (!Schema.Table("CommandStructureNodes").Column(column).Exists())
						Alter.Table("CommandStructureNodes").AddColumn(column).AsString(64).Nullable();
				}
			}

			if (!Schema.Table("IncidentNeeds").Exists())
			{
				Create.Table("IncidentNeeds")
					.WithColumn("IncidentNeedId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(500).NotNullable()
					.WithColumn("Description").AsString(2000).Nullable()
					.WithColumn("Category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("QuantityRequested").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("QuantityFulfilled").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedByUserId").AsString(450).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("MetByUserId").AsString(450).Nullable()
					.WithColumn("MetOn").AsDateTime2().Nullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ModifiedOn").AsDateTime2().Nullable();

				Create.Index("IX_IncidentNeeds_Department_Call")
					.OnTable("IncidentNeeds")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("IncidentNeeds").Exists())
				Delete.Table("IncidentNeeds");

			if (Schema.Table("IncidentCommands").Exists())
			{
				foreach (var column in new[] { "EstimatedEndOn", "ImportantInformation" })
				{
					if (Schema.Table("IncidentCommands").Column(column).Exists())
						Delete.Column(column).FromTable("IncidentCommands");
				}
			}

			if (Schema.Table("TacticalObjectives").Exists())
			{
				foreach (var column in new[] { "Description", "ProgressPercent", "Priority", "TargetCompleteOn" })
				{
					if (Schema.Table("TacticalObjectives").Column(column).Exists())
						Delete.Column(column).FromTable("TacticalObjectives");
				}
			}

			if (Schema.Table("CommandStructureNodes").Exists())
			{
				foreach (var column in new[]
				{
					"PrimaryObjectiveId", "SecondaryObjectiveId", "LinkedNeedId",
					"PrimaryLeadUserId", "PrimaryLeadName", "PrimaryLeadPhone", "PrimaryLeadEmail",
					"SecondaryLeadUserId", "SecondaryLeadName", "SecondaryLeadPhone", "SecondaryLeadEmail"
				})
				{
					if (Schema.Table("CommandStructureNodes").Column(column).Exists())
						Delete.Column(column).FromTable("CommandStructureNodes");
				}
			}
		}
	}
}

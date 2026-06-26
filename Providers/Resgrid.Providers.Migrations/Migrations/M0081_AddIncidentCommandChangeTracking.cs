using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Offline-first sync foundation: adds a ModifiedOn change cursor to the mutable incident-command tables
	/// (delta "changed since" + last-write-wins) and a DeletedOn soft-delete tombstone to CommandStructureNodes
	/// so a lane removed offline propagates on delta sync. Append-only tables (CommandLogEntries / CommandTransfers)
	/// already carry a natural creation timestamp and are intentionally excluded.
	/// See docs/architecture/offline-first-architecture.md.
	/// </summary>
	[Migration(81)]
	public class M0081_AddIncidentCommandChangeTracking : Migration
	{
		private static readonly string[] ChangeTrackedTables =
		{
			"IncidentCommands",
			"CommandStructureNodes",
			"ResourceAssignments",
			"TacticalObjectives",
			"IncidentTimers",
			"IncidentMapAnnotations",
			"IncidentRoleAssignments"
		};

		public override void Up()
		{
			foreach (var table in ChangeTrackedTables)
			{
				if (Schema.Table(table).Exists() && !Schema.Table(table).Column("ModifiedOn").Exists())
				{
					Alter.Table(table).AddColumn("ModifiedOn").AsDateTime2().Nullable();
				}
			}

			if (Schema.Table("CommandStructureNodes").Exists() && !Schema.Table("CommandStructureNodes").Column("DeletedOn").Exists())
			{
				Alter.Table("CommandStructureNodes").AddColumn("DeletedOn").AsDateTime2().Nullable();
			}
		}

		public override void Down()
		{
			foreach (var table in ChangeTrackedTables)
			{
				if (Schema.Table(table).Exists() && Schema.Table(table).Column("ModifiedOn").Exists())
				{
					Delete.Column("ModifiedOn").FromTable(table);
				}
			}

			if (Schema.Table("CommandStructureNodes").Exists() && Schema.Table("CommandStructureNodes").Column("DeletedOn").Exists())
			{
				Delete.Column("DeletedOn").FromTable("CommandStructureNodes");
			}
		}
	}
}

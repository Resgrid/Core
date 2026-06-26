using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Offline-first sync foundation (PostgreSQL): adds a ModifiedOn change cursor to the mutable incident-command
	/// tables (delta "changed since" + last-write-wins) and a DeletedOn soft-delete tombstone to commandstructurenodes.
	/// Append-only tables already carry a natural creation timestamp and are intentionally excluded.
	/// See docs/architecture/offline-first-architecture.md.
	/// </summary>
	[Migration(81)]
	public class M0081_AddIncidentCommandChangeTrackingPg : Migration
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
				var t = table.ToLower();
				if (Schema.Table(t).Exists() && !Schema.Table(t).Column("ModifiedOn".ToLower()).Exists())
				{
					Alter.Table(t).AddColumn("ModifiedOn".ToLower()).AsDateTime2().Nullable();
				}
			}

			if (Schema.Table("CommandStructureNodes".ToLower()).Exists() && !Schema.Table("CommandStructureNodes".ToLower()).Column("DeletedOn".ToLower()).Exists())
			{
				Alter.Table("CommandStructureNodes".ToLower()).AddColumn("DeletedOn".ToLower()).AsDateTime2().Nullable();
			}
		}

		public override void Down()
		{
			foreach (var table in ChangeTrackedTables)
			{
				var t = table.ToLower();
				if (Schema.Table(t).Exists() && Schema.Table(t).Column("ModifiedOn".ToLower()).Exists())
				{
					Delete.Column("ModifiedOn".ToLower()).FromTable(t);
				}
			}

			if (Schema.Table("CommandStructureNodes".ToLower()).Exists() && Schema.Table("CommandStructureNodes".ToLower()).Column("DeletedOn".ToLower()).Exists())
			{
				Delete.Column("DeletedOn".ToLower()).FromTable("CommandStructureNodes".ToLower());
			}
		}
	}
}

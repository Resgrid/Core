using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Unit status timer acknowledgements: a dispatcher can mark a unit that has sat in a status past its
	/// threshold as seen, mute it, and leave a note. Each row is keyed to the UnitStates row that began the
	/// episode, so it cascades away with that state. One uncleared row per episode is enforced by a filtered
	/// unique index.
	/// </summary>
	[Migration(258)]
	public class M0258_AddUnitStatusAlertAcknowledgements : Migration
	{
		private const string Table = "UnitStatusAlertAcknowledgements";

		public override void Up()
		{
			if (!Schema.Table(Table).Exists())
			{
				Create.Table(Table)
					.WithColumn("UnitStatusAlertAcknowledgementId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UnitId").AsInt32().NotNullable()
					.WithColumn("UnitStateId").AsInt32().NotNullable()
					.WithColumn("Level").AsInt32().NotNullable()
					.WithColumn("Mode").AsInt32().NotNullable()
					.WithColumn("MutedUntil").AsDateTime2().Nullable()
					.WithColumn("Note").AsString(500).Nullable()
					.WithColumn("AcknowledgedByUserId").AsString(128).NotNullable()
					.WithColumn("AcknowledgedOn").AsDateTime2().NotNullable()
					.WithColumn("ClearedByUserId").AsString(128).Nullable()
					.WithColumn("ClearedOn").AsDateTime2().Nullable();

				// M0101 created UQ_Units_DepartmentId_UnitId, so the unit and the department always agree.
				Create.ForeignKey("FK_UnitStatusAlertAcknowledgements_Units_Department_Unit")
					.FromTable(Table).ForeignColumns("DepartmentId", "UnitId")
					.ToTable("Units").PrimaryColumns("DepartmentId", "UnitId");

				Create.ForeignKey("FK_UnitStatusAlertAcknowledgements_UnitStates")
					.FromTable(Table).ForeignColumn("UnitStateId")
					.ToTable("UnitStates").PrimaryColumn("UnitStateId")
					.OnDelete(Rule.Cascade);
			}

			if (!Schema.Table(Table).Index("IX_UnitStatusAlertAcknowledgements_Department_Cleared").Exists())
			{
				Create.Index("IX_UnitStatusAlertAcknowledgements_Department_Cleared")
					.OnTable(Table)
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("ClearedOn").Ascending();
			}

			Execute.Sql(@"
				IF NOT EXISTS (
					SELECT 1
					FROM sys.indexes
					WHERE name = 'UX_UnitStatusAlertAcknowledgements_ActiveEpisode'
					  AND object_id = OBJECT_ID(N'UnitStatusAlertAcknowledgements'))
				BEGIN
					CREATE UNIQUE NONCLUSTERED INDEX UX_UnitStatusAlertAcknowledgements_ActiveEpisode
					ON UnitStatusAlertAcknowledgements (UnitStateId)
					WHERE ClearedOn IS NULL;
				END");
		}

		public override void Down()
		{
			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}

using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Unit status timer acknowledgements: a dispatcher can mark a unit that has sat in a status past its
	/// threshold as seen, mute it, and leave a note. Each row is keyed to the unitstates row that began the
	/// episode, so it cascades away with that state. One uncleared row per episode is enforced by a partial
	/// unique index.
	/// </summary>
	[Migration(258)]
	public class M0258_AddUnitStatusAlertAcknowledgementsPg : Migration
	{
		private const string Table = "unitstatusalertacknowledgements";

		public override void Up()
		{
			if (!Schema.Table(Table).Exists())
			{
				Create.Table(Table)
					.WithColumn("unitstatusalertacknowledgementid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("unitid").AsInt32().NotNullable()
					.WithColumn("unitstateid").AsInt32().NotNullable()
					.WithColumn("level").AsInt32().NotNullable()
					.WithColumn("mode").AsInt32().NotNullable()
					.WithColumn("muteduntil").AsDateTime2().Nullable()
					.WithColumn("note").AsCustom("citext").Nullable()
					.WithColumn("acknowledgedbyuserid").AsCustom("citext").NotNullable()
					.WithColumn("acknowledgedon").AsDateTime2().NotNullable()
					.WithColumn("clearedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("clearedon").AsDateTime2().Nullable();

				// M0101 created uq_units_departmentid_unitid, so the unit and the department always agree.
				Create.ForeignKey("fk_unitstatusalertacknowledgements_units_department_unit")
					.FromTable(Table).ForeignColumns("departmentid", "unitid")
					.ToTable("units").PrimaryColumns("departmentid", "unitid");

				Create.ForeignKey("fk_unitstatusalertacknowledgements_unitstates")
					.FromTable(Table).ForeignColumn("unitstateid")
					.ToTable("unitstates").PrimaryColumn("unitstateid")
					.OnDelete(Rule.Cascade);
			}

			if (!Schema.Table(Table).Index("ix_unitstatusalertacknowledgements_department_cleared").Exists())
			{
				Create.Index("ix_unitstatusalertacknowledgements_department_cleared")
					.OnTable(Table)
					.OnColumn("departmentid").Ascending()
					.OnColumn("clearedon").Ascending();
			}

			Execute.Sql(@"
				CREATE UNIQUE INDEX IF NOT EXISTS ux_unitstatusalertacknowledgements_activeepisode
				ON unitstatusalertacknowledgements (unitstateid)
				WHERE clearedon IS NULL;");
		}

		public override void Down()
		{
			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}

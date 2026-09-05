using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0170 (registry M0170, RMS-3): rmsrecordduestates and rmsrecordlegalholds. Lowercase
	/// unquoted identifiers, citext for text, unique index on (department, record, obligation) so the
	/// emit-once-per-transition guarantee is enforced by the database and not by the worker's memory.
	/// </summary>
	[Migration(170)]
	public class M0170_AddRmsDueStateAndRetentionPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsrecordduestates").Exists())
			{
				Create.Table("rmsrecordduestates")
					.WithColumn("rmsrecordduestateid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("recordkind").AsInt32().NotNullable()
					.WithColumn("obligation").AsInt32().NotNullable()
					.WithColumn("dueon").AsDateTime2().Nullable()
					.WithColumn("lastemittedstate").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("lastemittedon").AsDateTime2().Nullable()
					.WithColumn("responsibleuserid").AsString(128).Nullable()
					.WithColumn("overduecount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsrecordduestates_record_obligation ON rmsrecordduestates (departmentid, recordid, obligation);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordduestates_department_due ON rmsrecordduestates (departmentid, dueon);");
			}

			if (!Schema.Table("rmsrecordlegalholds").Exists())
			{
				Create.Table("rmsrecordlegalholds")
					.WithColumn("rmsrecordlegalholdid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("recordid").AsString(36).Nullable()
					.WithColumn("definitionkey").AsCustom("citext").Nullable()
					.WithColumn("periodstart").AsDateTime2().Nullable()
					.WithColumn("periodend").AsDateTime2().Nullable()
					.WithColumn("reason").AsCustom("citext").Nullable()
					.WithColumn("referencenumber").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("placedbyuserid").AsString(128).Nullable()
					.WithColumn("placedon").AsDateTime2().NotNullable()
					.WithColumn("releasedbyuserid").AsString(128).Nullable()
					.WithColumn("releasedon").AsDateTime2().Nullable()
					.WithColumn("releasenotes").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordlegalholds_department_released ON rmsrecordlegalholds (departmentid, releasedon);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordlegalholds_department_record ON rmsrecordlegalholds (departmentid, recordid);");
			}

			// A purged Record keeps its row and loses only its content (plan 4.9); the tombstone needs to say so.
			if (Schema.Table("rmsoperationalrecords").Exists() && !Schema.Table("rmsoperationalrecords").Column("purgedon").Exists())
				Alter.Table("rmsoperationalrecords").AddColumn("purgedon").AsDateTime2().Nullable();

			if (Schema.Table("rmsincidentreports").Exists() && !Schema.Table("rmsincidentreports").Column("purgedon").Exists())
				Alter.Table("rmsincidentreports").AddColumn("purgedon").AsDateTime2().Nullable();
		}

		public override void Down()
		{
			foreach (var table in new[] { "rmsrecordlegalholds", "rmsrecordduestates" })
			{
				if (Schema.Table(table).Exists())
					Delete.Table(table);
			}
		}
	}
}

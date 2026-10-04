using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Call location history: a lookup row per call holding its parsed address key and coordinates, so previous calls at
	/// a location are found however the address was typed ("110 S Main St" / "110 South Main"). Rows cascade with their
	/// call. A per-department state row drives the backfill (worker 73) and records Advanced Data Protection
	/// suppression, since the keys are derived from protected call fields. Also indexes callcontacts, which had none,
	/// for the contact side of the same history.
	/// </summary>
	[Migration(259)]
	public class M0259_AddCallLocationIndexPg : Migration
	{
		private const string Keys = "calllocationkeys";
		private const string States = "calllocationindexstates";

		public override void Up()
		{
			if (!Schema.Table(Keys).Exists())
			{
				Create.Table(Keys)
					.WithColumn("callid").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("addresskey").AsString(120).Nullable()
					.WithColumn("addresscanonical").AsString(500).Nullable()
					.WithColumn("latitude").AsDecimal(9, 6).Nullable()
					.WithColumn("longitude").AsDecimal(9, 6).Nullable()
					.WithColumn("loggedon").AsDateTime2().NotNullable()
					.WithColumn("keyversion").AsInt32().NotNullable()
					.WithColumn("indexedon").AsDateTime2().NotNullable();

				Create.ForeignKey("fk_calllocationkeys_calls")
					.FromTable(Keys).ForeignColumn("callid")
					.ToTable("calls").PrimaryColumn("callid")
					.OnDelete(Rule.Cascade);
			}

			if (!Schema.Table(Keys).Index("ix_calllocationkeys_department_addresskey").Exists())
			{
				Create.Index("ix_calllocationkeys_department_addresskey")
					.OnTable(Keys)
					.OnColumn("departmentid").Ascending()
					.OnColumn("addresskey").Ascending()
					.OnColumn("loggedon").Descending();
			}

			if (!Schema.Table(Keys).Index("ix_calllocationkeys_department_coordinates").Exists())
			{
				Create.Index("ix_calllocationkeys_department_coordinates")
					.OnTable(Keys)
					.OnColumn("departmentid").Ascending()
					.OnColumn("latitude").Ascending()
					.OnColumn("longitude").Ascending();
			}

			if (!Schema.Table(States).Exists())
			{
				Create.Table(States)
					.WithColumn("departmentid").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("keyversion").AsInt32().NotNullable()
					.WithColumn("nextcallid").AsInt32().Nullable()
					.WithColumn("completedon").AsDateTime2().Nullable()
					.WithColumn("issuppressed").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("modifiedon").AsDateTime2().NotNullable();
			}

			Execute.Sql(@"
				CREATE INDEX IF NOT EXISTS ix_callcontacts_departmentid_contactid ON callcontacts (departmentid, contactid);
				CREATE INDEX IF NOT EXISTS ix_callcontacts_callid ON callcontacts (callid);");
		}

		public override void Down()
		{
			Execute.Sql(@"
				DROP INDEX IF EXISTS ix_callcontacts_callid;
				DROP INDEX IF EXISTS ix_callcontacts_departmentid_contactid;");

			if (Schema.Table(States).Exists())
				Delete.Table(States);

			if (Schema.Table(Keys).Exists())
				Delete.Table(Keys);
		}
	}
}

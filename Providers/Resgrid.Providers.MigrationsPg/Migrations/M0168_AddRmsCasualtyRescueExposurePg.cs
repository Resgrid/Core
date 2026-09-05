using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0168 (registry M0168, RMS-3): rmscasualtyrescues and rmsexposures. Lowercase unquoted
	/// identifiers, citext for text. Both are restricted classes with the inert Protected Data envelope and a
	/// permanent retention default.
	/// </summary>
	[Migration(168)]
	public class M0168_AddRmsCasualtyRescueExposurePg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmscasualtyrescues").Exists())
			{
				Create.Table("rmscasualtyrescues")
					.WithColumn("rmscasualtyrescueid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("kind").AsInt32().NotNullable()
					.WithColumn("persontype").AsCustom("citext").NotNullable()
					.WithColumn("personneluserid").AsString(128).Nullable()
					.WithColumn("rank").AsCustom("citext").Nullable()
					.WithColumn("yearsofservice").AsDecimal(6, 2).Nullable()
					.WithColumn("jobclassification").AsCustom("citext").Nullable()
					.WithColumn("birthmonthyear").AsCustom("citext").Nullable()
					.WithColumn("gender").AsCustom("citext").Nullable()
					.WithColumn("race").AsCustom("citext").Nullable()
					.WithColumn("wasinjured").AsBoolean().Nullable()
					.WithColumn("casualtycause").AsCustom("citext").Nullable()
					.WithColumn("casualtyaction").AsCustom("citext").Nullable()
					.WithColumn("casualtytimeline").AsCustom("citext").Nullable()
					.WithColumn("dutytype").AsCustom("citext").Nullable()
					.WithColumn("ppecsv").AsCustom("citext").Nullable()
					.WithColumn("injurydetailjson").AsCustom("citext").Nullable()
					.WithColumn("wasfatal").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rescuetype").AsCustom("citext").Nullable()
					.WithColumn("rescueactionscsv").AsCustom("citext").Nullable()
					.WithColumn("rescueimpedimentscsv").AsCustom("citext").Nullable()
					.WithColumn("rescuemode").AsCustom("citext").Nullable()
					.WithColumn("rescuepath").AsCustom("citext").Nullable()
					.WithColumn("rescueelevation").AsCustom("citext").Nullable()
					.WithColumn("presenceknown").AsCustom("citext").Nullable()
					.WithColumn("detailjson").AsCustom("citext").Nullable()
					.WithColumn("occurredon").AsDateTime2().Nullable()
					.WithColumn("protectedenvelope").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmscasualtyrescues_department_record_revision ON rmscasualtyrescues (departmentid, recordid, revisionid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmscasualtyrescues_department_personnel ON rmscasualtyrescues (departmentid, personneluserid);");
			}

			if (!Schema.Table("rmsexposures").Exists())
			{
				Create.Table("rmsexposures")
					.WithColumn("rmsexposureid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("locationkind").AsCustom("citext").Nullable()
					.WithColumn("itemtype").AsCustom("citext").Nullable()
					.WithColumn("damagetype").AsCustom("citext").Nullable()
					.WithColumn("locationuse").AsCustom("citext").Nullable()
					.WithColumn("peoplepresent").AsBoolean().Nullable()
					.WithColumn("displacementcount").AsInt32().Nullable()
					.WithColumn("displacementcausescsv").AsCustom("citext").Nullable()
					.WithColumn("addresstext").AsCustom("citext").Nullable()
					.WithColumn("street").AsCustom("citext").Nullable()
					.WithColumn("municipality").AsCustom("citext").Nullable()
					.WithColumn("state").AsCustom("citext").Nullable()
					.WithColumn("postalcode").AsCustom("citext").Nullable()
					.WithColumn("latitude").AsDecimal(12, 8).Nullable()
					.WithColumn("longitude").AsDecimal(12, 8).Nullable()
					.WithColumn("estimatedvalue").AsDecimal(18, 2).Nullable()
					.WithColumn("estimatedloss").AsDecimal(18, 2).Nullable()
					.WithColumn("currencycode").AsCustom("citext").Nullable()
					.WithColumn("detailjson").AsCustom("citext").Nullable()
					.WithColumn("protectedenvelope").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsexposures_department_record_revision ON rmsexposures (departmentid, recordid, revisionid);");
			}
		}

		public override void Down()
		{
			foreach (var table in new[] { "rmsexposures", "rmscasualtyrescues" })
			{
				if (Schema.Table(table).Exists())
					Delete.Table(table);
			}
		}
	}
}

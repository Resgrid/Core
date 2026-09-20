using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0215 (Workforce &amp; Business Operations plan, Phase C). Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(215)]
	public class M0215_AddRateSchedulesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rateschedules").Exists())
			{
				Create.Table("rateschedules")
					.WithColumn("ratescheduleid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsString(200).NotNullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("currency").AsString(3).NotNullable().WithDefaultValue("USD")
					.WithColumn("effectiveon").AsDateTime2().Nullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("policyjson").AsCustom("text").Nullable()
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rateschedules_department ON rateschedules (departmentid, isdeleted);");
			}
			if (!Schema.Table("ratescheduleentries").Exists())
			{
				Create.Table("ratescheduleentries")
					.WithColumn("ratescheduleentryid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("ratescheduleid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("entrytype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("name").AsString(200).NotNullable()
					.WithColumn("code").AsString(50).Nullable()
					.WithColumn("groupkey").AsString(50).Nullable()
					.WithColumn("crewsize").AsInt32().Nullable()
					.WithColumn("certificationcode").AsString(50).Nullable()
					.WithColumn("unittypeid").AsInt32().Nullable()
					.WithColumn("inventoryitemid").AsString(36).Nullable()
					.WithColumn("inventorycategoryid").AsString(36).Nullable()
					.WithColumn("billingbasis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("requiredcertificationsjson").AsCustom("text").Nullable()
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_ratescheduleentries_schedule ON ratescheduleentries (ratescheduleid, isdeleted, sortorder);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_ratescheduleentries_group ON ratescheduleentries (ratescheduleid, groupkey);");
				Create.ForeignKey("fk_ratescheduleentries_schedule").FromTable("ratescheduleentries").ForeignColumn("ratescheduleid").ToTable("rateschedules").PrimaryColumn("ratescheduleid");
			}
			if (!Schema.Table("ratescheduleentrybands").Exists())
			{
				Create.Table("ratescheduleentrybands")
					.WithColumn("ratescheduleentrybandid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("ratescheduleentryid").AsString(36).NotNullable()
					.WithColumn("bandtype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("rate").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("thresholdstarthours").AsDecimal(9,2).Nullable()
					.WithColumn("thresholdendhours").AsDecimal(9,2).Nullable()
					.WithColumn("dailytierminhours").AsDecimal(9,2).Nullable()
					.WithColumn("dailytiermaxhours").AsDecimal(9,2).Nullable()
					.WithColumn("freeunitsperday").AsDecimal(9,2).Nullable()
					.WithColumn("requiresairtravel").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("mealcode").AsString(10).Nullable()
					.WithColumn("label").AsString(200).Nullable()
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0);
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_ratescheduleentrybands_entry ON ratescheduleentrybands (ratescheduleentryid, sortorder);");
				Create.ForeignKey("fk_ratescheduleentrybands_entry").FromTable("ratescheduleentrybands").ForeignColumn("ratescheduleentryid").ToTable("ratescheduleentries").PrimaryColumn("ratescheduleentryid");
			}
			if (!Schema.Table("ratepremiums").Exists())
			{
				Create.Table("ratepremiums")
					.WithColumn("ratepremiumid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("ratescheduleid").AsString(36).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsString(200).NotNullable()
					.WithColumn("code").AsString(50).Nullable()
					.WithColumn("standbyadder").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("deploymentadder").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("overtime1adder").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("overtime2adder").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsString(128).Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_ratepremiums_schedule ON ratepremiums (ratescheduleid, isdeleted);");
				Create.ForeignKey("fk_ratepremiums_schedule").FromTable("ratepremiums").ForeignColumn("ratescheduleid").ToTable("rateschedules").PrimaryColumn("ratescheduleid");
			}
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS ratepremiums;");
			Execute.Sql("DROP TABLE IF EXISTS ratescheduleentrybands;");
			Execute.Sql("DROP TABLE IF EXISTS ratescheduleentries;");
			Execute.Sql("DROP TABLE IF EXISTS rateschedules;");
		}
	}
}

using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(56)]
	public class M0056_AddingCheckInTimersPg : Migration
	{
		public override void Up()
		{
			// ── checkIntimerconfigs ─────────────────────────────────
			Create.Table("checkintimerconfigs")
				.WithColumn("checkintimerconfigid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("timertargettype").AsInt32().NotNullable()
				.WithColumn("unittypeid").AsInt32().Nullable()
				.WithColumn("durationminutes").AsInt32().NotNullable()
				.WithColumn("warningthresholdminutes").AsInt32().NotNullable()
				.WithColumn("isenabled").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("createdbyuserid").AsCustom("citext").NotNullable()
				.WithColumn("createdon").AsDateTime().NotNullable()
				.WithColumn("updatedon").AsDateTime().Nullable();

			Create.ForeignKey("fk_checkintimerconfigs_departments")
				.FromTable("checkintimerconfigs").ForeignColumn("departmentid")
				.ToTable("departments").PrimaryColumn("departmentid");

			Create.Index("ix_checkintimerconfigs_departmentid")
				.OnTable("checkintimerconfigs")
				.OnColumn("departmentid");

			Create.UniqueConstraint("uq_checkintimerconfigs_dept_target_unit")
				.OnTable("checkintimerconfigs")
				.Columns("departmentid", "timertargettype", "unittypeid");

			// ── checkintimeroverrides ──────────────────────────────
			Create.Table("checkintimeroverrides")
				.WithColumn("checkintimeroverrideid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("calltypeid").AsInt32().Nullable()
				.WithColumn("callpriority").AsInt32().Nullable()
				.WithColumn("timertargettype").AsInt32().NotNullable()
				.WithColumn("unittypeid").AsInt32().Nullable()
				.WithColumn("durationminutes").AsInt32().NotNullable()
				.WithColumn("warningthresholdminutes").AsInt32().NotNullable()
				.WithColumn("isenabled").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("createdbyuserid").AsCustom("citext").NotNullable()
				.WithColumn("createdon").AsDateTime().NotNullable()
				.WithColumn("updatedon").AsDateTime().Nullable();

			Create.ForeignKey("fk_checkintimeroverrides_departments")
				.FromTable("checkintimeroverrides").ForeignColumn("departmentid")
				.ToTable("departments").PrimaryColumn("departmentid");

			Create.Index("ix_checkintimeroverrides_departmentid")
				.OnTable("checkintimeroverrides")
				.OnColumn("departmentid");

			Create.UniqueConstraint("uq_checkintimeroverrides_dept_call_target_unit")
				.OnTable("checkintimeroverrides")
				.Columns("departmentid", "calltypeid", "callpriority", "timertargettype", "unittypeid");

			// ── checkinrecords ─────────────────────────────────────
			Create.Table("checkinrecords")
				.WithColumn("checkinrecordid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("callid").AsInt32().NotNullable()
				.WithColumn("checkintype").AsInt32().NotNullable()
				.WithColumn("userid").AsCustom("citext").NotNullable()
				.WithColumn("unitid").AsInt32().Nullable()
				.WithColumn("latitude").AsCustom("citext").Nullable()
				.WithColumn("longitude").AsCustom("citext").Nullable()
				.WithColumn("timestamp").AsDateTime().NotNullable()
				.WithColumn("note").AsCustom("citext").Nullable();

			Create.ForeignKey("fk_checkinrecords_departments")
				.FromTable("checkinrecords").ForeignColumn("departmentid")
				.ToTable("departments").PrimaryColumn("departmentid");

			Create.ForeignKey("fk_checkinrecords_calls")
				.FromTable("checkinrecords").ForeignColumn("callid")
				.ToTable("calls").PrimaryColumn("callid");

			Create.Index("ix_checkinrecords_callid")
				.OnTable("checkinrecords")
				.OnColumn("callid");

			Create.Index("ix_checkinrecords_departmentid_timestamp")
				.OnTable("checkinrecords")
				.OnColumn("departmentid").Ascending()
				.OnColumn("timestamp").Descending();

			// ── Alter calls ────────────────────────────────────────
			Alter.Table("calls")
				.AddColumn("checkintimersenabled").AsBoolean().NotNullable().WithDefaultValue(false);

			// ── Alter callquicktemplates ────────────────────────────
			Alter.Table("callquicktemplates")
				.AddColumn("checkintimersenabled").AsBoolean().Nullable();
		}

		public override void Down()
		{
			Delete.Column("checkintimersenabled").FromTable("callquicktemplates");
			Delete.Column("checkintimersenabled").FromTable("calls");

			Delete.ForeignKey("fk_checkinrecords_calls").OnTable("checkinrecords");
			Delete.ForeignKey("fk_checkinrecords_departments").OnTable("checkinrecords");
			Delete.Table("checkinrecords");

			Delete.ForeignKey("fk_checkintimeroverrides_departments").OnTable("checkintimeroverrides");
			Delete.Table("checkintimeroverrides");

			Delete.ForeignKey("fk_checkintimerconfigs_departments").OnTable("checkintimerconfigs");
			Delete.Table("checkintimerconfigs");
		}
	}
}

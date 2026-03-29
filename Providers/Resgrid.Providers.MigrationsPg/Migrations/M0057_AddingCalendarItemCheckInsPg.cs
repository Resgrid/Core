using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(57)]
	public class M0057_AddingCalendarItemCheckInsPg : Migration
	{
		public override void Up()
		{
			Create.Table("calendaritemcheckins")
				.WithColumn("calendaritemcheckinid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("calendaritemid").AsInt32().NotNullable()
				.WithColumn("userid").AsCustom("citext").NotNullable()
				.WithColumn("checkintime").AsDateTime().NotNullable()
				.WithColumn("checkouttime").AsDateTime().Nullable()
				.WithColumn("checkinbyuserid").AsCustom("citext").Nullable()
				.WithColumn("checkoutbyuserid").AsCustom("citext").Nullable()
				.WithColumn("ismanualoverride").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("checkinnote").AsCustom("citext").Nullable()
				.WithColumn("checkoutnote").AsCustom("citext").Nullable()
				.WithColumn("checkinlatitude").AsCustom("citext").Nullable()
				.WithColumn("checkinlongitude").AsCustom("citext").Nullable()
				.WithColumn("checkoutlatitude").AsCustom("citext").Nullable()
				.WithColumn("checkoutlongitude").AsCustom("citext").Nullable()
				.WithColumn("timestamp").AsDateTime().NotNullable();

			Create.ForeignKey("fk_calendaritemcheckins_departments")
				.FromTable("calendaritemcheckins").ForeignColumn("departmentid")
				.ToTable("departments").PrimaryColumn("departmentid");

			Create.ForeignKey("fk_calendaritemcheckins_calendaritems")
				.FromTable("calendaritemcheckins").ForeignColumn("calendaritemid")
				.ToTable("calendaritems").PrimaryColumn("calendaritemid");

			Create.Index("ix_calendaritemcheckins_calendaritemid")
				.OnTable("calendaritemcheckins")
				.OnColumn("calendaritemid");

			Create.Index("ix_calendaritemcheckins_departmentid_userid")
				.OnTable("calendaritemcheckins")
				.OnColumn("departmentid").Ascending()
				.OnColumn("userid").Ascending();

			Create.UniqueConstraint("uq_calendaritemcheckins_calitem_user")
				.OnTable("calendaritemcheckins")
				.Columns("calendaritemid", "userid");

			// Add checkintype to calendaritems table
			Alter.Table("calendaritems")
				.AddColumn("checkintype").AsInt32().NotNullable().WithDefaultValue(0);
		}

		public override void Down()
		{
			Delete.Column("checkintype").FromTable("calendaritems");

			Delete.ForeignKey("fk_calendaritemcheckins_calendaritems").OnTable("calendaritemcheckins");
			Delete.ForeignKey("fk_calendaritemcheckins_departments").OnTable("calendaritemcheckins");
			Delete.Table("calendaritemcheckins");
		}
	}
}

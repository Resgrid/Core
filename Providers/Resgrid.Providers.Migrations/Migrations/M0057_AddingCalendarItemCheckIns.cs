using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(57)]
	public class M0057_AddingCalendarItemCheckIns : Migration
	{
		public override void Up()
		{
			Create.Table("CalendarItemCheckIns")
				.WithColumn("CalendarItemCheckInId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("CalendarItemId").AsInt32().NotNullable()
				.WithColumn("UserId").AsString(128).NotNullable()
				.WithColumn("CheckInTime").AsDateTime2().NotNullable()
				.WithColumn("CheckOutTime").AsDateTime2().Nullable()
				.WithColumn("CheckInByUserId").AsString(128).Nullable()
				.WithColumn("CheckOutByUserId").AsString(128).Nullable()
				.WithColumn("IsManualOverride").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("CheckInNote").AsString(1000).Nullable()
				.WithColumn("CheckOutNote").AsString(1000).Nullable()
				.WithColumn("CheckInLatitude").AsString(50).Nullable()
				.WithColumn("CheckInLongitude").AsString(50).Nullable()
				.WithColumn("CheckOutLatitude").AsString(50).Nullable()
				.WithColumn("CheckOutLongitude").AsString(50).Nullable()
				.WithColumn("Timestamp").AsDateTime2().NotNullable();

			Create.ForeignKey("FK_CalendarItemCheckIns_Departments")
				.FromTable("CalendarItemCheckIns").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.ForeignKey("FK_CalendarItemCheckIns_CalendarItems")
				.FromTable("CalendarItemCheckIns").ForeignColumn("CalendarItemId")
				.ToTable("CalendarItems").PrimaryColumn("CalendarItemId");

			Create.Index("IX_CalendarItemCheckIns_CalendarItemId")
				.OnTable("CalendarItemCheckIns")
				.OnColumn("CalendarItemId");

			Create.Index("IX_CalendarItemCheckIns_DepartmentId_UserId")
				.OnTable("CalendarItemCheckIns")
				.OnColumn("DepartmentId").Ascending()
				.OnColumn("UserId").Ascending();

			Create.UniqueConstraint("UQ_CalendarItemCheckIns_CalItem_User")
				.OnTable("CalendarItemCheckIns")
				.Columns("CalendarItemId", "UserId");

			// Add CheckInType to CalendarItems table
			Alter.Table("CalendarItems")
				.AddColumn("CheckInType").AsInt32().NotNullable().WithDefaultValue(0);
		}

		public override void Down()
		{
			Delete.Column("CheckInType").FromTable("CalendarItems");

			Delete.ForeignKey("FK_CalendarItemCheckIns_CalendarItems").OnTable("CalendarItemCheckIns");
			Delete.ForeignKey("FK_CalendarItemCheckIns_Departments").OnTable("CalendarItemCheckIns");
			Delete.Table("CalendarItemCheckIns");
		}
	}
}

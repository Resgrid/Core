using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// On-shift hours and static shift staffing. WorkshiftFills.FilledById was created as int (M0014) but holds the user id of
	/// the person crewing a unit on a static shift day, so it becomes nvarchar(128); nothing ever wrote the table, so no rows
	/// need converting. ShiftStaffings gets a department/day index for the report's date-range read.
	/// </summary>
	[Migration(305)]
	public class M0305_AddShiftStaffingHours : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('WorkshiftFills') AND name = 'FilledById' AND system_type_id = TYPE_ID('int'))
	ALTER TABLE [WorkshiftFills] ALTER COLUMN [FilledById] nvarchar(128) NOT NULL;");

			if (!Schema.Table("ShiftStaffings").Index("IX_ShiftStaffings_Department_ShiftDay").Exists())
				Create.Index("IX_ShiftStaffings_Department_ShiftDay").OnTable("ShiftStaffings").OnColumn("DepartmentId").Ascending().OnColumn("ShiftDay").Ascending();

			if (!Schema.Table("WorkshiftFills").Index("IX_WorkshiftFills_WorkshiftDayId").Exists())
				Create.Index("IX_WorkshiftFills_WorkshiftDayId").OnTable("WorkshiftFills").OnColumn("WorkshiftDayId").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("WorkshiftFills").Index("IX_WorkshiftFills_WorkshiftDayId").Exists())
				Delete.Index("IX_WorkshiftFills_WorkshiftDayId").OnTable("WorkshiftFills");

			if (Schema.Table("ShiftStaffings").Index("IX_ShiftStaffings_Department_ShiftDay").Exists())
				Delete.Index("IX_ShiftStaffings_Department_ShiftDay").OnTable("ShiftStaffings");
		}
	}
}

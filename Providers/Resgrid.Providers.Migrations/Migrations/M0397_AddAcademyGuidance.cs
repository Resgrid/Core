using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// A2/A3 (protocols-training-knowledge-plan §15): AcademyGuidanceStates remembers each member's guided tours and "What's new" lessons
	/// (dismissed, completed, tasks done; unique per department, member and key; no foreign keys, purged by DepartmentId in
	/// UnlinkedDepartmentCleanup). AcademyLessonUpdates records a major Academy lesson revision for worker 88 to apply its notify / re-assign
	/// policy department by department (system-level, unique per course and revision). Existence-guarded.
	/// </summary>
	[Migration(397)]
	public class M0397_AddAcademyGuidance : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("AcademyGuidanceStates").Exists())
			{
				Create.Table("AcademyGuidanceStates")
					.WithColumn("AcademyGuidanceStateId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("GuidanceKey").AsString(100).NotNullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Revision").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("TasksDone").AsString(1000).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable();
			}

			if (!Schema.Table("AcademyGuidanceStates").Index("UX_AcademyGuidanceStates_Member_Key").Exists())
				Create.Index("UX_AcademyGuidanceStates_Member_Key").OnTable("AcademyGuidanceStates")
					.OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().OnColumn("GuidanceKey").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("AcademyLessonUpdates").Exists())
			{
				Create.Table("AcademyLessonUpdates")
					.WithColumn("AcademyLessonUpdateId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("SystemKey").AsString(100).NotNullable()
					.WithColumn("LmsCourseId").AsInt32().NotNullable()
					.WithColumn("LmsCourseVersionId").AsInt32().NotNullable()
					.WithColumn("Revision").AsInt32().NotNullable()
					.WithColumn("Policy").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ReassignDueDays").AsInt32().Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("AppliedThroughDepartmentId").AsInt32().Nullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable();
			}

			if (!Schema.Table("AcademyLessonUpdates").Index("UX_AcademyLessonUpdates_Course_Revision").Exists())
				Create.Index("UX_AcademyLessonUpdates_Course_Revision").OnTable("AcademyLessonUpdates")
					.OnColumn("LmsCourseId").Ascending().OnColumn("Revision").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("AcademyLessonUpdates").Index("IX_AcademyLessonUpdates_Pending").Exists())
				Create.Index("IX_AcademyLessonUpdates_Pending").OnTable("AcademyLessonUpdates")
					.OnColumn("CompletedOn").Ascending().OnColumn("AcademyLessonUpdateId").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("AcademyLessonUpdates").Exists())
				Delete.Table("AcademyLessonUpdates");
			if (Schema.Table("AcademyGuidanceStates").Exists())
				Delete.Table("AcademyGuidanceStates");
		}
	}
}

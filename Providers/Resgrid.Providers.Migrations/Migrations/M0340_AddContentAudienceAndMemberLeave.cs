using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Shared, neutrally named tables Training T0 needs and later phases reuse:
	/// - ContentAudienceChanges: the membership-change queue (group, role, shift, certification, unit crew, joining) that
	///   worker 83 drains to re-evaluate dynamic audiences without sweeping every department. Protocols P1 audiences use it too.
	/// - MemberLeavePeriods (R-C2): an officer-entered leave of absence that pauses training due dates and reminders and
	///   shifts them on return. T3 learning requirements reuse it.
	/// - MemberPinnedItems: what a member pinned to My Training (certifications first).
	/// No foreign keys: UnlinkedDepartmentCleanup removes the rows with the department. Existence-guarded for safe retry.
	/// </summary>
	[Migration(340)]
	public class M0340_AddContentAudienceAndMemberLeave : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ContentAudienceChanges").Exists())
			{
				Create.Table("ContentAudienceChanges")
					.WithColumn("ContentAudienceChangeId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).Nullable()
					.WithColumn("Source").AsInt32().NotNullable()
					.WithColumn("QueuedOn").AsDateTime2().NotNullable()
					.WithColumn("ProcessedOn").AsDateTime2().Nullable();

				Create.Index("IX_ContentAudienceChanges_ProcessedOn").OnTable("ContentAudienceChanges")
					.OnColumn("ProcessedOn").Ascending().OnColumn("ContentAudienceChangeId").Ascending();
				Create.Index("IX_ContentAudienceChanges_DepartmentId").OnTable("ContentAudienceChanges")
					.OnColumn("DepartmentId").Ascending();
			}

			if (!Schema.Table("MemberLeavePeriods").Exists())
			{
				Create.Table("MemberLeavePeriods")
					.WithColumn("MemberLeavePeriodId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("StartsOn").AsDateTime2().NotNullable()
					.WithColumn("EndsOn").AsDateTime2().NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Note").AsString(500).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable();

				Create.Index("IX_MemberLeavePeriods_Department_User").OnTable("MemberLeavePeriods")
					.OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending();
			}

			if (!Schema.Table("MemberPinnedItems").Exists())
			{
				Create.Table("MemberPinnedItems")
					.WithColumn("MemberPinnedItemId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable()
					.WithColumn("RefId").AsString(128).NotNullable()
					.WithColumn("PinnedOn").AsDateTime2().NotNullable();

				Create.Index("UX_MemberPinnedItems_Department_User_Kind_Ref").OnTable("MemberPinnedItems")
					.OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().OnColumn("Kind").Ascending().OnColumn("RefId").Ascending()
					.WithOptions().Unique();
			}
		}

		public override void Down()
		{
			if (Schema.Table("MemberPinnedItems").Exists())
				Delete.Table("MemberPinnedItems");
			if (Schema.Table("MemberLeavePeriods").Exists())
				Delete.Table("MemberLeavePeriods");
			if (Schema.Table("ContentAudienceChanges").Exists())
				Delete.Table("ContentAudienceChanges");
		}
	}
}

using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 6F (protocols-training-knowledge-plan §13 T5 items 4 and 5): LmsCourseOrigins (where a department course came from: an Enhanced AI
	/// draft from a protocol version or an uploaded SOP, with each lesson's citations, or a Resgrid starter clone with its attribution — source,
	/// licence, revision; one row per course) and LmsFlashcards (a course's accepted practice cards, each with its source version and anchor,
	/// flagged for review when that source is superseded). LMS tables carry DepartmentId and no foreign keys (§17.3); LmsDepartmentCleanup
	/// clears them. Existence-guarded.
	/// </summary>
	[Migration(398)]
	public class M0398_AddLmsCourseStudio : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("LmsCourseOrigins").Exists())
			{
				Create.Table("LmsCourseOrigins")
					.WithColumn("LmsCourseOriginId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("LmsCourseId").AsInt32().NotNullable()
					.WithColumn("LmsCourseVersionId").AsInt32().NotNullable()
					.WithColumn("OriginKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceKey").AsString(200).Nullable()
					.WithColumn("SourceName").AsString(250).Nullable()
					.WithColumn("SourceVersion").AsString(50).Nullable()
					.WithColumn("SourceUrl").AsString(500).Nullable()
					.WithColumn("License").AsString(100).Nullable()
					.WithColumn("Attribution").AsString(1000).Nullable()
					.WithColumn("ExchangeId").AsString(64).Nullable()
					.WithColumn("CitationsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable();
			}

			if (!Schema.Table("LmsCourseOrigins").Index("UX_LmsCourseOrigins_Course").Exists())
				Create.Index("UX_LmsCourseOrigins_Course").OnTable("LmsCourseOrigins")
					.OnColumn("DepartmentId").Ascending().OnColumn("LmsCourseId").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("LmsFlashcards").Exists())
			{
				Create.Table("LmsFlashcards")
					.WithColumn("LmsFlashcardId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("LmsCourseId").AsInt32().NotNullable()
					.WithColumn("SourceKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceItemId").AsInt32().NotNullable()
					.WithColumn("SourceVersionId").AsInt32().NotNullable()
					.WithColumn("SourceVersionLabel").AsString(50).Nullable()
					.WithColumn("SourceAnchor").AsString(128).Nullable()
					.WithColumn("SourceHeading").AsString(500).Nullable()
					.WithColumn("SourceTitle").AsString(250).Nullable()
					.WithColumn("Front").AsString(300).NotNullable()
					.WithColumn("Back").AsString(600).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("FlagReason").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("FlaggedOn").AsDateTime2().Nullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ExchangeId").AsString(64).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ReviewedOn").AsDateTime2().Nullable()
					.WithColumn("ReviewedByUserId").AsString(128).Nullable();
			}

			if (!Schema.Table("LmsFlashcards").Index("IX_LmsFlashcards_Course").Exists())
				Create.Index("IX_LmsFlashcards_Course").OnTable("LmsFlashcards")
					.OnColumn("DepartmentId").Ascending().OnColumn("LmsCourseId").Ascending();

			// The supersede hook's one update: every card of the department drafted from a source item.
			if (!Schema.Table("LmsFlashcards").Index("IX_LmsFlashcards_Source").Exists())
				Create.Index("IX_LmsFlashcards_Source").OnTable("LmsFlashcards")
					.OnColumn("DepartmentId").Ascending().OnColumn("SourceKind").Ascending().OnColumn("SourceItemId").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("LmsFlashcards").Exists())
				Delete.Table("LmsFlashcards");
			if (Schema.Table("LmsCourseOrigins").Exists())
				Delete.Table("LmsCourseOrigins");
		}
	}
}

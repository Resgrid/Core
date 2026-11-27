using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// K1 knowledge gaps (protocols-training-knowledge-plan §16.4): questions the department's protocols and training did not cover, and
	/// answers given a thumbs-down, with the asker's role, the time and the nearest sources, for training officers and protocol owners to
	/// act on. While Advanced Data Protection is on the question text is not stored. No foreign keys; purged by DepartmentId
	/// (UnlinkedDepartmentCleanup). Existence-guarded.
	/// </summary>
	[Migration(354)]
	public class M0354_AddKnowledgeGaps : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("KnowledgeGaps").Exists())
			{
				Create.Table("KnowledgeGaps")
					.WithColumn("KnowledgeGapId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).Nullable()
					.WithColumn("AskerRole").AsString(250).Nullable()
					.WithColumn("Question").AsString(2000).Nullable()
					.WithColumn("Reason").AsInt32().NotNullable()
					.WithColumn("Surface").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AskedOn").AsDateTime2().NotNullable()
					.WithColumn("NearestSourcesJson").AsString(4000).Nullable()
					.WithColumn("ContextKind").AsInt32().Nullable()
					.WithColumn("ContextItemId").AsString(64).Nullable()
					.WithColumn("AiGenerationId").AsString(36).Nullable()
					.WithColumn("ResolvedOn").AsDateTime2().Nullable()
					.WithColumn("ResolvedByUserId").AsString(128).Nullable()
					.WithColumn("ResolutionNote").AsString(500).Nullable();
			}

			if (!Schema.Table("KnowledgeGaps").Index("IX_KnowledgeGaps_Department").Exists())
				Create.Index("IX_KnowledgeGaps_Department").OnTable("KnowledgeGaps")
					.OnColumn("DepartmentId").Ascending().OnColumn("Status").Ascending().OnColumn("AskedOn").Descending();
		}

		public override void Down()
		{
			if (Schema.Table("KnowledgeGaps").Exists())
				Delete.Table("KnowledgeGaps");
		}
	}
}

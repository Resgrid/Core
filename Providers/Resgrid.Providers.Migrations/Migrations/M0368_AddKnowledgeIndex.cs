using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// K1, the department knowledge index (protocols-training-knowledge-plan §16, decision §25.4a item 11):
	/// - KnowledgeChunks: one row per retrievable section or page, with its source (kind, item, version, anchor, heading path), effective
	///   dates, audience rules and access flags, and its embedding as a varbinary(max) of little-endian float32 values, scored exactly
	///   (cosine) inside Resgrid.Search after the department and audience filters. DepartmentId 0 is the Academy system corpus.
	/// - KnowledgeIndexRequests: the per-item reindex queue (publish, retire, audience change) worker 89 drains, unique per item.
	/// - KnowledgeIndexStates: per department, the revision readers cache against and the nightly repair schedule.
	/// No foreign keys: the rows are derived from content and are purged by DepartmentId (UnlinkedDepartmentCleanup). Existence-guarded.
	/// </summary>
	[Migration(368)]
	public class M0368_AddKnowledgeIndex : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("KnowledgeChunks").Exists())
			{
				Create.Table("KnowledgeChunks")
					.WithColumn("KnowledgeChunkId").AsInt64().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("SourceKind").AsInt32().NotNullable()
					.WithColumn("ItemId").AsString(64).NotNullable()
					.WithColumn("VersionId").AsString(64).Nullable()
					.WithColumn("VersionLabel").AsString(32).Nullable()
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Anchor").AsString(128).Nullable()
					.WithColumn("HeadingPath").AsString(500).Nullable()
					.WithColumn("Title").AsString(250).NotNullable()
					.WithColumn("Text").AsString(int.MaxValue).NotNullable()
					.WithColumn("TextHash").AsString(64).NotNullable()
					.WithColumn("Locale").AsString(16).Nullable()
					.WithColumn("EffectiveOn").AsDateTime2().Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("AudienceJson").AsString(4000).Nullable()
					.WithColumn("AccessFlags").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Embedding").AsBinary(int.MaxValue).Nullable()
					.WithColumn("EmbeddingModel").AsString(128).Nullable()
					.WithColumn("EmbeddedOn").AsDateTime2().Nullable()
					.WithColumn("IndexedOn").AsDateTime2().NotNullable();
			}

			if (!Schema.Table("KnowledgeChunks").Index("IX_KnowledgeChunks_Item").Exists())
				Create.Index("IX_KnowledgeChunks_Item").OnTable("KnowledgeChunks")
					.OnColumn("DepartmentId").Ascending().OnColumn("SourceKind").Ascending().OnColumn("ItemId").Ascending();

			if (!Schema.Table("KnowledgeIndexRequests").Exists())
			{
				Create.Table("KnowledgeIndexRequests")
					.WithColumn("KnowledgeIndexRequestId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("SourceKind").AsInt32().NotNullable()
					.WithColumn("ItemId").AsString(64).NotNullable().WithDefaultValue("")
					.WithColumn("RequestedOn").AsDateTime2().NotNullable()
					.WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Revision").AsInt32().NotNullable().WithDefaultValue(0);
			}

			if (!Schema.Table("KnowledgeIndexRequests").Index("UX_KnowledgeIndexRequests_Item").Exists())
				Create.Index("UX_KnowledgeIndexRequests_Item").OnTable("KnowledgeIndexRequests")
					.OnColumn("DepartmentId").Ascending().OnColumn("SourceKind").Ascending().OnColumn("ItemId").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("KnowledgeIndexStates").Exists())
			{
				Create.Table("KnowledgeIndexStates")
					.WithColumn("DepartmentId").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("Revision").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("Active").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("NextRepairOn").AsDateTime2().Nullable()
					.WithColumn("LastRepairOn").AsDateTime2().Nullable()
					.WithColumn("LastIndexedOn").AsDateTime2().Nullable()
					.WithColumn("ChunkCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("EmbeddedCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("EmbeddingModel").AsString(128).Nullable()
					.WithColumn("LastError").AsString(500).Nullable();
			}

			if (!Schema.Table("KnowledgeIndexStates").Index("IX_KnowledgeIndexStates_Repair").Exists())
				Create.Index("IX_KnowledgeIndexStates_Repair").OnTable("KnowledgeIndexStates")
					.OnColumn("Active").Ascending().OnColumn("NextRepairOn").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("KnowledgeIndexStates").Exists())
				Delete.Table("KnowledgeIndexStates");
			if (Schema.Table("KnowledgeIndexRequests").Exists())
				Delete.Table("KnowledgeIndexRequests");
			if (Schema.Table("KnowledgeChunks").Exists())
				Delete.Table("KnowledgeChunks");
		}
	}
}

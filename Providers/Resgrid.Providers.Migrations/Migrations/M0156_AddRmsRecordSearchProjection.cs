using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records (RMS) search projection (RMS plan sections 5.3/5.10, registry M0156).
	/// RmsRecordSearchProjections is the derived, rebuildable RecordSearchProjectionV1 row per Record:
	/// department scope, identity, number/type/state, safe dates, station/group, Call correlation,
	/// authorization hints (author, reviewer, participant/unit ids, group-scope set) and SearchText
	/// limited to fields whose classification and Searchable flag currently permit it. It feeds the
	/// RMS-owned records Lucene index and, later, Unified Search; it never holds narrative, addresses,
	/// restricted sections, ciphertext or attachment content. RmsSearchIndexStates tracks the index
	/// generation key (schemaVersion, protectedCatalogVersion, policyEpoch) per department so an
	/// enrollment or permission change cannot serve stale hits. Existence-guarded for safe retry.
	/// </summary>
	[Migration(156)]
	public class M0156_AddRmsRecordSearchProjection : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsRecordSearchProjections").Exists())
			{
				Create.Table("RmsRecordSearchProjections")
					.WithColumn("RmsRecordSearchProjectionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("SourceType").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("SourceId").AsString(36).NotNullable()
					.WithColumn("RecordKind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("RecordNumber").AsString(50).Nullable()
					.WithColumn("DraftReference").AsString(20).Nullable()
					.WithColumn("DefinitionKey").AsString(100).NotNullable()
					.WithColumn("DefinitionVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("RecordType").AsInt32().Nullable()
					.WithColumn("State").AsInt32().NotNullable()
					.WithColumn("OccurredOn").AsDateTime2().Nullable()
					.WithColumn("RecordCreatedOn").AsDateTime2().NotNullable()
					.WithColumn("FinalizedOn").AsDateTime2().Nullable()
					.WithColumn("StationGroupId").AsInt32().Nullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("CallNumber").AsString(50).Nullable()
					.WithColumn("AuthorUserId").AsString(128).Nullable()
					.WithColumn("OwnerUserId").AsString(128).Nullable()
					.WithColumn("ReviewerUserId").AsString(128).Nullable()
					.WithColumn("ParticipantUserIds").AsString(int.MaxValue).Nullable()
					.WithColumn("UnitIds").AsString(int.MaxValue).Nullable()
					.WithColumn("GroupScopeIds").AsString(int.MaxValue).Nullable()
					.WithColumn("DisplaySummary").AsString(400).Nullable()
					.WithColumn("SearchText").AsString(int.MaxValue).Nullable()
					.WithColumn("IsLegacy").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProjectionVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PolicyEpoch").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsRecordSearchProjections_Department_Source ON RmsRecordSearchProjections (DepartmentId, SourceType, SourceId);");
				Create.Index("IX_RmsRecordSearchProjections_Department_State").OnTable("RmsRecordSearchProjections")
					.OnColumn("DepartmentId").Ascending().OnColumn("State").Ascending();
				Create.Index("IX_RmsRecordSearchProjections_Department_Occurred").OnTable("RmsRecordSearchProjections")
					.OnColumn("DepartmentId").Ascending().OnColumn("OccurredOn").Descending();
				Create.Index("IX_RmsRecordSearchProjections_Department_Modified").OnTable("RmsRecordSearchProjections")
					.OnColumn("DepartmentId").Ascending().OnColumn("ModifiedOn").Ascending();
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_RmsRecordSearchProjections_Department_Call ON RmsRecordSearchProjections (DepartmentId, CallId) WHERE CallId IS NOT NULL;");
			}

			if (!Schema.Table("RmsSearchIndexStates").Exists())
			{
				Create.Table("RmsSearchIndexStates")
					.WithColumn("RmsSearchIndexStateId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IndexName").AsString(50).NotNullable()
					.WithColumn("SchemaVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PolicyEpoch").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("Generation").AsString(100).NotNullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DocumentCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LastRebuiltOn").AsDateTime2().Nullable()
					.WithColumn("LastIndexedModifiedOn").AsDateTime2().Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable();

				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsSearchIndexStates_Department_Index ON RmsSearchIndexStates (DepartmentId, IndexName);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("RmsSearchIndexStates").Exists())
				Delete.Table("RmsSearchIndexStates");

			if (Schema.Table("RmsRecordSearchProjections").Exists())
				Delete.Table("RmsRecordSearchProjections");
		}
	}
}

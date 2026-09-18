using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Unified Search Phase 1b/2 (plan R4, R5; registry §4F, next physical number under the no-gaps rule):
	/// SearchProjections is the safe, rebuildable search row per entity for the global index (one table with an
	/// EntityType discriminator; only allowlisted fields, cataloged columns only where protection is not enforced,
	/// never an envelope); SearchIndexStates tracks the per-index, per-department generation key
	/// (schemaVersion, protectedCatalogVersion, policyEpoch), checkpoint and admin rebuild requests for the shared
	/// host; SearchIndexLeases is the single-writer publish lease for the object store. Seeds the Search.Unified
	/// feature flag off. Existence-guarded for safe retry.
	/// </summary>
	[Migration(208)]
	public class M0208_AddUnifiedSearch : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("SearchProjections").Exists())
			{
				Create.Table("SearchProjections")
					.WithColumn("SearchProjectionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("EntityType").AsString(30).NotNullable()
					.WithColumn("EntityId").AsString(128).NotNullable()
					.WithColumn("Title").AsString(400).Nullable()
					.WithColumn("Summary").AsString(1000).Nullable()
					.WithColumn("SearchText").AsString(int.MaxValue).Nullable()
					.WithColumn("Keywords").AsString(400).Nullable()
					.WithColumn("Category").AsString(100).Nullable()
					.WithColumn("Status").AsString(50).Nullable()
					.WithColumn("Priority").AsInt32().Nullable()
					.WithColumn("GroupId").AsInt32().Nullable()
					.WithColumn("OwnerUserId").AsString(128).Nullable()
					.WithColumn("ParticipantUserIds").AsString(int.MaxValue).Nullable()
					.WithColumn("IsAdminOnly").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("OccurredOn").AsDateTime2().NotNullable()
					.WithColumn("Url").AsString(400).Nullable()
					.WithColumn("MetadataJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PolicyEpoch").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("IncludesProtectedText").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_SearchProjections_Department_Entity ON SearchProjections (DepartmentId, EntityType, EntityId);");
				Create.Index("IX_SearchProjections_Department_Modified").OnTable("SearchProjections")
					.OnColumn("DepartmentId").Ascending().OnColumn("ModifiedOn").Ascending();
				Create.Index("IX_SearchProjections_Department_Type_Modified").OnTable("SearchProjections")
					.OnColumn("DepartmentId").Ascending().OnColumn("EntityType").Ascending().OnColumn("ModifiedOn").Ascending();
			}

			if (!Schema.Table("SearchIndexStates").Exists())
			{
				Create.Table("SearchIndexStates")
					.WithColumn("SearchIndexStateId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("IndexName").AsString(50).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("SchemaVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PolicyEpoch").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("Generation").AsString(100).NotNullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DocumentCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LastRebuiltOn").AsDateTime2().Nullable()
					.WithColumn("LastIndexedModifiedOn").AsDateTime2().Nullable()
					.WithColumn("RebuildRequestedOn").AsDateTime2().Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable();

				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_SearchIndexStates_Index_Department ON SearchIndexStates (IndexName, DepartmentId);");
			}

			if (!Schema.Table("SearchIndexLeases").Exists())
			{
				Create.Table("SearchIndexLeases")
					.WithColumn("IndexName").AsString(50).NotNullable().PrimaryKey()
					.WithColumn("LeaseOwner").AsString(200).Nullable()
					.WithColumn("LeaseExpiresOn").AsDateTime2().Nullable()
					.WithColumn("LastPublishedRevision").AsString(64).Nullable()
					.WithColumn("LastPublishedOn").AsDateTime2().Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable();
			}

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Search.Unified') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('Search.Unified', 'Unified Search', 'Cross-entity search (calls, units, personnel, contacts, messages, documents, notes, records) and the system-functionality command palette. Requires the search host (SearchConfig.Enabled) in every process. Seeded off.', 'Search', 0);");
		}

		public override void Down()
		{
			if (Schema.Table("SearchIndexLeases").Exists())
				Delete.Table("SearchIndexLeases");

			if (Schema.Table("SearchIndexStates").Exists())
				Delete.Table("SearchIndexStates");

			if (Schema.Table("SearchProjections").Exists())
				Delete.Table("SearchProjections");

			// The flag row is operator-owned once seeded (overrides, targeting); Up tolerates its presence.
		}
	}
}

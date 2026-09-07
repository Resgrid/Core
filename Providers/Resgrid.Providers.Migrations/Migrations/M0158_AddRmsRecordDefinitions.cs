using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records (RMS-1B) department definitions (plan sections 4.1 and 5.2, registry M0158): stable definition identity, immutable-once-published versions (authored SchemaJson, checksum, capability floor, numbering/lifecycle/retention policy) and the section/field rows materialized at publish for stable query identity.
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(158)]
	public class M0158_AddRmsRecordDefinitions : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsRecordDefinitions").Exists())
			{
				Create.Table("RmsRecordDefinitions")
					.WithColumn("RmsRecordDefinitionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("DefinitionKey").AsString(200).NotNullable()
					.WithColumn("Owner").AsInt32().NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Category").AsString(200).Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("TemplateKey").AsString(200).Nullable()
					.WithColumn("TemplatePackVersion").AsInt32().Nullable()
					.WithColumn("JurisdictionProfileKey").AsString(64).Nullable()
					.WithColumn("PermittedSubjectTypes").AsString(400).Nullable()
					.WithColumn("CurrentPublishedVersion").AsInt32().Nullable()
					.WithColumn("LatestVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsRetired").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RetiredOn").AsDateTime2().Nullable()
					.WithColumn("RetiredByUserId").AsString(128).Nullable()
					.WithColumn("RetiredReason").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsRecordDefinitions_Department_Key ON RmsRecordDefinitions (DepartmentId, DefinitionKey);");
			}

			if (!Schema.Table("RmsRecordDefinitionVersions").Exists())
			{
				Create.Table("RmsRecordDefinitionVersions")
					.WithColumn("RmsRecordDefinitionVersionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RmsRecordDefinitionId").AsString(36).NotNullable()
					.WithColumn("DefinitionKey").AsString(200).NotNullable()
					.WithColumn("Version").AsInt32().NotNullable()
					.WithColumn("State").AsInt32().NotNullable()
					.WithColumn("LifecyclePreset").AsInt32().NotNullable()
					.WithColumn("ReviewerRoleIds").AsString(400).Nullable()
					.WithColumn("ApproverRoleIds").AsString(400).Nullable()
					.WithColumn("ReviewDueHours").AsInt32().Nullable()
					.WithColumn("ApproveDueHours").AsInt32().Nullable()
					.WithColumn("RequireAuthorAttestation").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("NumberingJson").AsString(int.MaxValue).Nullable()
					.WithColumn("RetentionYears").AsInt32().Nullable()
					.WithColumn("Classification").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SchemaJson").AsString(int.MaxValue).Nullable()
					.WithColumn("SchemaChecksum").AsString(128).Nullable()
					.WithColumn("MinimumClientCapability").AsString(64).Nullable()
					.WithColumn("ClientSurfaceJson").AsString(int.MaxValue).Nullable()
					.WithColumn("MigrationMapJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ChangeNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("PublishedOn").AsDateTime2().Nullable()
					.WithColumn("PublishedByUserId").AsString(128).Nullable()
					.WithColumn("RetiredOn").AsDateTime2().Nullable()
					.WithColumn("RetiredByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsRecordDefinitionVersions_Department_Key_Version ON RmsRecordDefinitionVersions (DepartmentId, DefinitionKey, Version);");
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_RmsRecordDefinitionVersions_Department_State ON RmsRecordDefinitionVersions (DepartmentId, State);");
			}

			if (!Schema.Table("RmsRecordSectionDefinitions").Exists())
			{
				Create.Table("RmsRecordSectionDefinitions")
					.WithColumn("RmsRecordSectionDefinitionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RmsRecordDefinitionVersionId").AsString(36).NotNullable()
					.WithColumn("DefinitionKey").AsString(200).NotNullable()
					.WithColumn("DefinitionVersion").AsInt32().NotNullable()
					.WithColumn("SectionKey").AsString(64).NotNullable()
					.WithColumn("Label").AsString(400).Nullable()
					.WithColumn("Help").AsString(int.MaxValue).Nullable()
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsRepeating").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("MinRows").AsInt32().Nullable()
					.WithColumn("MaxRows").AsInt32().Nullable()
					.WithColumn("RulesJson").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsRecordSectionDefinitions_Version_Key ON RmsRecordSectionDefinitions (DepartmentId, RmsRecordDefinitionVersionId, SectionKey);");
			}

			if (!Schema.Table("RmsRecordFieldDefinitions").Exists())
			{
				Create.Table("RmsRecordFieldDefinitions")
					.WithColumn("RmsRecordFieldDefinitionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RmsRecordDefinitionVersionId").AsString(36).NotNullable()
					.WithColumn("DefinitionKey").AsString(200).NotNullable()
					.WithColumn("DefinitionVersion").AsInt32().NotNullable()
					.WithColumn("SectionKey").AsString(64).NotNullable()
					.WithColumn("FieldKey").AsString(64).NotNullable()
					.WithColumn("Label").AsString(400).Nullable()
					.WithColumn("DataType").AsInt32().NotNullable()
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Required").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RequiredToFinalize").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Classification").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ReferenceType").AsString(64).Nullable()
					.WithColumn("Searchable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Filterable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Sortable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Groupable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Aggregatable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("WorkflowExposed").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Exportable").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("ConstraintsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("RulesJson").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsRecordFieldDefinitions_Version_Key ON RmsRecordFieldDefinitions (DepartmentId, RmsRecordDefinitionVersionId, FieldKey);");
			}

		}

		public override void Down()
		{
			if (Schema.Table("RmsRecordFieldDefinitions").Exists())
				Delete.Table("RmsRecordFieldDefinitions");
			if (Schema.Table("RmsRecordSectionDefinitions").Exists())
				Delete.Table("RmsRecordSectionDefinitions");
			if (Schema.Table("RmsRecordDefinitionVersions").Exists())
				Delete.Table("RmsRecordDefinitionVersions");
			if (Schema.Table("RmsRecordDefinitions").Exists())
				Delete.Table("RmsRecordDefinitions");
		}
	}
}

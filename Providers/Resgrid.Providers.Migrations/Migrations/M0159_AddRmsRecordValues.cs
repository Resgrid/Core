using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records (RMS-1B) typed values (plan section 5.3, registry M0159): one discriminated table, one row per scalar / repeating-group cell / multi-select option, exactly one column group populated (check constraint plus the service guard), repeating rows in RmsRecordValueGroups, explicit equality/range indexes filtered to unprotected rows, inert ADP columns.
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(159)]
	public class M0159_AddRmsRecordValues : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsRecordValueGroups").Exists())
			{
				Create.Table("RmsRecordValueGroups")
					.WithColumn("RmsRecordValueGroupId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RecordKind").AsInt32().NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("RmsRecordDefinitionVersionId").AsString(36).Nullable()
					.WithColumn("SectionKey").AsString(64).NotNullable()
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ClientRowKey").AsString(64).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_RmsRecordValueGroups_Department_Record_Revision ON RmsRecordValueGroups (DepartmentId, RecordId, RevisionId);");
			}

			if (!Schema.Table("RmsRecordValues").Exists())
			{
				Create.Table("RmsRecordValues")
					.WithColumn("RmsRecordValueId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RecordKind").AsInt32().NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("RmsRecordDefinitionVersionId").AsString(36).Nullable()
					.WithColumn("FieldKey").AsString(64).NotNullable()
					.WithColumn("RmsRecordValueGroupId").AsString(36).Nullable()
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ValueType").AsInt32().NotNullable()
					.WithColumn("TextValue").AsString(400).Nullable()
					.WithColumn("LongTextValue").AsString(int.MaxValue).Nullable()
					.WithColumn("NumberValue").AsDecimal(28, 10).Nullable()
					.WithColumn("BoolValue").AsBoolean().Nullable()
					.WithColumn("DateTimeValue").AsDateTime2().Nullable()
					.WithColumn("DateTimeOffsetMinutes").AsInt32().Nullable()
					.WithColumn("DurationSeconds").AsInt64().Nullable()
					.WithColumn("UnitCode").AsString(64).Nullable()
					.WithColumn("CanonicalNumberValue").AsDecimal(28, 10).Nullable()
					.WithColumn("CanonicalUnitCode").AsString(64).Nullable()
					.WithColumn("CurrencyCode").AsString(64).Nullable()
					.WithColumn("ReferenceType").AsString(64).Nullable()
					.WithColumn("ReferenceId").AsString(200).Nullable()
					.WithColumn("ReferenceSnapshotJson").AsString(int.MaxValue).Nullable()
					.WithColumn("OptionKey").AsString(64).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedEnvelope").AsString(int.MaxValue).Nullable()
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_RmsRecordValues_Department_Record_Revision ON RmsRecordValues (DepartmentId, RecordId, RevisionId);");
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_RmsRecordValues_Department_Version_Field_Text ON RmsRecordValues (DepartmentId, RmsRecordDefinitionVersionId, FieldKey, TextValue) WHERE IsProtected = 0;");
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_RmsRecordValues_Department_Version_Field_Number ON RmsRecordValues (DepartmentId, RmsRecordDefinitionVersionId, FieldKey, NumberValue) WHERE IsProtected = 0;");
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_RmsRecordValues_Department_Version_Field_DateTime ON RmsRecordValues (DepartmentId, RmsRecordDefinitionVersionId, FieldKey, DateTimeValue) WHERE IsProtected = 0;");
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_RmsRecordValues_Department_Revision ON RmsRecordValues (DepartmentId, RevisionId);");
				Execute.Sql("ALTER TABLE RmsRecordValues ADD CONSTRAINT CK_RmsRecordValues_OneColumnGroup CHECK ((CASE WHEN TextValue IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN LongTextValue IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN NumberValue IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN BoolValue IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN DateTimeValue IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN DurationSeconds IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN ReferenceId IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN OptionKey IS NOT NULL THEN 1 ELSE 0 END) = 1 OR ProtectedEnvelope IS NOT NULL);");
			}

		}

		public override void Down()
		{
			if (Schema.Table("RmsRecordValues").Exists())
				Delete.Table("RmsRecordValues");
			if (Schema.Table("RmsRecordValueGroups").Exists())
				Delete.Table("RmsRecordValueGroups");
		}
	}
}

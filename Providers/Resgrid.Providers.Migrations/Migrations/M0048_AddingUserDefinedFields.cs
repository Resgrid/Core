using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(48)]
	public class M0048_AddingUserDefinedFields : Migration
	{
		public override void Up()
		{
			// ── UdfDefinitions ────────────────────────────────────────────────
			Create.Table("UdfDefinitions")
				.WithColumn("UdfDefinitionId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("EntityType").AsInt32().NotNullable()
				.WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(1)
				.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("CreatedOn").AsDateTime().NotNullable()
				.WithColumn("CreatedBy").AsString(128).NotNullable()
				.WithColumn("UpdatedOn").AsDateTime().Nullable()
				.WithColumn("UpdatedBy").AsString(128).Nullable();

			Create.Index("IX_UdfDefinitions_DepartmentId_EntityType_IsActive")
				.OnTable("UdfDefinitions")
				.OnColumn("DepartmentId").Ascending()
				.OnColumn("EntityType").Ascending()
				.OnColumn("IsActive").Ascending();

			// ── UdfFields ─────────────────────────────────────────────────────
			Create.Table("UdfFields")
				.WithColumn("UdfFieldId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("UdfDefinitionId").AsString(128).NotNullable()
				.WithColumn("Name").AsString(200).NotNullable()
				.WithColumn("Label").AsString(200).NotNullable()
				.WithColumn("Description").AsString(500).Nullable()
				.WithColumn("Placeholder").AsString(200).Nullable()
				.WithColumn("FieldDataType").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("IsRequired").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("IsReadOnly").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("DefaultValue").AsString(int.MaxValue).Nullable()
				.WithColumn("ValidationRules").AsString(int.MaxValue).Nullable()
				.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("GroupName").AsString(100).Nullable()
				.WithColumn("IsVisibleOnMobile").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("IsVisibleOnReports").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true);

			Create.Index("IX_UdfFields_UdfDefinitionId")
				.OnTable("UdfFields")
				.OnColumn("UdfDefinitionId").Ascending();

			// ── UdfFieldValues ────────────────────────────────────────────────
			Create.Table("UdfFieldValues")
				.WithColumn("UdfFieldValueId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("UdfFieldId").AsString(128).NotNullable()
				.WithColumn("UdfDefinitionId").AsString(128).NotNullable()
				.WithColumn("EntityId").AsString(128).NotNullable()
				.WithColumn("EntityType").AsInt32().NotNullable()
				.WithColumn("Value").AsString(int.MaxValue).Nullable()
				.WithColumn("CreatedOn").AsDateTime().NotNullable()
				.WithColumn("CreatedBy").AsString(128).NotNullable()
				.WithColumn("UpdatedOn").AsDateTime().Nullable()
				.WithColumn("UpdatedBy").AsString(128).Nullable();

			Create.Index("IX_UdfFieldValues_EntityType_EntityId_DefinitionId")
				.OnTable("UdfFieldValues")
				.OnColumn("EntityType").Ascending()
				.OnColumn("EntityId").Ascending()
				.OnColumn("UdfDefinitionId").Ascending();
		}

		public override void Down()
		{
			Delete.Index("IX_UdfFieldValues_EntityType_EntityId_DefinitionId").OnTable("UdfFieldValues");
			Delete.Table("UdfFieldValues");

			Delete.Index("IX_UdfFields_UdfDefinitionId").OnTable("UdfFields");
			Delete.Table("UdfFields");

			Delete.Index("IX_UdfDefinitions_DepartmentId_EntityType_IsActive").OnTable("UdfDefinitions");
			Delete.Table("UdfDefinitions");
		}
	}
}


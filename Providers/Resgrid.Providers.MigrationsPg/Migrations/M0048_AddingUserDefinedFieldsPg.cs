using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(48)]
	public class M0048_AddingUserDefinedFieldsPg : Migration
	{
		public override void Up()
		{
			// ── UdfDefinitions ────────────────────────────────────────────────
			Create.Table("UdfDefinitions".ToLower())
				.WithColumn("UdfDefinitionId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
				.WithColumn("EntityType".ToLower()).AsInt32().NotNullable()
				.WithColumn("Version".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
				.WithColumn("IsActive".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("CreatedOn".ToLower()).AsDateTime().NotNullable()
				.WithColumn("CreatedBy".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("UpdatedOn".ToLower()).AsDateTime().Nullable()
				.WithColumn("UpdatedBy".ToLower()).AsCustom("citext").Nullable();

			Create.Index("IX_UdfDefinitions_DepartmentId_EntityType_IsActive".ToLower())
				.OnTable("UdfDefinitions".ToLower())
				.OnColumn("DepartmentId".ToLower()).Ascending()
				.OnColumn("EntityType".ToLower()).Ascending()
				.OnColumn("IsActive".ToLower()).Ascending();

			// ── UdfFields ─────────────────────────────────────────────────────
			Create.Table("UdfFields".ToLower())
				.WithColumn("UdfFieldId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("UdfDefinitionId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Label".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Description".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("Placeholder".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("FieldDataType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("IsRequired".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("IsReadOnly".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("DefaultValue".ToLower()).AsCustom("text").Nullable()
				.WithColumn("ValidationRules".ToLower()).AsCustom("text").Nullable()
				.WithColumn("SortOrder".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("GroupName".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("IsVisibleOnMobile".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("IsVisibleOnReports".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("IsEnabled".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true);

			Create.Index("IX_UdfFields_UdfDefinitionId".ToLower())
				.OnTable("UdfFields".ToLower())
				.OnColumn("UdfDefinitionId".ToLower()).Ascending();

			// ── UdfFieldValues ────────────────────────────────────────────────
			Create.Table("UdfFieldValues".ToLower())
				.WithColumn("UdfFieldValueId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("UdfFieldId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("UdfDefinitionId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("EntityId".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("EntityType".ToLower()).AsInt32().NotNullable()
				.WithColumn("Value".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("CreatedOn".ToLower()).AsDateTime().NotNullable()
				.WithColumn("CreatedBy".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("UpdatedOn".ToLower()).AsDateTime().Nullable()
				.WithColumn("UpdatedBy".ToLower()).AsCustom("citext").Nullable();

			Create.Index("IX_UdfFieldValues_EntityType_EntityId_DefinitionId".ToLower())
				.OnTable("UdfFieldValues".ToLower())
				.OnColumn("EntityType".ToLower()).Ascending()
				.OnColumn("EntityId".ToLower()).Ascending()
				.OnColumn("UdfDefinitionId".ToLower()).Ascending();
		}

		public override void Down()
		{
			Delete.Index("IX_UdfFieldValues_EntityType_EntityId_DefinitionId".ToLower()).OnTable("UdfFieldValues".ToLower());
			Delete.Table("UdfFieldValues".ToLower());

			Delete.Index("IX_UdfFields_UdfDefinitionId".ToLower()).OnTable("UdfFields".ToLower());
			Delete.Table("UdfFields".ToLower());

			Delete.Index("IX_UdfDefinitions_DepartmentId_EntityType_IsActive".ToLower()).OnTable("UdfDefinitions".ToLower());
			Delete.Table("UdfDefinitions".ToLower());
		}
	}
}


using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records (RMS) print layouts (RMS plan section 4.10.1, registry M0160). One versioned row per
	/// (department, scope, definition key): the DepartmentDefault scope carries the branding block, letterhead lines,
	/// footer, watermark, page size and date format edited from Records Settings in RMS-1; the Definition scope is
	/// written by the RMS-1B designer. DefinitionKey is the empty string for the department default so the unique
	/// index behaves identically in both dialects. Numbering counters stay on the MAX(sequence) path for now.
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(160)]
	public class M0160_AddRmsPrintLayouts : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsRecordPrintLayouts").Exists())
			{
				Create.Table("RmsRecordPrintLayouts")
					.WithColumn("RmsRecordPrintLayoutId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("Scope").AsInt32().NotNullable()
					.WithColumn("DefinitionKey").AsString(200).NotNullable().WithDefaultValue("")
					.WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ConfigJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsRecordPrintLayouts_Department_Scope_Definition ON RmsRecordPrintLayouts (DepartmentId, Scope, DefinitionKey);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("RmsRecordPrintLayouts").Exists())
				Delete.Table("RmsRecordPrintLayouts");
		}
	}
}

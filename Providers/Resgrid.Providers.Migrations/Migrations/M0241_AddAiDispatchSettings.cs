using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Per-department AI dispatch settings (enhanced-ai-addon-plan.md §4; registry §4D, next physical number): the
	/// <c>DepartmentAiDispatchConfigs</c> table. Absent row = defaults (host confidence, every sender, no separate cap, 365-day audit,
	/// every enrichment on). Revision is the compare-and-swap token. No setting types are used, per the Enhanced AI allocation.
	/// </summary>
	[Migration(241)]
	public class M0241_AddAiDispatchSettings : Migration
	{
		public override void Up()
		{
			if (Schema.Table("DepartmentAiDispatchConfigs").Exists())
				return;

			Create.Table("DepartmentAiDispatchConfigs")
				.WithColumn("DepartmentId").AsInt32().NotNullable().PrimaryKey()
				.WithColumn("MinimumConfidence").AsDecimal(3, 2).Nullable()
				.WithColumn("SenderAllowlist").AsString(4000).Nullable()
				.WithColumn("MonthlyTokenCap").AsInt32().Nullable()
				.WithColumn("AuditRetentionDays").AsInt32().NotNullable().WithDefaultValue(365)
				.WithColumn("FillCallType").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("FillAddress").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("FillContact").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("FillIncidentNumber").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("RenamePlaceholder").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("AddSummaryNote").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("FlagRelatedCalls").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("Revision").AsInt64().NotNullable()
				.WithColumn("UpdatedByUserId").AsString(128).Nullable()
				.WithColumn("UpdatedOnUtc").AsDateTime2().Nullable();
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('[DepartmentAiDispatchConfigs]', 'U') IS NOT NULL DROP TABLE [DepartmentAiDispatchConfigs];");
		}
	}
}

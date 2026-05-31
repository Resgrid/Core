using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(71)]
	public class M0071_AddingFeatureToggles : Migration
	{
		public override void Up()
		{
			// FeatureFlags - system-wide flag definitions with a global default.
			Create.Table("FeatureFlags")
				.WithColumn("FeatureFlagId").AsInt32().NotNullable().PrimaryKey().Identity()
				.WithColumn("FlagKey").AsString(256).NotNullable()
				.WithColumn("Name").AsString(256).NotNullable()
				.WithColumn("Description").AsString(1000).Nullable()
				.WithColumn("Category").AsString(128).Nullable()
				.WithColumn("Tags").AsString(512).Nullable()
				.WithColumn("FlagType").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("IsEnabledGlobally").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("DefaultValue").AsString(int.MaxValue).Nullable()
				.WithColumn("OffValue").AsString(int.MaxValue).Nullable()
				.WithColumn("RolloutPercentage").AsInt32().Nullable()
				.WithColumn("MinimumPlanType").AsInt32().Nullable()
				.WithColumn("Environment").AsInt32().Nullable()
				.WithColumn("EnableOn").AsDateTime2().Nullable()
				.WithColumn("DisableOn").AsDateTime2().Nullable()
				.WithColumn("IsArchived").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("IsPermanent").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("LastEvaluatedOn").AsDateTime2().Nullable()
				.WithColumn("CreatedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
				.WithColumn("CreatedByUserId").AsString(450).Nullable()
				.WithColumn("UpdatedOn").AsDateTime2().Nullable()
				.WithColumn("UpdatedByUserId").AsString(450).Nullable();

			Create.Index("UX_FeatureFlags_FlagKey")
				.OnTable("FeatureFlags")
				.OnColumn("FlagKey").Ascending()
				.WithOptions().Unique();

			// FeatureFlagOverrides - per-department override of a flag's value.
			Create.Table("FeatureFlagOverrides")
				.WithColumn("FeatureFlagOverrideId").AsInt32().NotNullable().PrimaryKey().Identity()
				.WithColumn("FeatureFlagId").AsInt32().NotNullable()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("FlagValue").AsString(int.MaxValue).Nullable()
				.WithColumn("Reason").AsString(512).Nullable()
				.WithColumn("ExpiresOn").AsDateTime2().Nullable()
				.WithColumn("CreatedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
				.WithColumn("CreatedByUserId").AsString(450).Nullable()
				.WithColumn("UpdatedOn").AsDateTime2().Nullable()
				.WithColumn("UpdatedByUserId").AsString(450).Nullable();

			Create.Index("UX_FeatureFlagOverrides_Flag_Department")
				.OnTable("FeatureFlagOverrides")
				.OnColumn("FeatureFlagId").Ascending()
				.OnColumn("DepartmentId").Ascending()
				.WithOptions().Unique();

			// FeatureFlagTargetingRules - attribute/segment targeting rules.
			Create.Table("FeatureFlagTargetingRules")
				.WithColumn("FeatureFlagTargetingRuleId").AsInt32().NotNullable().PrimaryKey().Identity()
				.WithColumn("FeatureFlagId").AsInt32().NotNullable()
				.WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("AttributeType").AsInt32().NotNullable()
				.WithColumn("OperatorType").AsInt32().NotNullable()
				.WithColumn("ComparisonValue").AsString(int.MaxValue).Nullable()
				.WithColumn("ResultEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("ResultValue").AsString(int.MaxValue).Nullable()
				.WithColumn("RolloutPercentage").AsInt32().Nullable()
				.WithColumn("CreatedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
				.WithColumn("CreatedByUserId").AsString(450).Nullable()
				.WithColumn("UpdatedOn").AsDateTime2().Nullable()
				.WithColumn("UpdatedByUserId").AsString(450).Nullable();

			Create.Index("IX_FeatureFlagTargetingRules_Flag")
				.OnTable("FeatureFlagTargetingRules")
				.OnColumn("FeatureFlagId").Ascending();

			// FeatureFlagPrerequisites - flag dependency edges.
			Create.Table("FeatureFlagPrerequisites")
				.WithColumn("FeatureFlagPrerequisiteId").AsInt32().NotNullable().PrimaryKey().Identity()
				.WithColumn("FeatureFlagId").AsInt32().NotNullable()
				.WithColumn("RequiredFeatureFlagId").AsInt32().NotNullable()
				.WithColumn("RequiredValue").AsString(int.MaxValue).Nullable();

			Create.Index("IX_FeatureFlagPrerequisites_Flag")
				.OnTable("FeatureFlagPrerequisites")
				.OnColumn("FeatureFlagId").Ascending();

			// FeatureFlagUsages - aggregated daily evaluation counts (append-only flushes).
			Create.Table("FeatureFlagUsages")
				.WithColumn("FeatureFlagUsageId").AsInt32().NotNullable().PrimaryKey().Identity()
				.WithColumn("FeatureFlagId").AsInt32().NotNullable()
				.WithColumn("DepartmentId").AsInt32().Nullable()
				.WithColumn("UsageDate").AsDateTime2().NotNullable()
				.WithColumn("EvaluationCount").AsInt64().NotNullable().WithDefaultValue(0)
				.WithColumn("EnabledCount").AsInt64().NotNullable().WithDefaultValue(0)
				.WithColumn("DisabledCount").AsInt64().NotNullable().WithDefaultValue(0);

			Create.Index("IX_FeatureFlagUsages_Flag_Date")
				.OnTable("FeatureFlagUsages")
				.OnColumn("FeatureFlagId").Ascending()
				.OnColumn("UsageDate").Ascending();
		}

		public override void Down()
		{
			Delete.Table("FeatureFlagUsages");
			Delete.Table("FeatureFlagPrerequisites");
			Delete.Table("FeatureFlagTargetingRules");
			Delete.Table("FeatureFlagOverrides");
			Delete.Table("FeatureFlags");
		}
	}
}

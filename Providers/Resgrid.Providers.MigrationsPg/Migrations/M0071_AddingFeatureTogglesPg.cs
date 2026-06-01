using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(71)]
	public class M0071_AddingFeatureTogglesPg : Migration
	{
		public override void Up()
		{
			// FeatureFlags - system-wide flag definitions with a global default.
			Create.Table("FeatureFlags".ToLower())
				.WithColumn("FeatureFlagId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
				.WithColumn("FlagKey".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
				.WithColumn("Description".ToLower()).AsCustom("text").Nullable()
				.WithColumn("Category".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("Tags".ToLower()).AsCustom("text").Nullable()
				.WithColumn("FlagType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("IsEnabledGlobally".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("DefaultValue".ToLower()).AsCustom("text").Nullable()
				.WithColumn("OffValue".ToLower()).AsCustom("text").Nullable()
				.WithColumn("RolloutPercentage".ToLower()).AsInt32().Nullable()
				.WithColumn("MinimumPlanType".ToLower()).AsInt32().Nullable()
				.WithColumn("Environment".ToLower()).AsInt32().Nullable()
				.WithColumn("EnableOn".ToLower()).AsDateTime2().Nullable()
				.WithColumn("DisableOn".ToLower()).AsDateTime2().Nullable()
				.WithColumn("IsArchived".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("IsPermanent".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("LastEvaluatedOn".ToLower()).AsDateTime2().Nullable()
				.WithColumn("CreatedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
				.WithColumn("CreatedByUserId".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("UpdatedOn".ToLower()).AsDateTime2().Nullable()
				.WithColumn("UpdatedByUserId".ToLower()).AsCustom("citext").Nullable();

			Create.Index("UX_FeatureFlags_FlagKey".ToLower())
				.OnTable("FeatureFlags".ToLower())
				.OnColumn("FlagKey".ToLower()).Ascending()
				.WithOptions().Unique();

			// FeatureFlagOverrides - per-department override of a flag's value.
			Create.Table("FeatureFlagOverrides".ToLower())
				.WithColumn("FeatureFlagOverrideId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
				.WithColumn("FeatureFlagId".ToLower()).AsInt32().NotNullable()
				.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
				.WithColumn("IsEnabled".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("FlagValue".ToLower()).AsCustom("text").Nullable()
				.WithColumn("Reason".ToLower()).AsCustom("text").Nullable()
				.WithColumn("ExpiresOn".ToLower()).AsDateTime2().Nullable()
				.WithColumn("CreatedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
				.WithColumn("CreatedByUserId".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("UpdatedOn".ToLower()).AsDateTime2().Nullable()
				.WithColumn("UpdatedByUserId".ToLower()).AsCustom("citext").Nullable();

			Create.ForeignKey("FK_FeatureFlagOverrides_FeatureFlags".ToLower())
				.FromTable("FeatureFlagOverrides".ToLower()).ForeignColumn("FeatureFlagId".ToLower())
				.ToTable("FeatureFlags".ToLower()).PrimaryColumn("FeatureFlagId".ToLower());

			Create.ForeignKey("FK_FeatureFlagOverrides_Departments".ToLower())
				.FromTable("FeatureFlagOverrides".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());

			Create.Index("UX_FeatureFlagOverrides_Flag_Department".ToLower())
				.OnTable("FeatureFlagOverrides".ToLower())
				.OnColumn("FeatureFlagId".ToLower()).Ascending()
				.OnColumn("DepartmentId".ToLower()).Ascending()
				.WithOptions().Unique();

			// FeatureFlagTargetingRules - attribute/segment targeting rules.
			Create.Table("FeatureFlagTargetingRules".ToLower())
				.WithColumn("FeatureFlagTargetingRuleId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
				.WithColumn("FeatureFlagId".ToLower()).AsInt32().NotNullable()
				.WithColumn("Priority".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("AttributeType".ToLower()).AsInt32().NotNullable()
				.WithColumn("OperatorType".ToLower()).AsInt32().NotNullable()
				.WithColumn("ComparisonValue".ToLower()).AsCustom("text").Nullable()
				.WithColumn("ResultEnabled".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("ResultValue".ToLower()).AsCustom("text").Nullable()
				.WithColumn("RolloutPercentage".ToLower()).AsInt32().Nullable()
				.WithColumn("CreatedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
				.WithColumn("CreatedByUserId".ToLower()).AsCustom("citext").Nullable()
				.WithColumn("UpdatedOn".ToLower()).AsDateTime2().Nullable()
				.WithColumn("UpdatedByUserId".ToLower()).AsCustom("citext").Nullable();

			Create.ForeignKey("FK_FeatureFlagTargetingRules_FeatureFlags".ToLower())
				.FromTable("FeatureFlagTargetingRules".ToLower()).ForeignColumn("FeatureFlagId".ToLower())
				.ToTable("FeatureFlags".ToLower()).PrimaryColumn("FeatureFlagId".ToLower());

			Create.Index("IX_FeatureFlagTargetingRules_Flag".ToLower())
				.OnTable("FeatureFlagTargetingRules".ToLower())
				.OnColumn("FeatureFlagId".ToLower()).Ascending();

			// FeatureFlagPrerequisites - flag dependency edges.
			Create.Table("FeatureFlagPrerequisites".ToLower())
				.WithColumn("FeatureFlagPrerequisiteId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
				.WithColumn("FeatureFlagId".ToLower()).AsInt32().NotNullable()
				.WithColumn("RequiredFeatureFlagId".ToLower()).AsInt32().NotNullable()
				.WithColumn("RequiredValue".ToLower()).AsCustom("text").Nullable();

			Create.ForeignKey("FK_FeatureFlagPrerequisites_FeatureFlags".ToLower())
				.FromTable("FeatureFlagPrerequisites".ToLower()).ForeignColumn("FeatureFlagId".ToLower())
				.ToTable("FeatureFlags".ToLower()).PrimaryColumn("FeatureFlagId".ToLower());

			Create.ForeignKey("FK_FeatureFlagPrerequisites_RequiredFeatureFlag".ToLower())
				.FromTable("FeatureFlagPrerequisites".ToLower()).ForeignColumn("RequiredFeatureFlagId".ToLower())
				.ToTable("FeatureFlags".ToLower()).PrimaryColumn("FeatureFlagId".ToLower());

			Create.Index("IX_FeatureFlagPrerequisites_Flag".ToLower())
				.OnTable("FeatureFlagPrerequisites".ToLower())
				.OnColumn("FeatureFlagId".ToLower()).Ascending();

			// FeatureFlagUsages - aggregated daily evaluation counts (append-only flushes).
			Create.Table("FeatureFlagUsages".ToLower())
				.WithColumn("FeatureFlagUsageId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
				.WithColumn("FeatureFlagId".ToLower()).AsInt32().NotNullable()
				.WithColumn("DepartmentId".ToLower()).AsInt32().Nullable()
				.WithColumn("UsageDate".ToLower()).AsDateTime2().NotNullable()
				.WithColumn("EvaluationCount".ToLower()).AsInt64().NotNullable().WithDefaultValue(0)
				.WithColumn("EnabledCount".ToLower()).AsInt64().NotNullable().WithDefaultValue(0)
				.WithColumn("DisabledCount".ToLower()).AsInt64().NotNullable().WithDefaultValue(0);

			Create.ForeignKey("FK_FeatureFlagUsages_FeatureFlags".ToLower())
				.FromTable("FeatureFlagUsages".ToLower()).ForeignColumn("FeatureFlagId".ToLower())
				.ToTable("FeatureFlags".ToLower()).PrimaryColumn("FeatureFlagId".ToLower());

			Create.ForeignKey("FK_FeatureFlagUsages_Departments".ToLower())
				.FromTable("FeatureFlagUsages".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());

			Create.Index("IX_FeatureFlagUsages_Flag_Date".ToLower())
				.OnTable("FeatureFlagUsages".ToLower())
				.OnColumn("FeatureFlagId".ToLower()).Ascending()
				.OnColumn("UsageDate".ToLower()).Ascending();
		}

		public override void Down()
		{
			Delete.ForeignKey("FK_FeatureFlagUsages_Departments".ToLower()).OnTable("FeatureFlagUsages".ToLower());
			Delete.ForeignKey("FK_FeatureFlagUsages_FeatureFlags".ToLower()).OnTable("FeatureFlagUsages".ToLower());
			Delete.Table("FeatureFlagUsages".ToLower());

			Delete.ForeignKey("FK_FeatureFlagPrerequisites_RequiredFeatureFlag".ToLower()).OnTable("FeatureFlagPrerequisites".ToLower());
			Delete.ForeignKey("FK_FeatureFlagPrerequisites_FeatureFlags".ToLower()).OnTable("FeatureFlagPrerequisites".ToLower());
			Delete.Table("FeatureFlagPrerequisites".ToLower());

			Delete.ForeignKey("FK_FeatureFlagTargetingRules_FeatureFlags".ToLower()).OnTable("FeatureFlagTargetingRules".ToLower());
			Delete.Table("FeatureFlagTargetingRules".ToLower());

			Delete.ForeignKey("FK_FeatureFlagOverrides_Departments".ToLower()).OnTable("FeatureFlagOverrides".ToLower());
			Delete.ForeignKey("FK_FeatureFlagOverrides_FeatureFlags".ToLower()).OnTable("FeatureFlagOverrides".ToLower());
			Delete.Table("FeatureFlagOverrides".ToLower());

			Delete.Table("FeatureFlags".ToLower());
		}
	}
}

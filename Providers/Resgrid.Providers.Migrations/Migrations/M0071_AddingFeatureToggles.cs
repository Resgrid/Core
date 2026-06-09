using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(71)]
	public class M0071_AddingFeatureToggles : Migration
	{
		public override void Up()
		{
			// Each table (with its foreign keys and indexes) is guarded so the migration is safe to
			// re-run / safe on databases where a prior partial apply already created some of them.

			// FeatureFlags - system-wide flag definitions with a global default.
			if (!Schema.Table("FeatureFlags").Exists())
			{
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
			}

			// FeatureFlagOverrides - per-department override of a flag's value.
			if (!Schema.Table("FeatureFlagOverrides").Exists())
			{
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

				Create.ForeignKey("FK_FeatureFlagOverrides_FeatureFlags")
					.FromTable("FeatureFlagOverrides").ForeignColumn("FeatureFlagId")
					.ToTable("FeatureFlags").PrimaryColumn("FeatureFlagId");

				Create.ForeignKey("FK_FeatureFlagOverrides_Departments")
					.FromTable("FeatureFlagOverrides").ForeignColumn("DepartmentId")
					.ToTable("Departments").PrimaryColumn("DepartmentId");

				Create.Index("UX_FeatureFlagOverrides_Flag_Department")
					.OnTable("FeatureFlagOverrides")
					.OnColumn("FeatureFlagId").Ascending()
					.OnColumn("DepartmentId").Ascending()
					.WithOptions().Unique();
			}

			// FeatureFlagTargetingRules - attribute/segment targeting rules.
			if (!Schema.Table("FeatureFlagTargetingRules").Exists())
			{
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

				Create.ForeignKey("FK_FeatureFlagTargetingRules_FeatureFlags")
					.FromTable("FeatureFlagTargetingRules").ForeignColumn("FeatureFlagId")
					.ToTable("FeatureFlags").PrimaryColumn("FeatureFlagId");

				Create.Index("IX_FeatureFlagTargetingRules_Flag")
					.OnTable("FeatureFlagTargetingRules")
					.OnColumn("FeatureFlagId").Ascending();
			}

			// FeatureFlagPrerequisites - flag dependency edges.
			if (!Schema.Table("FeatureFlagPrerequisites").Exists())
			{
				Create.Table("FeatureFlagPrerequisites")
					.WithColumn("FeatureFlagPrerequisiteId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("FeatureFlagId").AsInt32().NotNullable()
					.WithColumn("RequiredFeatureFlagId").AsInt32().NotNullable()
					.WithColumn("RequiredValue").AsString(int.MaxValue).Nullable();

				Create.ForeignKey("FK_FeatureFlagPrerequisites_FeatureFlags")
					.FromTable("FeatureFlagPrerequisites").ForeignColumn("FeatureFlagId")
					.ToTable("FeatureFlags").PrimaryColumn("FeatureFlagId");

				Create.ForeignKey("FK_FeatureFlagPrerequisites_RequiredFeatureFlag")
					.FromTable("FeatureFlagPrerequisites").ForeignColumn("RequiredFeatureFlagId")
					.ToTable("FeatureFlags").PrimaryColumn("FeatureFlagId");

				Create.Index("IX_FeatureFlagPrerequisites_Flag")
					.OnTable("FeatureFlagPrerequisites")
					.OnColumn("FeatureFlagId").Ascending();
			}

			// FeatureFlagUsages - aggregated daily evaluation counts (append-only flushes).
			if (!Schema.Table("FeatureFlagUsages").Exists())
			{
				Create.Table("FeatureFlagUsages")
					.WithColumn("FeatureFlagUsageId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("FeatureFlagId").AsInt32().NotNullable()
					.WithColumn("DepartmentId").AsInt32().Nullable()
					.WithColumn("UsageDate").AsDateTime2().NotNullable()
					.WithColumn("EvaluationCount").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("EnabledCount").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("DisabledCount").AsInt64().NotNullable().WithDefaultValue(0);

				Create.ForeignKey("FK_FeatureFlagUsages_FeatureFlags")
					.FromTable("FeatureFlagUsages").ForeignColumn("FeatureFlagId")
					.ToTable("FeatureFlags").PrimaryColumn("FeatureFlagId");

				Create.ForeignKey("FK_FeatureFlagUsages_Departments")
					.FromTable("FeatureFlagUsages").ForeignColumn("DepartmentId")
					.ToTable("Departments").PrimaryColumn("DepartmentId");

				Create.Index("IX_FeatureFlagUsages_Flag_Date")
					.OnTable("FeatureFlagUsages")
					.OnColumn("FeatureFlagId").Ascending()
					.OnColumn("UsageDate").Ascending();
			}
		}

		public override void Down()
		{
			Delete.ForeignKey("FK_FeatureFlagUsages_Departments").OnTable("FeatureFlagUsages");
			Delete.ForeignKey("FK_FeatureFlagUsages_FeatureFlags").OnTable("FeatureFlagUsages");
			Delete.Table("FeatureFlagUsages");

			Delete.ForeignKey("FK_FeatureFlagPrerequisites_RequiredFeatureFlag").OnTable("FeatureFlagPrerequisites");
			Delete.ForeignKey("FK_FeatureFlagPrerequisites_FeatureFlags").OnTable("FeatureFlagPrerequisites");
			Delete.Table("FeatureFlagPrerequisites");

			Delete.ForeignKey("FK_FeatureFlagTargetingRules_FeatureFlags").OnTable("FeatureFlagTargetingRules");
			Delete.Table("FeatureFlagTargetingRules");

			Delete.ForeignKey("FK_FeatureFlagOverrides_Departments").OnTable("FeatureFlagOverrides");
			Delete.ForeignKey("FK_FeatureFlagOverrides_FeatureFlags").OnTable("FeatureFlagOverrides");
			Delete.Table("FeatureFlagOverrides");

			Delete.Table("FeatureFlags");
		}
	}
}

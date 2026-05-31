using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.FeatureToggles
{
	#region Poll / evaluation results

	/// <summary>Resolved state of a single feature toggle for the calling department.</summary>
	public class FeatureToggleData
	{
		public string Key { get; set; }
		public bool Enabled { get; set; }
		public string Value { get; set; }
		public string ValueType { get; set; }
		public string Source { get; set; }
		public int? MatchedRuleId { get; set; }
	}

	public class FeatureToggleResult : StandardApiResponseV4Base
	{
		public FeatureToggleData Data { get; set; } = new FeatureToggleData();
	}

	public class FeatureTogglesResult : StandardApiResponseV4Base
	{
		public List<FeatureToggleData> Data { get; set; } = new List<FeatureToggleData>();

		/// <summary>Stable hash of the full resolved state (also returned as the ETag header).</summary>
		public string StateHash { get; set; }
	}

	#endregion

	#region Flag management results

	public class FeatureFlagData
	{
		public string FeatureFlagId { get; set; }
		public string Key { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string Category { get; set; }
		public string Tags { get; set; }
		public int FlagType { get; set; }
		public string FlagTypeName { get; set; }
		public bool IsEnabledGlobally { get; set; }
		public string DefaultValue { get; set; }
		public string OffValue { get; set; }
		public int? RolloutPercentage { get; set; }
		public int? MinimumPlanType { get; set; }
		public int? Environment { get; set; }
		public string EnableOn { get; set; }
		public string DisableOn { get; set; }
		public bool IsArchived { get; set; }
		public bool IsPermanent { get; set; }
		public string LastEvaluatedOn { get; set; }
		public string CreatedOn { get; set; }
		public string UpdatedOn { get; set; }
	}

	public class FeatureFlagResult : StandardApiResponseV4Base
	{
		public FeatureFlagData Data { get; set; } = new FeatureFlagData();
	}

	public class FeatureFlagsResult : StandardApiResponseV4Base
	{
		public List<FeatureFlagData> Data { get; set; } = new List<FeatureFlagData>();
	}

	#endregion

	#region Override results

	public class FeatureFlagOverrideData
	{
		public string FeatureFlagOverrideId { get; set; }
		public string FeatureFlagId { get; set; }
		public int DepartmentId { get; set; }
		public bool IsEnabled { get; set; }
		public string Value { get; set; }
		public string Reason { get; set; }
		public string ExpiresOn { get; set; }
	}

	public class FeatureFlagOverrideResult : StandardApiResponseV4Base
	{
		public FeatureFlagOverrideData Data { get; set; }
	}

	public class FeatureFlagOverridesResult : StandardApiResponseV4Base
	{
		public List<FeatureFlagOverrideData> Data { get; set; } = new List<FeatureFlagOverrideData>();
	}

	#endregion

	#region Targeting rule & prerequisite results

	public class FeatureFlagTargetingRuleData
	{
		public string FeatureFlagTargetingRuleId { get; set; }
		public string FeatureFlagId { get; set; }
		public int Priority { get; set; }
		public int AttributeType { get; set; }
		public int OperatorType { get; set; }
		public string ComparisonValue { get; set; }
		public bool ResultEnabled { get; set; }
		public string ResultValue { get; set; }
		public int? RolloutPercentage { get; set; }
	}

	public class FeatureFlagTargetingRulesResult : StandardApiResponseV4Base
	{
		public List<FeatureFlagTargetingRuleData> Data { get; set; } = new List<FeatureFlagTargetingRuleData>();
	}

	public class FeatureFlagPrerequisiteData
	{
		public string FeatureFlagPrerequisiteId { get; set; }
		public string FeatureFlagId { get; set; }
		public string RequiredFeatureFlagId { get; set; }
		public string RequiredValue { get; set; }
	}

	public class FeatureFlagPrerequisitesResult : StandardApiResponseV4Base
	{
		public List<FeatureFlagPrerequisiteData> Data { get; set; } = new List<FeatureFlagPrerequisiteData>();
	}

	#endregion

	#region Analytics results

	public class FeatureFlagUsageData
	{
		public string FeatureFlagId { get; set; }
		public int? DepartmentId { get; set; }
		public string UsageDate { get; set; }
		public long EvaluationCount { get; set; }
		public long EnabledCount { get; set; }
		public long DisabledCount { get; set; }
	}

	public class FeatureFlagUsageResult : StandardApiResponseV4Base
	{
		public List<FeatureFlagUsageData> Data { get; set; } = new List<FeatureFlagUsageData>();
	}

	#endregion
}

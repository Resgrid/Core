using System;

namespace Resgrid.Web.Services.Models.v4.FeatureToggles
{
	/// <summary>Create or update a feature flag definition (matched by Key).</summary>
	public class SaveFeatureFlagInput
	{
		public string Key { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string Category { get; set; }
		public string Tags { get; set; }
		public int FlagType { get; set; }
		public bool IsEnabledGlobally { get; set; }
		public string DefaultValue { get; set; }
		public string OffValue { get; set; }
		public int? RolloutPercentage { get; set; }
		public int? MinimumPlanType { get; set; }
		public int? Environment { get; set; }
		public DateTime? EnableOn { get; set; }
		public DateTime? DisableOn { get; set; }
		public bool IsArchived { get; set; }
		public bool IsPermanent { get; set; }
	}

	public class SetGlobalEnabledInput
	{
		public string Key { get; set; }
		public bool Enabled { get; set; }
	}

	public class SetRolloutInput
	{
		public string Key { get; set; }
		public int Percentage { get; set; }
	}

	public class SetFeatureFlagOverrideInput
	{
		public string Key { get; set; }
		/// <summary>Target department. Honored only for system administrators; otherwise the caller's department is used.</summary>
		public int? DepartmentId { get; set; }
		public bool IsEnabled { get; set; }
		public string Value { get; set; }
		public string Reason { get; set; }
		public DateTime? ExpiresOn { get; set; }
	}

	public class SaveTargetingRuleInput
	{
		public int FeatureFlagTargetingRuleId { get; set; }
		public string Key { get; set; }
		public int Priority { get; set; }
		public int AttributeType { get; set; }
		public int OperatorType { get; set; }
		public string ComparisonValue { get; set; }
		public bool ResultEnabled { get; set; }
		public string ResultValue { get; set; }
		public int? RolloutPercentage { get; set; }
	}

	public class AddPrerequisiteInput
	{
		public string Key { get; set; }
		public string RequiredKey { get; set; }
		public string RequiredValue { get; set; }
	}
}

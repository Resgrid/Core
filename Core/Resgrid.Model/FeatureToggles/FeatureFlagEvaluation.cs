namespace Resgrid.Model
{
	/// <summary>
	/// The resolved state of a feature flag for a specific department, including the reason it
	/// resolved that way. Returned by the feature toggle service and surfaced by the poll API.
	/// </summary>
	public class FeatureFlagEvaluation
	{
		public int FeatureFlagId { get; set; }

		/// <summary>The flag's stable key.</summary>
		public string Key { get; set; }

		public bool IsEnabled { get; set; }

		/// <summary>Resolved value (for multivariate flags); for boolean flags mirrors IsEnabled.</summary>
		public string Value { get; set; }

		public FeatureFlagValueTypes ValueType { get; set; }

		/// <summary>Which rule in the evaluation ladder decided the result.</summary>
		public FeatureFlagEvaluationSource Source { get; set; }

		/// <summary>The targeting rule id when <see cref="Source"/> is TargetingRule.</summary>
		public int? MatchedRuleId { get; set; }
	}
}

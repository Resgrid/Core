namespace Resgrid.Model
{
	/// <summary>
	/// The comparison applied by a targeting rule between a department attribute and the rule's
	/// ComparisonValue. In/NotIn treat ComparisonValue as a comma-separated list.
	/// </summary>
	public enum FeatureFlagOperatorTypes
	{
		Equals = 0,
		NotEquals = 1,
		In = 2,
		NotIn = 3,
		GreaterThan = 4,
		GreaterThanOrEqual = 5,
		LessThan = 6,
		LessThanOrEqual = 7,
		Contains = 8,
	}
}

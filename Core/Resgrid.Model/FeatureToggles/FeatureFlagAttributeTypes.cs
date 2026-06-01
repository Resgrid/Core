namespace Resgrid.Model
{
	/// <summary>
	/// The department attribute a targeting rule compares against. Resolved lazily (and cached) during
	/// evaluation from the department, its subscription plan, and personnel counts.
	/// </summary>
	public enum FeatureFlagAttributeTypes
	{
		/// <summary>The department's current subscription plan id.</summary>
		PlanType = 0,
		/// <summary>The department's country/region.</summary>
		Country = 1,
		/// <summary>The department's active personnel count.</summary>
		PersonnelCount = 2,
		/// <summary>The department's type (e.g. fire, ems).</summary>
		DepartmentType = 3,
		/// <summary>The department's creation date.</summary>
		CreatedDate = 4,
		/// <summary>The department id itself (allow/deny lists).</summary>
		DepartmentId = 5,
		/// <summary>A caller-supplied custom context value (matched by ComparisonValue key).</summary>
		Custom = 6,
	}
}

namespace Resgrid.Model
{
	/// <summary>
	/// Explains which rule in the evaluation ladder decided a flag's value for a department. Returned
	/// on every evaluation so the poll API and logs can show LaunchDarkly-style "evaluation reasons".
	/// </summary>
	public enum FeatureFlagEvaluationSource
	{
		/// <summary>No flag with the requested key exists.</summary>
		NotFound = 0,
		/// <summary>Value came from a code-registered default in FeatureFlagsConfig.</summary>
		CodeDefault = 1,
		/// <summary>The whole feature-toggle subsystem is disabled via config.</summary>
		SubsystemDisabled = 2,
		/// <summary>The flag is archived.</summary>
		Archived = 3,
		/// <summary>Decided by the flag's scheduled enable/disable window.</summary>
		Schedule = 4,
		/// <summary>A prerequisite flag was not satisfied.</summary>
		Prerequisite = 5,
		/// <summary>An explicit per-department override decided the value.</summary>
		Override = 6,
		/// <summary>The department's subscription plan did not meet the flag's minimum plan.</summary>
		PlanGate = 7,
		/// <summary>A matching attribute/segment targeting rule decided the value.</summary>
		TargetingRule = 8,
		/// <summary>Decided by the global default and the percentage rollout bucket.</summary>
		GlobalRollout = 9,
		/// <summary>Decided by the global default (fully on/off, no rollout in effect).</summary>
		GlobalDefault = 10,
	}
}

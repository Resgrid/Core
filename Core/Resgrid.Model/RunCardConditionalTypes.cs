namespace Resgrid.Model
{
	/// <summary>
	/// Condition kinds a run card decision node can test (conditional run cards). Declarative only: a kind is added in
	/// code with its own evaluator, never as a script. Stored on <see cref="RunCardDecisionNode.ConditionType"/>, so
	/// values are never renumbered.
	/// </summary>
	public enum RunCardConditionTypes
	{
		None = 0,

		/// <summary>The subject's pick arrives at least ThresholdSeconds before the reference's pick (turnout + travel).</summary>
		ArrivesBefore = 1,

		/// <summary>At least RequiredCount resources of the subject can arrive in under ThresholdSeconds (turnout + travel).</summary>
		AvailableWithin = 2
	}

	/// <summary>Which resources, by status meaning, a row or condition operand accepts.</summary>
	public enum RunCardStatusFilters
	{
		/// <summary>Every dispatchable resource (today's behaviour).</summary>
		AnyAvailable = 0,

		/// <summary>Units whose current status is a custom status with the In Quarters base type.</summary>
		InQuarters = 1,

		/// <summary>Dispatchable units that are not In Quarters (built-in Available, Delayed-class statuses, returning, repositioning).</summary>
		AwayFromQuarters = 2,

		/// <summary>Dispatchable units whose status base type is in the configured set (StatusBaseTypes, comma separated ActionBaseTypes).</summary>
		BaseTypes = 3
	}

	public enum RunCardDecisionNodeKinds
	{
		Condition = 1,
		Outcome = 2
	}

	/// <summary>What a run card target matches. Stored on <see cref="RunCardTarget.TargetKind"/>; never renumber.</summary>
	public enum RunCardTargetKinds
	{
		/// <summary>Units of one unit type (TargetRefId = UnitTypeId).</summary>
		UnitType = 1,

		/// <summary>Units whose type has this meaning (TargetRefId = UnitTypeClasses value).</summary>
		UnitClass = 2,

		/// <summary>Every dispatchable unit.</summary>
		AnyUnit = 3,

		/// <summary>People holding this role (TargetRefId = PersonnelRoleId).</summary>
		PersonnelRole = 4,

		/// <summary>Every dispatchable person.</summary>
		AnyPerson = 5
	}

	/// <summary>What a <see cref="RunCardTarget"/> row belongs to. Stored; never renumber.</summary>
	public enum RunCardTargetOwners
	{
		PackageRow = 1,
		NodeSubject = 2,
		NodeReference = 3
	}

	/// <summary>How a package row is filled.</summary>
	public enum RequirementFillModes
	{
		/// <summary>Pick RequiredCount resources matching the row's targets.</summary>
		Count = 1,

		/// <summary>
		/// Filled after every other row: adds the earliest-arriving resource matching the targets only when no other row
		/// already picked it (and nothing matching is already on the call). Complements a package; never replaces a row.
		/// </summary>
		EnsureFirstArriving = 2
	}

	/// <summary>Where a pinned run card decision came from.</summary>
	public enum RunCardDecisionSources
	{
		/// <summary>Evaluated when the call was saved (auto-dispatch, inbound channels).</summary>
		SaveTime = 1,

		/// <summary>The outcome the dispatcher saw on the New Call preview, posted with the form.</summary>
		DispatcherPreview = 2,

		/// <summary>A dispatcher re-evaluated the call and applied the fresh outcome.</summary>
		Reevaluated = 3
	}

	/// <summary>How an outstanding requirement was cleared. Stored; never renumber.</summary>
	public enum OutstandingRequirementResolutions
	{
		Open = 0,

		/// <summary>A matching resource was dispatched to the call.</summary>
		Dispatched = 1,

		/// <summary>A freed unit was dispatched to the call automatically (department or card setting).</summary>
		AutoDispatched = 2,

		/// <summary>A person closed or cancelled the call.</summary>
		CallClosed = 3,

		/// <summary>A person dismissed the requirement.</summary>
		Dismissed = 4
	}

	/// <summary>Which table a recommendation's requirement id refers to.</summary>
	public enum RequirementScopes
	{
		/// <summary>RunCardUnitRequirements / RunCardRoleRequirements (fixed level rows).</summary>
		Level = 0,

		/// <summary>ResponsePackageRows.</summary>
		Package = 1
	}

	/// <summary>What the chosen outcome asks for.</summary>
	public enum RecommendationOutcomes
	{
		/// <summary>Resources are recommended (the usual case).</summary>
		Dispatch = 0,

		/// <summary>Advice only; nothing is dispatched.</summary>
		Advice = 1
	}

	/// <summary>Result of one condition on the decision path.</summary>
	public enum RunCardConditionResults
	{
		No = 0,
		Yes = 1,

		/// <summary>The determinate answer could flip because a resource matching an operand was left out for data quality.</summary>
		Uncertain = 2
	}

	/// <summary>Fallback step taken for a row that the configured fill left short.</summary>
	public enum RequirementFallbackSteps
	{
		/// <summary>Filled with available resources of the same targets, status filter dropped.</summary>
		Relaxed = 1,

		/// <summary>Filled from the row's substitute package.</summary>
		Substitute = 2,

		/// <summary>Still short after every step.</summary>
		Shortfall = 3
	}

	/// <summary>Where the time compared by a condition came from.</summary>
	public enum ResponseTimeSources
	{
		Estimated = 0,
		Routed = 1
	}
}

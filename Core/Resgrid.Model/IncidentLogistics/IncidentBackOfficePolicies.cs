using System;
using System.Collections.Generic;

namespace Resgrid.Model.IncidentLogistics
{
	// Department policies of the Incident Back Office (registry §4C settings 85-91, 96-97, 120), stored as JSON in
	// DepartmentSettings. A missing row is the documented default below; every number is configurable, never hard-coded
	// into a rule (plan §9.5: "configurable, never hard-coded"; generic over customer-specific).

	/// <summary>Setting 85 (LI-A).</summary>
	public sealed class IncidentBackOfficeDefaults
	{
		/// <summary>An IC/Dispatch source older than this greys its tiles (plan §3.2 consequence 3, §13.4).</summary>
		public int SnapshotStalenessMinutes { get; set; } = 15;
		/// <summary>ISO 4217 currency for new activations; blank = derived from the department's country.</summary>
		public string DefaultCurrency { get; set; }
		/// <summary>metric / customary for new activations; blank = derived from the department's country.</summary>
		public string DefaultMeasurementSystem { get; set; }
		/// <summary>Default length of a new operational period, in hours.</summary>
		public int OperationalPeriodHours { get; set; } = 12;
		/// <summary>Exercises never send to external participants or vendors unless this is on (D16 default off).</summary>
		public bool ExercisesMayNotifyExternal { get; set; }
	}

	/// <summary>Setting 86 (LI-A, plan §9.5).</summary>
	public sealed class IncidentEnrollmentPolicy
	{
		/// <summary><see cref="IncidentEnrollmentPolicies"/>; default Suggest.</summary>
		public int Mode { get; set; } = (int)IncidentEnrollmentPolicies.Suggest;
		/// <summary>Count unenrolled operational subjects as a separate, labelled band in feeding, sleeping and water demand.</summary>
		public bool UnregisteredForecastBand { get; set; } = true;
		/// <summary>Trigger 199 fires when the queue exceeds this count …</summary>
		public int BacklogCount { get; set; } = 25;
		/// <summary>… or its oldest subject has waited this long.</summary>
		public int BacklogMinutes { get; set; } = 120;
	}

	/// <summary>Setting 87 (LI-A, plan §13.8): default position codes per native notice, resolved to current holders at send time.</summary>
	public sealed class IncidentNotificationRoutingPolicy
	{
		public Dictionary<int, List<string>> Positions { get; set; } = new Dictionary<int, List<string>>();

		public static IReadOnlyDictionary<int, string[]> Defaults { get; } = new Dictionary<int, string[]>
		{
			[35] = new string[0],
			[36] = new[] { "IC", "PSC" },
			[37] = new[] { "LSC", "OSC", "FDUL", "FACL" },
			[38] = new[] { "LSC", "OSC", "FDUL", "FACL" },
			[39] = new string[0],
			[40] = new string[0],
			[41] = new[] { "FSC" },
			[42] = new[] { "FSC", "PROC" },
			[43] = new[] { "COMP" },
			[44] = new[] { "IC", "DOCL", "FSC" },
			[49] = new[] { "LSC", "OSC", "RESL", "DMOB" }
		};

		public IReadOnlyList<string> For(int eventType)
		{
			if (Positions != null && Positions.TryGetValue(eventType, out var configured) && configured != null && configured.Count > 0)
				return configured;
			return Defaults.TryGetValue(eventType, out var defaults) ? defaults : Array.Empty<string>();
		}
	}

	/// <summary>Setting 88 (LI-B, plan §9.8, §9.17.2): forecast coefficients and the predicted-timeout policy.</summary>
	public sealed class IncidentForecastPolicy
	{
		public decimal MealContingencyPercent { get; set; } = 10m;
		/// <summary>Litres per person per day when no IncidentDemandCoefficient row overrides it.</summary>
		public decimal DrinkingWaterLitresPerPersonDay { get; set; } = 7.5m;
		public decimal WaterContingencyPercent { get; set; } = 15m;
		public decimal BedReservePercent { get; set; } = 5m;
		/// <summary>Predicted timeouts reduce forecasts only when the department flips this (plan §9.17.2 point 5).</summary>
		public bool PredictedTimeoutsReduceForecast { get; set; }
		/// <summary>Typical fill-plus-travel lead for a like-for-like replacement, in days (order-by date).</summary>
		public int ReplacementLeadDays { get; set; } = 3;
		/// <summary>Trigger 122 lead window before an ETA, in minutes.</summary>
		public int ArrivalLeadMinutes { get; set; } = 120;
		/// <summary>Trigger 192 lead window before a timeout date, in days.</summary>
		public int TimeoutLeadDays { get; set; } = 3;
		/// <summary>Trigger 129 lead window before a meal cutoff, in minutes.</summary>
		public int MealCutoffLeadMinutes { get; set; } = 180;
		public decimal FacilityThresholdPercent { get; set; } = 90m;
		public int WaterDaysOfSupplyThreshold { get; set; } = 2;
	}

	/// <summary>Setting 89 (LI-B, plan §9.5, §9.13): maximum time allowance and work/rest rule pack.</summary>
	public sealed class IncidentAssignmentRulePolicy
	{
		/// <summary>Key of the rule pack: "federal-14-plus-travel", "calfire-rotation" or "department".</summary>
		public string RulePack { get; set; } = "federal-14-plus-travel";
		public int MaxAssignmentDays { get; set; } = 14;
		public int TravelDays { get; set; } = 2;
		public int MaxExtensionDays { get; set; } = 7;
		public decimal MaxWorkHoursPerShift { get; set; } = 16m;
		/// <summary>Hours of rest required per hour of work (1 hour rest for every 2 hours worked = 0.5).</summary>
		public decimal RestPerWorkHour { get; set; } = 0.5m;
	}

	/// <summary>Setting 90 (LI-B, plan §9.12): verification interval and capacity holds.</summary>
	public sealed class IncidentVendorPolicy
	{
		public int VerificationIntervalDays { get; set; } = 365;
		public int HoldExpiryHours { get; set; } = 24;
		public int ComplianceLeadDays { get; set; } = 30;
		public int HoldLeadHours { get; set; } = 6;
	}

	/// <summary>Setting 91 (LI-B, plan §9.28, D17).</summary>
	public sealed class IncidentSelfServicePolicy
	{
		/// <summary>Days a link outlives the subject's assignment window or engagement end.</summary>
		public int LinkLifetimeDays { get; set; } = 14;
		/// <summary>A one-time code to the contact method on file before lodging or travel is shown (D17).</summary>
		public bool RequireOneTimeCodeForLodging { get; set; } = true;
		/// <summary>Vendor links only for a verified vendor Contact.</summary>
		public bool RequireVerifiedVendorContact { get; set; }
	}

	/// <summary>Setting 96 (LI-C, plan §9.13, §9.27, §15.1).</summary>
	public sealed class IncidentFinancePolicy
	{
		/// <summary>Single-person operation: the same user may request, approve, commit, receive and accept — only with a recorded acknowledgment.</summary>
		public bool AllowSinglePersonOperation { get; set; }
		public string SinglePersonAcknowledgedByUserId { get; set; }
		public DateTime? SinglePersonAcknowledgedOn { get; set; }
		/// <summary>Extra sender domains allowed into the intake inbox beyond participants, engaged vendors and members.</summary>
		public List<string> IntakeAllowedDomains { get; set; } = new List<string>();
		public decimal BudgetThresholdPercent { get; set; } = 80m;
		public decimal AgreementCeilingThresholdPercent { get; set; } = 85m;
		public decimal DailyBurnThreshold { get; set; }
		public int IntakeBacklogCount { get; set; } = 50;
		public int IntakeBacklogHours { get; set; } = 24;
		public int ClaimDeadlineLeadDays { get; set; } = 7;
		public int RecoveryDeadlineLeadDays { get; set; } = 14;
		public decimal SplitPurchaseWindowDays { get; set; } = 3m;
		public decimal MicroPurchaseThreshold { get; set; } = 10000m;
	}

	/// <summary>Setting 97 (LI-C, plan §18).</summary>
	public sealed class IncidentRetentionPolicy
	{
		public int SnapshotDaysAfterClose { get; set; } = 90;
		public int LedgerYearsAfterClose { get; set; } = 7;
		public int SelfServiceLinkDaysAfterExpiry { get; set; } = 30;
	}

	/// <summary>Setting 120 (LI-D, plan §9.25.3a).</summary>
	public sealed class CoordinationBoardDefaults
	{
		public int DefaultStalenessMinutes { get; set; } = 60;
		public bool AlarmRoadClosures { get; set; } = true;
		public bool AlarmShelterCapacity { get; set; } = true;
		public int ShareGrantDays { get; set; } = 14;
		public int ItemDueLeadMinutes { get; set; } = 60;
	}
}

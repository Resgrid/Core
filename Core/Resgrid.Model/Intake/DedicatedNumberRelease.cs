using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Resgrid.Config;

namespace Resgrid.Model
{
	/// <summary>
	/// Whether a department is paying for its dedicated number (call-intake-and-ai-destinations-plan.md §8 D10): the Dedicated Number
	/// add-on's paid interval and, while the add-on is held, whether the base plan is an active paid entity plan. A null part is unknown
	/// (billing unconfigured or unreachable, an unreadable answer); the number lifecycle acts only on a known state.
	/// </summary>
	public sealed class DedicatedNumberPaymentState
	{
		/// <summary>True: a paid add-on interval is current. False: known to have none. Null: unknown.</summary>
		public bool? AddonActive { get; init; }

		/// <summary>True: the plan is an active paid entity plan. False: known not to be. Null: unknown or not read.</summary>
		public bool? PaidEntityPlan { get; init; }

		/// <summary>The add-on has no paid interval (cancelled or unpaid).</summary>
		public bool AddonStopped => AddonActive == false;

		/// <summary>The add-on is still paid but the plan it needs is known not to be an active paid entity plan.</summary>
		public bool PlanStopped => AddonActive == true && PaidEntityPlan == false;

		/// <summary>Known to have stopped paying (T0 is when the lifecycle first sees this).</summary>
		public bool Stopped => AddonStopped || PlanStopped;

		/// <summary>Known to be paying: the add-on is paid and the plan is an active paid entity plan.</summary>
		public bool Paying => AddonActive == true && PaidEntityPlan == true;

		/// <summary>Neither known paying nor known stopped: the lifecycle changes nothing.</summary>
		public bool IsUnknown => !Stopped && !Paying;
	}

	/// <summary>What an admin cancelled in Resgrid that will stop the department paying for its dedicated number (plan §8 D12).</summary>
	public enum DedicatedNumberCancellations
	{
		/// <summary>The Dedicated Number add-on's renewal (it ends at the end of the paid period).</summary>
		AddonRenewal = 1,

		/// <summary>The department's base plan, which the add-on needs.</summary>
		Plan = 2
	}

	/// <summary>
	/// When a department that stopped paying (T0, <see cref="DepartmentPhoneNumber.AddonInactiveSince"/>) loses intake on its dedicated
	/// number (T0 + <see cref="DedicatedNumberAddonConfig.LapseDelayHours"/>) and when the number is released to the carrier
	/// (T0 + <see cref="DedicatedNumberAddonConfig.ReleaseGraceDays"/>, or the Lapsed row's stored ReleaseAfter), plus the release warnings
	/// already sent. All times UTC.
	/// </summary>
	public sealed class DedicatedNumberReleaseSchedule
	{
		/// <summary>The days-before-release warning stages, in order: the first (largest) configured day count is FifteenDays, and so on.</summary>
		public static readonly DedicatedNumberReleaseWarnings[] DayStageFlags =
		{
			DedicatedNumberReleaseWarnings.FifteenDays, DedicatedNumberReleaseWarnings.FiveDays, DedicatedNumberReleaseWarnings.OneDay
		};

		private static readonly int[] DefaultWarningDays = { 15, 5, 1 };

		/// <summary>T0; null for a Lapsed row from before T0 was recorded.</summary>
		public DateTime? StoppedOn { get; init; }

		/// <summary>When texts and calls stop (or stopped) being taken; null when T0 is unknown.</summary>
		public DateTime? IntakeStopsOn { get; init; }

		/// <summary>The number is Lapsed: intake has already stopped.</summary>
		public bool IntakeStopped { get; init; }

		public DateTime ReleaseOn { get; init; }

		public DedicatedNumberReleaseWarnings WarningsSent { get; init; }

		public DateTime? LastWarningOn { get; init; }

		public static TimeSpan LapseDelay => TimeSpan.FromHours(Math.Max(0, DedicatedNumberAddonConfig.LapseDelayHours));

		public static TimeSpan Grace => TimeSpan.FromDays(Math.Max(0, DedicatedNumberAddonConfig.ReleaseGraceDays));

		/// <summary>
		/// The schedule of a held number whose department stopped paying: an Active number with T0 set, or any Lapsed number. Null for a
		/// paying Active number and for every other status.
		/// </summary>
		public static DedicatedNumberReleaseSchedule For(DepartmentPhoneNumber row)
		{
			if (row == null)
				return null;

			var lapsed = row.Status == (int)DepartmentPhoneNumberStatuses.Lapsed;
			var stopped = row.AddonInactiveSince;
			if (!lapsed && (row.Status != (int)DepartmentPhoneNumberStatuses.Active || !stopped.HasValue))
				return null;

			var release = lapsed && row.ReleaseAfter.HasValue ? row.ReleaseAfter : stopped?.Add(Grace);
			if (!release.HasValue)
				return null;

			return new DedicatedNumberReleaseSchedule
			{
				StoppedOn = stopped,
				IntakeStopsOn = stopped?.Add(LapseDelay),
				IntakeStopped = lapsed,
				ReleaseOn = release.Value,
				WarningsSent = (DedicatedNumberReleaseWarnings)row.ReleaseWarningsSent,
				LastWarningOn = row.LastReleaseWarningOn
			};
		}

		/// <summary>
		/// <see cref="DedicatedNumberAddonConfig.ReleaseWarningDays"/> ("15,5,1") as day counts, largest first: whole days 1–365, duplicates
		/// and anything unreadable dropped, at most three (the ones closest to release). Nothing usable falls back to 15, 5 and 1.
		/// </summary>
		public static IReadOnlyList<int> WarningDays(string configured)
		{
			var days = (configured ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(v => int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : 0)
				.Where(d => d > 0 && d <= 365)
				.Distinct()
				.OrderBy(d => d)
				.Take(DayStageFlags.Length)
				.OrderByDescending(d => d)
				.ToList();

			return days.Count > 0 ? days : DefaultWarningDays;
		}

		/// <summary>Each days-before-release stage with its flag, largest day count first.</summary>
		public static IReadOnlyList<(DedicatedNumberReleaseWarnings Stage, int Days)> DayStages(string configured) =>
			WarningDays(configured).Select((days, i) => (DayStageFlags[i], days)).ToList();
	}
}

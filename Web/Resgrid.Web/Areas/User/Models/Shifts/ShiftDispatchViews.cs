using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Resgrid.Model;
using Resgrid.Web.Areas.User.Models.DispatcherDesk;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	/// <summary>
	/// The "Dispatchers on duty" section of the new and edit shift pages (M0324): whether the shift takes dispatchers, its
	/// dispatch staffing, its break plan and, for an Assigned shift, the standing dispatch roster.
	/// </summary>
	public class ShiftDispatchSectionView
	{
		public bool IsEnabled { get; set; }
		public bool IsDispatchOnly { get; set; }
		public int RequiredDispatchers { get; set; }

		/// <summary>0 = the department's minimum (<see cref="DepartmentMinimum"/>).</summary>
		public int MinimumOnDesk { get; set; }
		public bool AllowSelfScheduledBreaks { get; set; }

		[BindNever]
		public List<DispatcherBreakPlanRow> BreakPlan { get; set; } = new List<DispatcherBreakPlanRow>();

		/// <summary>People who can be on the standing roster (Dispatch app login), and anyone already on it.</summary>
		[BindNever]
		public List<ShiftDispatcherCandidate> Candidates { get; set; } = new List<ShiftDispatcherCandidate>();

		/// <summary>The standing roster is shown: an Assigned shift (always rendered on the new shift wizard, shown by type).</summary>
		[BindNever]
		public bool ShowStandingRoster { get; set; }

		[BindNever]
		public int DepartmentMinimum { get; set; }

		[BindNever]
		public bool DeskEnabled { get; set; }
	}

	public class ShiftDispatcherCandidate
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public bool OnRoster { get; set; }
		public bool IsSupervisor { get; set; }

		/// <summary>
		/// On the standing roster (and a supervisor) when the editor opened: posted back so the save changes only what this
		/// editor changed, never a dispatcher someone else added meanwhile.
		/// </summary>
		public bool WasOnRoster { get; set; }
		public bool WasSupervisor { get; set; }

		/// <summary>Still allowed to dispatch; a roster member who lost the permission drops off on the next save.</summary>
		public bool IsEligible { get; set; } = true;
	}

	/// <summary>The "Dispatchers on duty" block of the shift day page.</summary>
	public class ShiftDayDispatchView
	{
		public int ShiftDayId { get; set; }
		public bool IsDispatchOnly { get; set; }
		public int RequiredDispatchers { get; set; }
		public bool IsSignupShift { get; set; }
		public bool RequireApproval { get; set; }
		public bool IsOver { get; set; }

		/// <summary>Shift manager (the whole shift) or department admin: assigns, removes and reviews dispatchers for the day.</summary>
		public bool CanManage { get; set; }

		/// <summary>An eligible dispatcher not yet on the day of a Signup shift.</summary>
		public bool CanSignup { get; set; }

		public List<ShiftDayDispatcherRow> Rows { get; set; } = new List<ShiftDayDispatcherRow>();
		public List<ShiftDayOption> AssignOptions { get; set; } = new List<ShiftDayOption>();

		public int ApprovedCount => Rows.Count(x => !x.IsPending);
		public int OpenPositions => Math.Max(0, RequiredDispatchers - ApprovedCount);
		public bool CanAssign => CanManage && !IsOver && AssignOptions.Count > 0;
	}

	public class ShiftDayDispatcherRow
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public string ShiftDispatcherId { get; set; }
		public bool IsSupervisor { get; set; }
		public bool IsPending { get; set; }

		/// <summary><see cref="ShiftDispatcherSources"/>.</summary>
		public int Source { get; set; }
		public bool IsYou { get; set; }

		/// <summary>Department-local duty window.</summary>
		public DateTime Start { get; set; }
		public DateTime End { get; set; }

		public bool CanReview { get; set; }
		public bool CanRemove { get; set; }
		public bool CanWithdraw { get; set; }
	}

	/// <summary>A dispatcher on the On Duty page: their desk state, break and work.</summary>
	public class OnDutyDispatcherView
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public DispatcherDisplayStates State { get; set; }
		public bool IsOnDuty { get; set; }
		public bool IsSupervisor { get; set; }
		public string ShiftName { get; set; }
		public string DeviceKind { get; set; }
		public string AwayReason { get; set; }

		/// <summary><see cref="DispatcherBreakTypes"/> of the break in progress; null when not on break.</summary>
		public int? BreakType { get; set; }

		/// <summary>Department-local expected return.</summary>
		public DateTime? BreakDueBack { get; set; }
		public int? BreakMinutesLeft { get; set; }
		public int OverdueMinutes { get; set; }
		public int HeldItems { get; set; }
		public int OfferedItems { get; set; }
	}

	public class OnDutyDispatchersView
	{
		public bool DeskEnabled { get; set; }
		public int MinimumAvailable { get; set; }
		public int AvailableCount { get; set; }
		public bool BelowMinimum { get; set; }
		public int QueueCount { get; set; }
		public List<OnDutyDispatcherView> Dispatchers { get; set; } = new List<OnDutyDispatcherView>();
	}
}

using System;
using System.Collections.Generic;
using Resgrid.Model.Dashboard.Widgets.Operations;

namespace Resgrid.Model.Dashboard.Widgets.Personnel
{
	/// <summary>
	/// personnel.myStatus (plan §7.3): today's Actions and Staffing iboxes. The buttons post to the classic dashboard's own
	/// HomeController actions (no dashboard-specific write path). The viewer's stored staffing note is never sent back.
	/// </summary>
	public sealed class PersonnelMyStatusWidgetData
	{
		/// <summary>The department has its own personnel statuses (otherwise the built-in buttons are listed).</summary>
		public bool CustomStatuses { get; set; }

		public bool CustomStaffing { get; set; }

		/// <summary>The status buttons in display order.</summary>
		public List<PersonnelMyStatusOption> Statuses { get; set; } = new List<PersonnelMyStatusOption>();

		public List<PersonnelMyStatusOption> StaffingLevels { get; set; } = new List<PersonnelMyStatusOption>();

		/// <summary>The viewer's latest status; null when they never set one.</summary>
		public DashboardStatusChip CurrentStatus { get; set; }

		public DateTime? CurrentStatusOnUtc { get; set; }

		/// <summary>The call or station the current status points at, when it resolves.</summary>
		public string CurrentStatusDestination { get; set; }

		/// <summary>The viewer's latest staffing level; null when they never set one.</summary>
		public DashboardStatusChip CurrentStaffing { get; set; }

		public DateTime? CurrentStaffingOnUtc { get; set; }

		/// <summary>Stations the viewer can respond to (the department's station groups).</summary>
		public List<PersonnelMyStatusDestination> Stations { get; set; } = new List<PersonnelMyStatusDestination>();

		/// <summary>Dispatched active calls in the viewer's dispatch scope (newest first, up to 20) to respond to.</summary>
		public List<PersonnelMyStatusDestination> Calls { get; set; } = new List<PersonnelMyStatusDestination>();

		/// <summary>POST /User/Home/SetCustomAction — actionType (query) and, optionally, note (form body only) → 204.</summary>
		public DashboardWidgetEndpoint SetStatus { get; set; }

		/// <summary>POST /User/Home/UserRespondingToCall — callId (query; 0 = no specific call). Redirects to the dashboard (fetch follows; ignore the body).</summary>
		public DashboardWidgetEndpoint RespondToCall { get; set; }

		/// <summary>POST /User/Home/UserRespondingToStation — stationId (query; 0 = no specific station). Redirects like <see cref="RespondToCall"/>.</summary>
		public DashboardWidgetEndpoint RespondToStation { get; set; }

		/// <summary>POST /User/Home/SetCustomStaffing — userId (the viewer's own id) + staffingLevel (query) → 204. No note.</summary>
		public DashboardWidgetEndpoint SetStaffing { get; set; }

		/// <summary>
		/// POST /User/Home/SetUserState — form body CustomStaffingActive, UserState (custom id) or UserStateEnum (built-in
		/// value), StateNote. The only action that takes a staffing note (sealed through the protected write path in an ADP
		/// department); it answers with a redirect to the dashboard, not JSON.
		/// </summary>
		public DashboardWidgetEndpoint SetStaffingWithNote { get; set; }

		/// <summary>The viewer's own user id (the SetCustomStaffing userId parameter).</summary>
		public string UserId { get; set; }
	}

	public sealed class PersonnelMyStatusOption
	{
		/// <summary>The value to post: a built-in ActionTypes / UserStateTypes value or the department's CustomStateDetailId.</summary>
		public int Id { get; set; }

		public DashboardStatusChip Chip { get; set; }

		/// <summary>CustomStateDetailTypes: 0 none, 1 stations, 2 calls, 3 calls and stations (where the status can point).</summary>
		public int DetailType { get; set; }

		/// <summary>CustomStateNoteTypes: 0 none, 1 optional, 2 required.</summary>
		public int NoteType { get; set; }
	}

	public sealed class PersonnelMyStatusDestination
	{
		public int Id { get; set; }

		public string Name { get; set; }

		/// <summary>Call number (calls only).</summary>
		public string Number { get; set; }

		/// <summary>Priority colour (calls only).</summary>
		public string Color { get; set; }
	}
}

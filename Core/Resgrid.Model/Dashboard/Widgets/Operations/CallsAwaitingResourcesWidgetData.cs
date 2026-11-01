using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Operations
{
	/// <summary>calls.awaitingResources (plan §7.1): active calls still holding a required run-card resource, oldest and highest priority first.</summary>
	public sealed class CallsAwaitingResourcesWidgetData
	{
		public int TotalCount { get; set; }

		public List<CallsAwaitingResourcesCall> Calls { get; set; } = new List<CallsAwaitingResourcesCall>();

		/// <summary>POST /User/Dispatch/DispatchToAwaitingCall, form body callId + unitId → JSON { success, message, resolved } (Call_Update).</summary>
		public DashboardWidgetEndpoint DispatchUnit { get; set; }

		/// <summary>POST /User/Dispatch/DismissOutstandingRequirement, form body callId + requirementId + note → JSON { success, message } (Call_Update).</summary>
		public DashboardWidgetEndpoint Dismiss { get; set; }

		public string ListUrl { get; set; } = "/User/Dispatch/AwaitingResources";
	}

	public sealed class CallsAwaitingResourcesCall
	{
		public int CallId { get; set; }

		public string Number { get; set; }

		public string Name { get; set; }

		public string Address { get; set; }

		public DashboardCallPriority Priority { get; set; }

		public DateTime LoggedOnUtc { get; set; }

		/// <summary>When the oldest open requirement was raised.</summary>
		public DateTime? WaitingSinceUtc { get; set; }

		public List<CallsAwaitingResourcesRequirement> Requirements { get; set; } = new List<CallsAwaitingResourcesRequirement>();

		public string Url { get; set; }
	}

	public sealed class CallsAwaitingResourcesRequirement
	{
		public int RequirementId { get; set; }

		/// <summary>What is missing ("2 x Engine").</summary>
		public string Label { get; set; }

		public int RequiredCount { get; set; }

		public int ResolvedCount { get; set; }

		/// <summary>What went instead, when something was substituted.</summary>
		public string SentInstead { get; set; }

		public string ResponseCode { get; set; }

		public string ResponseCodeColor { get; set; }

		public DateTime CreatedOnUtc { get; set; }

		/// <summary>Units available now that would satisfy it, nearest first when known (up to three).</summary>
		public List<CallsAwaitingResourcesUnit> AvailableUnits { get; set; } = new List<CallsAwaitingResourcesUnit>();
	}

	public sealed class CallsAwaitingResourcesUnit
	{
		public int UnitId { get; set; }

		public string Name { get; set; }

		public string Type { get; set; }

		public double? ResponseSeconds { get; set; }
	}
}

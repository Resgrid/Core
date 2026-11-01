using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.IncidentBackOffice
{
	/// <summary>ibo.checkIn (plan §7.10): people waiting to be enrolled, and enrolled participants not yet checked in.</summary>
	public sealed class IncidentCheckInWidgetData : IncidentWidgetDataBase
	{
		/// <summary>The enrollment queue's length (IIncidentParticipantsService.GetEnrollmentQueueAsync).</summary>
		public int QueueCount { get; set; }

		/// <summary>Enrolled (not draft) participants of the activation without a check-in.</summary>
		public int AwaitingCheckIn { get; set; }

		/// <summary>Draft participants (staged, not yet enrolled).</summary>
		public int Drafts { get; set; }

		/// <summary>Longest waiting first; at most the maxRows setting.</summary>
		public List<IncidentCheckInCandidate> Candidates { get; set; } = new List<IncidentCheckInCandidate>();
	}

	public sealed class IncidentCheckInCandidate
	{
		public string Name { get; set; }

		/// <summary>Candidate kind as the queue reports it (e.g. Person, Unit).</summary>
		public string Kind { get; set; }

		/// <summary>Where the candidate came from (e.g. Dispatch, Roster).</summary>
		public string Source { get; set; }

		public DateTime? SeenSince { get; set; }
	}
}

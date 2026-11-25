using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model
{
	/// <summary>An "event roster status" message audience (§13a T4, LMS): members of one class session by their roster state. Persisted nowhere.</summary>
	public enum EventRosterAudienceStatuses
	{
		/// <summary>Enrolled (holding a seat).</summary>
		Enrolled = 0,
		/// <summary>Enrolled and marked attended.</summary>
		Attended = 1,
		/// <summary>Enrolled and marked absent.</summary>
		Absent = 2,
		/// <summary>On the waitlist.</summary>
		Waitlisted = 3
	}

	/// <summary>A class session offered as a message audience (the compose page's list).</summary>
	public sealed class EventRosterAudienceSession
	{
		public int SessionId { get; set; }
		public string Title { get; set; }
		/// <summary>UTC.</summary>
		public DateTime StartsOn { get; set; }
	}

	/// <summary>Training hours offered to a timecard (R-C10): who, when, how long, from which learning-record source.</summary>
	public sealed class TrainingTimeOffer
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public DateTime StartUtc { get; set; }
		public DateTime EndUtc { get; set; }
		public decimal Hours { get; set; }
		public string Title { get; set; }
		/// <summary>The <see cref="LearningRecordSourceKinds"/> of the source.</summary>
		public int SourceKind { get; set; }
		public string SourceRefId { get; set; }
	}
}

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Messages → Compose audience "members of class X who are enrolled / attended / absent / waitlisted" (§13a T4, LMS add-on). Only active
	/// members are returned. A baseline screen (Messages) asks it, so it carries no LMS type; it answers only while the LMS gate is open
	/// (<see cref="IsAvailableAsync"/>), which the compose page asks once per request. Extends the round-4C credential-status audience.
	/// </summary>
	public interface IEventRosterAudienceService
	{
		/// <summary>The LMS flag and add-on are on for the department (one billing read).</summary>
		Task<bool> IsAvailableAsync(int departmentId);

		/// <summary>The class sessions to offer: recent and upcoming, newest first.</summary>
		Task<List<EventRosterAudienceSession>> GetSessionsAsync(int departmentId, DateTime utcNow);

		/// <summary>The active members of the session in the asked roster state.</summary>
		Task<List<string>> ResolveAsync(int departmentId, int sessionId, EventRosterAudienceStatuses status);
	}

	/// <summary>
	/// Where training hours are offered to a timecard (§13 T4 item 6, R-C10): the Shifts / Workforce Pro timecard, where that add-on exists;
	/// the timecard owner approves the entry like any other. Workforce Pro timecards (Shifts plan SM7) are not built yet, so the registered
	/// implementation says "not available"; Workforce Pro registers its own when it lands.
	/// </summary>
	public interface ITrainingTimecardOffers
	{
		/// <summary>Whether the department has a timecard that takes training time.</summary>
		Task<bool> IsAvailableAsync(int departmentId);

		/// <summary>Offers the hours; true when the timecard accepted the offer for its owner's approval.</summary>
		Task<bool> OfferAsync(TrainingTimeOffer offer, CancellationToken cancellationToken = default);
	}
}

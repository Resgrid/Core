using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Something about one occurrence of a <see cref="RecurringCall"/> that a dispatcher has to act on (M0320): its call could not be
	/// created, nobody could be dispatched, some of the chosen resources were not available, or (dispatcher mode) it is time to send
	/// it. One row per schedule, occurrence and kind, so a worker pass that runs again never raises or notifies twice. Open until a
	/// dispatcher dismisses it or what it is about is settled (the held call went out, the occurrence was skipped or created); see
	/// <see cref="RecurringCallAlertResolutions"/>. Shown in the Dispatch app and on the Recurring Calls and Scheduled Calls pages,
	/// and sent to the schedule's alert recipients when raised.
	/// </summary>
	[Table("RecurringCallAlerts")]
	public class RecurringCallAlert : IEntity
	{
		[Key]
		[Required]
		public string RecurringCallAlertId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public int RecurringCallId { get; set; }

		/// <summary>When the occurrence is (was) due, UTC.</summary>
		public DateTime OccurrenceOn { get; set; }

		/// <summary><see cref="RecurringCallAlertKinds"/>.</summary>
		public int Kind { get; set; }

		/// <summary>The call concerned, when there is one (held, partly dispatched, or created after the alert).</summary>
		public int? CallId { get; set; }

		/// <summary>What happened, as <see cref="RecurringCallAlertDetail"/> JSON (ids and a reason code; clients word it).</summary>
		public string DetailJson { get; set; }

		public DateTime CreatedOn { get; set; }

		/// <summary>When the alert recipients were sent it: a channel accepted it for at least one of them (null until then).</summary>
		public DateTime? NotifiedOn { get; set; }

		/// <summary>
		/// A sender's claim on notifying the recipients (when it was taken), kept apart from <see cref="NotifiedOn"/>: only one sender
		/// sends an open alert, one that reached nobody gives the claim back, and one that stopped mid-send leaves a claim that lapses
		/// after its lease, so a later worker pass sends the alert instead of treating it as sent.
		/// </summary>
		public DateTime? NotificationClaimedOn { get; set; }

		public DateTime? AcknowledgedOn { get; set; }

		public string AcknowledgedByUserId { get; set; }

		public DateTime? ResolvedOn { get; set; }

		/// <summary>Who closed it; null when the system did (the call went out, the occurrence was dealt with).</summary>
		public string ResolvedByUserId { get; set; }

		/// <summary><see cref="RecurringCallAlertResolutions"/>.</summary>
		public int? Resolution { get; set; }

		[MaxLength(1000)]
		public string ResolutionNote { get; set; }

		[NotMapped]
		public bool IsOpen => !ResolvedOn.HasValue;

		public RecurringCallAlertDetail GetDetail()
		{
			if (string.IsNullOrWhiteSpace(DetailJson))
				return new RecurringCallAlertDetail();

			try
			{
				return JsonConvert.DeserializeObject<RecurringCallAlertDetail>(DetailJson) ?? new RecurringCallAlertDetail();
			}
			catch (JsonException)
			{
				return new RecurringCallAlertDetail();
			}
		}

		public void SetDetail(RecurringCallAlertDetail detail)
		{
			DetailJson = detail == null ? null : JsonConvert.SerializeObject(detail, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RecurringCallAlertId; }
			set { RecurringCallAlertId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RecurringCallAlerts";

		[NotMapped]
		public string IdName => "RecurringCallAlertId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsOpen" };
	}

	/// <summary>What a <see cref="RecurringCallAlert"/> is about. Persisted: append only.</summary>
	public enum RecurringCallAlertKinds
	{
		/// <summary>Creating the occurrence's call failed; the worker keeps trying until it is due.</summary>
		CreateFailed = 1,

		/// <summary>The occurrence's time passed and no call was created.</summary>
		Missed = 2,

		/// <summary>Nobody could be dispatched when it was due (none chosen, none available, no run card match): the call is held as pending.</summary>
		NoResources = 3,

		/// <summary>The call went out, but some chosen resources were not available and were left off it.</summary>
		ResourcesUnavailable = 4,

		/// <summary>Dispatcher mode: the occurrence is due and a dispatcher has to create and send it.</summary>
		DispatchDue = 5
	}

	/// <summary>How a <see cref="RecurringCallAlert"/> was closed. Persisted: append only.</summary>
	public enum RecurringCallAlertResolutions
	{
		/// <summary>The call it is about went out.</summary>
		Dispatched = 1,

		/// <summary>A dispatcher dismissed it.</summary>
		Dismissed = 2,

		/// <summary>The occurrence was skipped.</summary>
		Skipped = 3,

		/// <summary>The call it is about was closed or deleted.</summary>
		CallClosed = 4,

		/// <summary>The occurrence's call was created after all.</summary>
		CallCreated = 5,

		/// <summary>Nobody acted before it went stale (<see cref="RecurringCallRecurrence.DispatcherAlertExpiryHours"/>).</summary>
		Expired = 6,

		/// <summary>The schedule was deleted.</summary>
		ScheduleDeleted = 7
	}

	/// <summary>The reasons behind a <see cref="RecurringCallAlert"/>; stored as JSON.</summary>
	public class RecurringCallAlertDetail
	{
		/// <summary>Chosen units that were not available, and left off the call.</summary>
		public List<int> UnavailableUnitIds { get; set; }

		/// <summary>Chosen members who were not available, and left off the call.</summary>
		public List<string> UnavailableUserIds { get; set; }

		/// <summary><see cref="RecurringCallAlertReasons"/>.</summary>
		public string Reason { get; set; }
	}

	/// <summary>Reason codes in <see cref="RecurringCallAlertDetail.Reason"/> (stable strings; clients translate them).</summary>
	public static class RecurringCallAlertReasons
	{
		/// <summary>The schedule names nobody and no run card matched.</summary>
		public const string NoneChosen = "NoneChosen";

		/// <summary>Everyone chosen was unavailable (or has left).</summary>
		public const string NoneAvailable = "NoneAvailable";

		/// <summary>Saving the call failed.</summary>
		public const string SaveFailed = "SaveFailed";

		/// <summary>The department could not be loaded.</summary>
		public const string DepartmentUnavailable = "DepartmentUnavailable";
	}
}

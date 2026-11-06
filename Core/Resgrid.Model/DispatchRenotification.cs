using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	public enum DispatchRenotifyRecipientTypes
	{
		User = 1,
		Unit = 2
	}

	/// <summary>
	/// The call dispatch a recipient was sent through. The sweep cancels a recipient whose dispatch has been taken off the call,
	/// so a person removed from a call stops being re-notified.
	/// </summary>
	public enum DispatchRenotifySourceTypes
	{
		/// <summary>The person was dispatched by name (CallDispatches).</summary>
		User = 1,
		/// <summary>The person is a member, or on-duty shift member, of a dispatched group (CallDispatchGroups).</summary>
		Group = 2,
		/// <summary>The person holds a dispatched role (CallDispatchRoles).</summary>
		Role = 3,
		/// <summary>The unit itself was dispatched (CallDispatchUnits).</summary>
		Unit = 4,
		/// <summary>The person is riding on a dispatched unit (Dispatch Settings: also dispatch assigned personnel).</summary>
		UnitCrew = 5,
		/// <summary>The person is in a dispatched unit's station group (Dispatch Settings: also dispatch the unit's group).</summary>
		UnitGroup = 6
	}

	public enum DispatchRenotifyStatuses
	{
		/// <summary>Waiting for an acknowledgment; re-notified at <see cref="DispatchRenotification.NextNotifyOn"/>.</summary>
		Pending = 0,
		/// <summary>The recipient set a status after the dispatch.</summary>
		Acknowledged = 1,
		/// <summary>Every re-notification was sent without an acknowledgment.</summary>
		Exhausted = 2,
		/// <summary>The call closed, the recipient came off the call, or the department turned re-notification off.</summary>
		Cancelled = 3
	}

	/// <summary>
	/// One dispatched person or unit being re-notified until they acknowledge a call (DispatchRenotifications, M0311). Written by
	/// the call broadcast when the department has <see cref="DepartmentSettingTypes.DispatchRenotifyConfig"/> on, and advanced by
	/// the dispatch re-notify worker (78). Holds ids and times only, never call content. Rows cascade with their call and are
	/// purged a while after they finish.
	/// </summary>
	public class DispatchRenotification : IEntity
	{
		public string DispatchRenotificationId { get; set; }
		public int DepartmentId { get; set; }
		public int CallId { get; set; }

		/// <summary><see cref="DispatchRenotifyRecipientTypes"/>.</summary>
		public int RecipientType { get; set; }

		/// <summary>The user id, or the unit id as text.</summary>
		public string RecipientId { get; set; }

		/// <summary><see cref="DispatchRenotifySourceTypes"/>.</summary>
		public int SourceType { get; set; }

		/// <summary>The dispatched user, group, role or unit id the recipient came through, as text.</summary>
		public string SourceId { get; set; }

		/// <summary><see cref="DispatchRenotifyStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>Re-notifications sent so far.</summary>
		public int Attempts { get; set; }

		/// <summary>A status set at or after this time (UTC) acknowledges the dispatch. Kept when a later broadcast restarts the cycle.</summary>
		public DateTime AcknowledgeAfter { get; set; }

		/// <summary>When the next re-notification is due (UTC); null once the row is finished.</summary>
		public DateTime? NextNotifyOn { get; set; }

		public DateTime? LastNotifiedOn { get; set; }
		public DateTime? AcknowledgedOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		public DateTime CreatedOn { get; set; }

		public object IdValue { get => DispatchRenotificationId; set => DispatchRenotificationId = (string)value; }
		public string TableName => "DispatchRenotifications";
		public string IdName => "DispatchRenotificationId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A person or unit the call broadcast sent a dispatch to, and the dispatch it came through.</summary>
	public sealed class DispatchRenotifyRecipient
	{
		public DispatchRenotifyRecipient(DispatchRenotifyRecipientTypes recipientType, string recipientId, DispatchRenotifySourceTypes sourceType, string sourceId)
		{
			RecipientType = recipientType;
			RecipientId = recipientId;
			SourceType = sourceType;
			SourceId = sourceId;
		}

		public DispatchRenotifyRecipientTypes RecipientType { get; }
		public string RecipientId { get; }
		public DispatchRenotifySourceTypes SourceType { get; }
		public string SourceId { get; }

		public static DispatchRenotifyRecipient ForUser(string userId, DispatchRenotifySourceTypes sourceType, string sourceId)
			=> new DispatchRenotifyRecipient(DispatchRenotifyRecipientTypes.User, userId, sourceType, sourceId);

		public static DispatchRenotifyRecipient ForUnit(int unitId)
			=> new DispatchRenotifyRecipient(DispatchRenotifyRecipientTypes.Unit, unitId.ToString(), DispatchRenotifySourceTypes.Unit, unitId.ToString());
	}

	/// <summary>What one sweep of the dispatch re-notify worker did.</summary>
	public sealed class DispatchRenotifySweepResult
	{
		public int Due { get; set; }
		public int Renotified { get; set; }
		public int Acknowledged { get; set; }
		public int Exhausted { get; set; }
		public int Cancelled { get; set; }
		public int Purged { get; set; }

		public override string ToString()
			=> $"Due {Due}; re-notified {Renotified} ({Exhausted} on their last attempt); acknowledged {Acknowledged}; cancelled {Cancelled}; purged {Purged}.";
	}
}

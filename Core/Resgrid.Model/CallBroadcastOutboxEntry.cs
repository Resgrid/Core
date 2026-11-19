using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// The page owed to resources an edit added to a live call (M0321): members, groups, units and roles newly dispatched, or
	/// released members named again. Written just before the call saves, so a broadcast the queue refuses (or a request that
	/// fails before it reaches the queue) is not lost: the edit only pages what it changed, so a retry of the same edit sees
	/// nothing new and would page nobody. The edit sends the row at once; worker 12 retries what is left. Whoever is paged is
	/// decided again at send time from the call as it is then (still live, still dispatched, not released since).
	/// </summary>
	[Table("CallBroadcastOutbox")]
	public class CallBroadcastOutboxEntry : IEntity
	{
		[Key]
		[Required]
		public string CallBroadcastOutboxId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public int CallId { get; set; }

		/// <summary>Member ids to page, comma separated, as the call's dispatch rows spell them.</summary>
		public string UserIds { get; set; }

		/// <summary>Group ids to page, comma separated.</summary>
		public string GroupIds { get; set; }

		/// <summary>Unit ids to page, comma separated.</summary>
		public string UnitIds { get; set; }

		/// <summary>Role ids to page, comma separated.</summary>
		public string RoleIds { get; set; }

		/// <summary><see cref="CallBroadcastOutboxStates"/>.</summary>
		public int State { get; set; }

		/// <summary>Send attempts so far (each claim counts one).</summary>
		public int Attempts { get; set; }

		/// <summary>When the worker may try it again, UTC.</summary>
		public DateTime NextAttemptOn { get; set; }

		/// <summary>The claim of whoever is sending it now; a claim past its lease can be taken over.</summary>
		public string ClaimToken { get; set; }

		public DateTime? LeaseExpiresOn { get; set; }

		[MaxLength(1000)]
		public string LastError { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		/// <summary>When it reached an end state (sent, skipped, expired or superseded).</summary>
		public DateTime? CompletedOn { get; set; }

		/// <summary>False when the row could not be written: the edit then sends it directly, once, as before the outbox.</summary>
		[NotMapped]
		[JsonIgnore]
		public bool Persisted { get; set; }

		public List<string> GetUserIds()
		{
			return (UserIds ?? string.Empty).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		public List<int> GetGroupIds() => ParseInts(GroupIds);

		public List<int> GetUnitIds() => ParseInts(UnitIds);

		public List<int> GetRoleIds() => ParseInts(RoleIds);

		public void SetRecipients(IEnumerable<string> userIds, IEnumerable<int> groupIds, IEnumerable<int> unitIds, IEnumerable<int> roleIds)
		{
			UserIds = Join((userIds ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
			GroupIds = Join((groupIds ?? Enumerable.Empty<int>()).Distinct().Select(x => x.ToString(CultureInfo.InvariantCulture)));
			UnitIds = Join((unitIds ?? Enumerable.Empty<int>()).Distinct().Select(x => x.ToString(CultureInfo.InvariantCulture)));
			RoleIds = Join((roleIds ?? Enumerable.Empty<int>()).Distinct().Select(x => x.ToString(CultureInfo.InvariantCulture)));
		}

		[NotMapped]
		public bool HasRecipients => GetUserIds().Count > 0 || GetGroupIds().Count > 0 || GetUnitIds().Count > 0 || GetRoleIds().Count > 0;

		private static string Join(IEnumerable<string> values)
		{
			var joined = string.Join(",", values);
			return joined.Length == 0 ? null : joined;
		}

		private static List<int> ParseInts(string value)
		{
			return (value ?? string.Empty).Split(',')
				.Select(x => int.TryParse(x.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (int?)null)
				.Where(x => x.HasValue).Select(x => x.Value).Distinct().ToList();
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallBroadcastOutboxId; }
			set { CallBroadcastOutboxId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "CallBroadcastOutbox";

		[NotMapped]
		public string IdName => "CallBroadcastOutboxId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Persisted", "HasRecipients" };
	}

	/// <summary>Where a <see cref="CallBroadcastOutboxEntry"/> stands. Persisted: append only.</summary>
	public enum CallBroadcastOutboxStates
	{
		/// <summary>Not sent yet; the worker tries it from <see cref="CallBroadcastOutboxEntry.NextAttemptOn"/>.</summary>
		Pending = 0,

		/// <summary>Claimed by a sender until its lease runs out.</summary>
		Sending = 1,

		/// <summary>The queue accepted the page.</summary>
		Sent = 2,

		/// <summary>Nobody was left to page: the call closed or went back to waiting, or everyone came off it.</summary>
		Skipped = 3,

		/// <summary>The queue refused it for longer than the page stays useful; logged.</summary>
		Expired = 4,

		/// <summary>A full rebroadcast of the call went out after it and paged everyone on it.</summary>
		Superseded = 5
	}
}

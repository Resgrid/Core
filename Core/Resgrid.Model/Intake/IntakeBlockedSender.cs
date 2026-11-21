using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A sender a department's dispatchers blocked from its intake channels (M0326), usually by dismissing one of their calls as spam.
	/// While a row is in force (<see cref="RemovedOn"/> null and <see cref="ExpiresOn"/> null or later than now) every message from
	/// <see cref="Sender"/> on any of the department's intake channels is recorded as <see cref="IntakeMessageStates.SenderBlocked"/>
	/// and creates nothing; a phone number is blocked for texts and calls alike. Blocks expire (phone numbers are reassigned) and an
	/// admin can lift one early; the row stays as the record until worker 80 purges it. No foreign keys; the department purge removes
	/// the rows.
	/// </summary>
	[Table("IntakeBlockedSenders")]
	public class IntakeBlockedSender : IEntity
	{
		public const int MaxSenderLength = 256;
		public const int MaxReasonLength = 100;

		[Key]
		[Required]
		public string IntakeBlockedSenderId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary>
		/// The sender as intake records it (<see cref="IntakeBlockedSenders.Normalize"/>): E.164 for phone numbers, lower case for email
		/// addresses, the platform id otherwise.
		/// </summary>
		[MaxLength(MaxSenderLength)]
		public string Sender { get; set; }

		/// <summary>The <see cref="IntakeChannelTypes"/> value of the call the block came from (for display; the block covers every channel).</summary>
		public int ChannelType { get; set; }

		/// <summary>An <see cref="IntakeBlockReasons"/> name ("Spam").</summary>
		[MaxLength(MaxReasonLength)]
		public string Reason { get; set; }

		/// <summary>The call that was dismissed when the sender was blocked, if any.</summary>
		public int? CallId { get; set; }

		public string BlockedByUserId { get; set; }

		public DateTime BlockedOn { get; set; }

		/// <summary>When the block stops applying; null = until an admin lifts it.</summary>
		public DateTime? ExpiresOn { get; set; }

		public string RemovedByUserId { get; set; }

		/// <summary>When an admin lifted the block; null while it stands.</summary>
		public DateTime? RemovedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsRemoved => RemovedOn.HasValue;

		/// <summary>The block applies at <paramref name="nowUtc"/>.</summary>
		public bool IsInForce(DateTime nowUtc) => !RemovedOn.HasValue && (!ExpiresOn.HasValue || ExpiresOn.Value > nowUtc);

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IntakeBlockedSenderId; }
			set { IntakeBlockedSenderId = (string)value; }
		}

		[NotMapped]
		public string TableName => "IntakeBlockedSenders";

		[NotMapped]
		public string IdName => "IntakeBlockedSenderId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsRemoved" };
	}

	/// <summary><see cref="IntakeBlockedSender.Reason"/> values.</summary>
	public static class IntakeBlockReasons
	{
		/// <summary>A dispatcher dismissed one of the sender's calls as spam.</summary>
		public const string Spam = "Spam";
	}

	/// <summary>How senders are compared on the blocked list, so the block check and the block itself agree.</summary>
	public static class IntakeBlockedSenders
	{
		/// <summary>
		/// The form a sender is stored and looked up in: E.164 for anything that is a phone number, lower case for an email address, the
		/// trimmed value otherwise. Null when there is nothing to block (blank, or a carrier's withheld caller id placeholder).
		/// </summary>
		public static string Normalize(string sender)
		{
			if (string.IsNullOrWhiteSpace(sender))
				return null;

			var value = sender.Trim();
			var phone = IntakeChannelAddresses.PhoneNumber(value);
			if (phone != null)
				return IntakeChannelAddresses.IsWithheldCallerId(phone) ? null : phone;
			if (value.Contains('@'))
				return value.ToLowerInvariant();

			return value.Length <= IntakeBlockedSender.MaxSenderLength ? value : value.Substring(0, IntakeBlockedSender.MaxSenderLength);
		}
	}

	/// <summary>The outcome of a dispatcher dismissing an intake call as spam.</summary>
	public enum IntakeDismissResults
	{
		Dismissed = 0,

		/// <summary>No such call in the department, or it did not come from intake (or its intake record was already purged).</summary>
		NotFound = 1,

		/// <summary>The call is no longer pending (a dispatcher sent or closed it, or is sending it right now).</summary>
		NotPending = 2,

		/// <summary>
		/// Nothing was dismissed and the call is still pending: the block asked for could not be put in force, or the label or the
		/// cancel could not be saved. The dispatcher tries again.
		/// </summary>
		Failed = 3
	}

	/// <summary>What <c>IIntakeScreeningService.DismissAsSpamAsync</c> did.</summary>
	public sealed class IntakeDismissResult
	{
		public IntakeDismissResults Result { get; set; }

		public int CallId { get; set; }

		/// <summary>The block that now covers the sender; null when no block was asked for or the sender cannot be blocked.</summary>
		public IntakeBlockedSender Block { get; set; }

		/// <summary>A block was asked for but the sender has no blockable address (withheld caller id, blank).</summary>
		public bool SenderNotBlockable { get; set; }
	}
}

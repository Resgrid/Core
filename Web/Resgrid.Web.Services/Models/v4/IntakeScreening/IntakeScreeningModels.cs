using System;
using System.ComponentModel.DataAnnotations;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Models.v4.IntakeScreening
{
	/// <summary>Dismiss a pending intake call as spam (cancel it, optionally block its sender).</summary>
	public class DismissIntakeCallInput
	{
		[Range(1, int.MaxValue)]
		public int CallId { get; set; }

		/// <summary>
		/// Also block the sender from the department's intake channels for IntakeConfig.SpamBlockDays (default true). False dismisses the
		/// call without blocking: the sender can still reach intake.
		/// </summary>
		public bool BlockSender { get; set; } = true;
	}

	/// <summary>What a dismissal did.</summary>
	public class DismissIntakeCallResult : StandardApiResponseV4Base
	{
		public DismissIntakeCallResultData Data { get; set; } = new DismissIntakeCallResultData();
	}

	public class DismissIntakeCallResultData
	{
		public int CallId { get; set; }

		/// <summary>
		/// Dismissed, NotFound (no such intake call in the department), NotPending (it was already sent or closed, or is being sent), or
		/// Failed (nothing was dismissed; the call is still pending and the request can be sent again).
		/// </summary>
		public string Outcome { get; set; }

		/// <summary>The sender is now blocked (also with Failed, when the block was made before the failure).</summary>
		public bool SenderBlocked { get; set; }

		/// <summary>A block was asked for but the sender cannot be blocked (withheld caller id).</summary>
		public bool SenderNotBlockable { get; set; }

		/// <summary>When the block ends (UTC); null = until an admin lifts it, or no block.</summary>
		public DateTime? BlockedUntilUtc { get; set; }
	}
}

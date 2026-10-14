using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// One inbound email delivery the API has accepted (registry M0271). Postmark deliveries to <c>api/Email/Receive</c> are keyed
	/// by Postmark's message id; v4 SaveCall / SaveCallFile requests carrying an <c>Idempotency-Key</c> (the SMTP relay sends the
	/// email's Message-Id) by "{kind}:{departmentId}:{key}". Both senders retry: Postmark every non-200 answer for about 10.5 hours
	/// (only a 403 stops it) and after waiting 2 minutes, the relay whenever the sending mail server redelivers. So the same email
	/// can arrive again after it was handled, or while it is still being handled. The receipt makes a redelivery acknowledge, wait,
	/// or finish the call or message an earlier attempt saved, instead of creating a second call and paging twice. A system
	/// record: no department column and no foreign key; rows are purged after <c>InboundEmailReceiptsService.Retention</c>.
	/// </summary>
	public class InboundEmailReceipt : IEntity
	{
		public const int ProviderMessageIdMaxLength = 128;

		[Key]
		[Required]
		[MaxLength(128)]
		public string InboundEmailReceiptId { get; set; }

		/// <summary>The provider's id for the email (Postmark <c>MessageID</c>), unique.</summary>
		[Required]
		[MaxLength(ProviderMessageIdMaxLength)]
		public string ProviderMessageId { get; set; }

		/// <summary>An <see cref="InboundEmailKinds"/> value.</summary>
		public int Kind { get; set; }

		/// <summary>An <see cref="InboundEmailReceiptStates"/> value.</summary>
		public int State { get; set; }

		/// <summary>The call (dispatch kinds) or message (group message) the email produced, once it is saved.</summary>
		public int? EntityId { get; set; }

		/// <summary><see cref="InboundEmailPendingSteps"/> still owed to <see cref="EntityId"/> while the receipt is Saved.</summary>
		public int PendingSteps { get; set; }

		/// <summary>How many times the provider delivered this email, the first included.</summary>
		public int Deliveries { get; set; }

		public DateTime ReceivedOn { get; set; }

		/// <summary>When the attempt working on the email took (or last renewed) its claim; null once no attempt holds it.</summary>
		public DateTime? ClaimedOn { get; set; }

		public DateTime? SavedOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => InboundEmailReceiptId;
			set => InboundEmailReceiptId = (string)value;
		}

		[NotMapped]
		public string TableName => "InboundEmailReceipts";

		[NotMapped]
		public string IdName => "InboundEmailReceiptId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// What a receipt guards. 1 to 4 are Postmark deliveries to <c>api/Email/Receive</c>, by where the email was addressed; 5 and
	/// 6 are v4 requests carrying an <c>Idempotency-Key</c> (the SMTP relay sends the email's Message-Id), keyed per department.
	/// </summary>
	public enum InboundEmailKinds
	{
		Dispatch = 1,
		DistributionList = 2,
		GroupDispatch = 3,
		GroupMessage = 4,
		ApiCall = 5,
		ApiCallFile = 6
	}

	public enum InboundEmailReceiptStates
	{
		/// <summary>An attempt claimed the email and has not saved anything yet.</summary>
		Processing = 0,

		/// <summary>The call or message is saved (<see cref="InboundEmailReceipt.EntityId"/>); its pending steps are not done.</summary>
		Saved = 1,

		/// <summary>Fully handled: a redelivery only acknowledges.</summary>
		Completed = 2
	}

	/// <summary>
	/// Work owed to a saved call or message, finished by a redelivery when the attempt that saved it failed. v4 SaveCall clears
	/// each step as it completes, so a retry only repeats the steps that never happened (no second Call Added event or page).
	/// </summary>
	[Flags]
	public enum InboundEmailPendingSteps
	{
		None = 0,
		CallBroadcast = 1,
		AiEnrichment = 2,
		MessageSend = 4,
		RunCardActivation = 8,
		WeatherAlerts = 16,
		CallAddedEvent = 32,
		DispatchStatuses = 64,
		CustomFieldValues = 128
	}
}

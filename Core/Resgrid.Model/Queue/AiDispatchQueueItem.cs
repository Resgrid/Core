using System;
using ProtoBuf;

namespace Resgrid.Model.Queue
{
	/// <summary>
	/// One Enrich-mode AI dispatch job (aidispatchtriage queue, ai-dispatch-template-plan.md). Identifiers only: the worker reads
	/// the message text from the call it already created, so caller and patient detail never travel on the bus.
	/// </summary>
	[ProtoContract]
	public class AiDispatchQueueItem
	{
		[ProtoMember(1)]
		public int DepartmentId { get; set; }

		[ProtoMember(2)]
		public int CallId { get; set; }

		/// <summary>1 = Postmark webhook, 2 = group dispatch address, 3 = IMAP import.</summary>
		[ProtoMember(3)]
		public int Channel { get; set; }

		[ProtoMember(4)]
		public DateTime QueuedOnUtc { get; set; }

		/// <summary>The department's sender allowlist excluded the sender (decided at enqueue so no address travels on the bus).</summary>
		[ProtoMember(5)]
		public bool SenderNotAllowed { get; set; }
	}
}

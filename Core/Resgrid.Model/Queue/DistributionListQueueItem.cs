using System.Collections.Generic;
using ProtoBuf;
using Resgrid.Model.Identity;

namespace Resgrid.Model.Queue
{
	[ProtoContract]
	public class DistributionListQueueItem
	{
		[ProtoMember(1)]
		public QueueItem QueueItem { get; set; }

		[ProtoMember(2)]
		public DistributionList List { get; set; }

		[ProtoMember(3)]
		public InboundMessage Message { get; set; }

		[ProtoMember(4)]
		public List<IdentityUser> Users { get; set; }

		[ProtoMember(5)]
		public List<int> FileIds { get; set; }
	}

	[ProtoContract]
	public class InboundMessageAttachment
	{
		[ProtoMember(1)]
		public string Content { get; set; }

		[ProtoMember(2)]
		public string ContentType { get; set; }

		[ProtoMember(3)]
		public string ContentID { get; set; }

		[ProtoMember(4)]
		public string ContentLength { get; set; }

		[ProtoMember(5)]
		public string Name { get; set; }
	}

	[ProtoContract]
	public class InboundMessage
	{
		[ProtoMember(1)]
		public List<InboundMessageAttachment> Attachments { get; set; }

		[ProtoMember(2)]
		public string Cc { get; set; }

		[ProtoMember(3)]
		public string Date { get; set; }

		[ProtoMember(4)]
		public string FromEmail { get; set; }

		[ProtoMember(5)]
		public string FromName { get; set; }

		[ProtoMember(6)]
		public string HtmlBody { get; set; }

		[ProtoMember(7)]
		public string MessageID { get; set; }

		[ProtoMember(8)]
		public string Subject { get; set; }

		[ProtoMember(9)]
		public string TextBody { get; set; }

		[ProtoMember(10)]
		public string To { get; set; }
	}
}

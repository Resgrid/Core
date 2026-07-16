using ProtoBuf;

namespace Resgrid.Model.Queue
{
	/// <summary>
	/// Carries an inbound chatbot message (e.g. a Twilio SMS) onto the bus so the chatbot
	/// pipeline runs in the worker process instead of inline on the inbound webhook thread.
	/// The worker resolves IChatbotIngressService, processes the message, and sends the reply
	/// back to <see cref="From"/> via the SMS transport.
	/// </summary>
	[ProtoContract]
	public class ChatbotMessageQueueItem
	{
		[ProtoMember(1)]
		public int DepartmentId { get; set; }

		/// <summary>Number the message was sent TO (the department text number), digits only.</summary>
		[ProtoMember(2)]
		public string To { get; set; }

		/// <summary>Sender's number, digits only — where the reply is delivered.</summary>
		[ProtoMember(3)]
		public string From { get; set; }

		[ProtoMember(4)]
		public string Body { get; set; }

		/// <summary>Platform message id (e.g. Twilio MessageSid).</summary>
		[ProtoMember(5)]
		public string MessageId { get; set; }

		/// <summary>ChatbotPlatform value (kept as int so Resgrid.Model stays free of the Chatbot dependency).</summary>
		[ProtoMember(6)]
		public int Platform { get; set; }
	}
}

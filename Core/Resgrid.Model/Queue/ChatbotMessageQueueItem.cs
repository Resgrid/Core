using ProtoBuf;

namespace Resgrid.Model.Queue
{
	/// <summary>
	/// Carries an inbound chatbot message onto the bus so the assistant runs in the worker.
	/// Platform selects the reply transport; From is an opaque native identity, not necessarily a phone number.
	/// </summary>
	[ProtoContract]
	public class ChatbotMessageQueueItem
	{
		[ProtoMember(1)]
		public int DepartmentId { get; set; }

		/// <summary>Destination identity; for SMS, the department text number in digits.</summary>
		[ProtoMember(2)]
		public string To { get; set; }

		/// <summary>Sender's platform identity; for SMS, the phone number in digits.</summary>
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

		/// <summary>
		/// The incident (call) the sender had open when they asked, so a command-board question like
		/// "PAR" resolves against the board they are looking at. Null for SMS and general web chat. It's
		/// only a hint — the pipeline still re-checks department scoping and view permission.
		/// </summary>
		[ProtoMember(7)]
		public int? IncidentCallId { get; set; }

		/// <summary>Routing metadata from an authenticated platform webhook; never a client-supplied URL.</summary>
		[ProtoMember(8)]
		public System.Collections.Generic.Dictionary<string, string> PlatformMetadata { get; set; }

		[ProtoMember(9)]
		public System.DateTime ReceivedAtUtc { get; set; }
	}
}

using System.Collections.Generic;

namespace Resgrid.Model
{
	public class CallEmail
	{
		public string MessageId { get; set; }
		/// <summary>Sender address when the inbound path knows it; used for the AI dispatch sender allowlist.</summary>
		public string From { get; set; }
		public string Subject { get; set; }
		public string Body { get; set; }
		public string TextBody { get; set; }
		public string DispatchAudioFileName { get; set; }
		public byte[] DispatchAudio { get; set; }
	}
}
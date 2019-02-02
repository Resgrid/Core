using System.Collections.Generic;

namespace Resgrid.Model
{
	public class CallEmail
	{
		public string MessageId { get; set; }
		public string Subject { get; set; }
		public string Body { get; set; }
		public string TextBody { get; set; }
		public string DispatchAudioFileName { get; set; }
		public byte[] DispatchAudio { get; set; }
	}
}
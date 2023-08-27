using System;
using System.Collections.Generic;
using System.Text;

namespace Resgrid.Providers.Voip.LiveKit
{
	public class LiveKitVideoGrant
	{
		public bool roomCreate { get; set; }
		public bool roomList { get; set; }
		public bool roomRecord { get; set; }

		public bool roomAdmin { get; set; }
		public bool roomJoin { get; set; }
		public string room { get; set; }

		public bool? canPublish { get; set; }
		public bool? canSubscribe { get; set; }
		public bool? canPublishData { get; set; }

		public bool? hidden { get; set; }
	}
}

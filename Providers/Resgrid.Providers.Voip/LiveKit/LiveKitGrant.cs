using System;
using System.Collections.Generic;
using System.Text;

namespace Resgrid.Providers.Voip.LiveKit
{
	public class LiveKitGrant
	{
		public string identity { get; set; }
		public LiveKitVideoGrant video { get; set; }
		public string metadata { get; set; }

		public IDictionary<string, object> ToDictionary()
		{
			var dir = new Dictionary<string, object>();
			dir.Add("identity", identity);
			dir.Add("video", video.ToDictionary());
			dir.Add("metadata", metadata);
			return dir;
		}
	}
}

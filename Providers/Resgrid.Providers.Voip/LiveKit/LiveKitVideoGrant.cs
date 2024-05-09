using Resgrid.Providers.Voip.Kazoo.Model;
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

		public IDictionary<string, object> ToDictionary()
		{
			var dir = new Dictionary<string, object>();
			dir.Add("roomCreate", roomCreate);
			dir.Add("roomList", roomList);
			dir.Add("roomRecord", roomRecord);
			dir.Add("roomAdmin", roomAdmin);
			dir.Add("roomJoin", roomJoin);
			dir.Add("room", room);

			if (canPublish.HasValue)
			{
				dir.Add("canPublish", canPublish);
			}

			if (canSubscribe.HasValue)
			{
				dir.Add("canSubscribe", canSubscribe);
			}

			if (canPublishData.HasValue)
			{
				dir.Add("canPublishData", canPublishData);
			}

			if (hidden.HasValue)
			{
				dir.Add("hidden", hidden);
			}

			return dir;
		}
	}
}

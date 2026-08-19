using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Resgrid.Providers.Bus.Models
{
	public class ApnsPayload
	{
		public ApnsHeader aps { get; set; }
		public string eventCode { get; set; }
		public string type { get; set; }
		public ApnsCustomData body { get; set; }
	}

	/// <summary>
	/// Serialized as the top-level `body` custom key, which is the only key
	/// expo-notifications on iOS surfaces to the app as content.data.
	/// </summary>
	public class ApnsCustomData
	{
		public string eventCode { get; set; }
		public string type { get; set; }
	}
}

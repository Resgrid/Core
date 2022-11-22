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
	}
}

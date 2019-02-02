using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace GoogleMapsApi
{
	public class WebClientEx : WebClient
	{
		public TimeSpan? Timeout { get; set; }

		public WebClientEx() { }
		public WebClientEx(TimeSpan timeout)
		{
			if (timeout != WebClientExtensionMethods.InfiniteTimeout && timeout <= TimeSpan.Zero)
				throw new ArgumentOutOfRangeException("timeout", timeout, "The specified timeout must be greater than zero or infinite.");

			Timeout = timeout;
		}

		protected override WebRequest GetWebRequest(Uri address)
		{
			var request = base.GetWebRequest(address);

			if (Timeout != null)
				request.Timeout = (int)Timeout.Value.TotalMilliseconds;

			return request;
		}
	}
}

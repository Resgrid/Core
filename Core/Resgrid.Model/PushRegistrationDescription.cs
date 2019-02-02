using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	public class PushRegistrationDescription
	{
		public string ETag { get; set; }
		public DateTime? ExpirationTime { get; set; }
		public string RegistrationId { get; set; }
		public ISet<string> Tags { get; set; }
	}
}
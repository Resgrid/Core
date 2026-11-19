namespace Resgrid.Model
{
	/// <summary>
	/// Where a request that a service audits came from, passed down by the web and API controllers so the audit row records the
	/// caller's address and client. Null for the worker.
	/// </summary>
	public class AuditOrigin
	{
		public string IpAddress { get; set; }

		public string UserAgent { get; set; }
	}
}

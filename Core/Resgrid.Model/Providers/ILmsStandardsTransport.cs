using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>An outbound HTTP answer for the LMS standards features (status 0 with <see cref="Error"/> when no answer came back).</summary>
	public sealed class LmsHttpResult
	{
		public int StatusCode { get; set; }

		/// <summary>The response body, at most 64 KB.</summary>
		public string Body { get; set; }

		/// <summary>Why nothing came back: a refused destination, a timeout or a network error.</summary>
		public string Error { get; set; }
	}

	/// <summary>
	/// Outbound HTTP for Training T5 (worker 98's xAPI delivery to a department LRS, an LTI tool's public key set). Only https, only public
	/// addresses (checked on the resolved address, so a name cannot be pointed at an internal service), no redirects, bounded time and size.
	/// Tests replace it; nothing here is reachable inbound (no-inbound-external-ports policy).
	/// </summary>
	public interface ILmsStandardsTransport
	{
		Task<LmsHttpResult> PostJsonAsync(string url, string json, IDictionary<string, string> headers, CancellationToken cancellationToken = default);

		Task<LmsHttpResult> GetAsync(string url, CancellationToken cancellationToken = default);
	}
}

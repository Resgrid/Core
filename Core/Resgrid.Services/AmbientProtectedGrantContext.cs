using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The default <see cref="IProtectedGrantContext"/> (ADP plan 3.3 / 7.2): no request is in flight, so the
	/// caller is a workload — a worker, a console tool, the composition test. Web hosts override this
	/// registration with their request-bound implementation after loading ServicesModule, so RMS services
	/// never have to know which host they run in.
	/// </summary>
	public sealed class WorkloadProtectedGrantContext : IProtectedGrantContext
	{
		public string GrantToken => null;

		public string UserId => null;

		public bool IsWorkloadCaller => true;
	}

	/// <summary>A fixed grant context for tests and one-off tool runs.</summary>
	public sealed class FixedProtectedGrantContext : IProtectedGrantContext
	{
		public FixedProtectedGrantContext(string grantToken, bool isWorkloadCaller, string userId = null)
		{
			GrantToken = grantToken;
			IsWorkloadCaller = isWorkloadCaller;
			UserId = userId;
		}

		public static FixedProtectedGrantContext Workload { get; } = new FixedProtectedGrantContext(null, true);

		public string GrantToken { get; }
		public string UserId { get; }
		public bool IsWorkloadCaller { get; }
	}
}

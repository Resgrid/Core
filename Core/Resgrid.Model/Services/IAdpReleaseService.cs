using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Providers;

namespace Resgrid.Model.Services
{
	public interface IAdpReleaseService
	{
		Task<bool> EnrollPinAsync(int departmentId, string userId, string grantToken, string pin, CancellationToken cancellationToken = default);
		Task<string> CreateChallengeAsync(int departmentId, int callId, string userId, string phone, ProtectedDataEgressChannel channel, CancellationToken cancellationToken = default);
		Task<string> ReleaseAsync(string challengeId, string phone, string pin, ProtectedDataEgressChannel channel, CancellationToken cancellationToken = default);
	}

	/// <summary>One-use, exact-field capabilities checked at the broker in addition to workload authentication.</summary>
	public interface IAdpReleaseReceiptService
	{
		Task<string> IssueAsync(int departmentId, long epoch, string actorId, string purpose,
			IReadOnlyList<ProtectedFieldOperationItem> fields, CancellationToken cancellationToken = default);
		Task<string> ConsumeAsync(string token, int departmentId, long epoch,
			IReadOnlyList<ProtectedFieldOperationItem> fields, CancellationToken cancellationToken = default);
	}
}

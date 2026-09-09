using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>Runs inside the flag/module command transaction. Before acquires locks before policy writes;
	/// After observes uncached policy and persists dependent operational state before commit.</summary>
	public interface IFeatureFlagMutationObserver
	{
		Task BeforeChangeAsync(int? departmentId, CancellationToken ct);
		Task AfterChangeAsync(Func<string, int, Task<bool>> evaluate, CancellationToken ct);
	}
}

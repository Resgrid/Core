using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Durable compare-and-swap state. No plaintext PIN, grant token or customer content is stored.</summary>
	public interface IAdpAccessStore
	{
		Task<AdpAccessState> GetAsync(string id, CancellationToken cancellationToken = default);
		Task<bool> SaveAsync(string id, string json, long expectedVersion, CancellationToken cancellationToken = default);
	}

	public sealed class AdpAccessState
	{
		public string StateId { get; set; }
		public string Json { get; set; }
		public long Version { get; set; }
	}
}

using System.Threading.Tasks;
using Resgrid.Model.Services;

namespace Resgrid.Model.Repositories
{
	/// <summary>Durable, metadata-only command reservations. Pending commands never expire into automatic retries.</summary>
	public interface IRmsCommandReceiptsRepository
	{
		Task<RecordCommandReceipt> GetAsync(int departmentId, string keyHash);
		Task<bool> ReserveAsync(int departmentId, string keyHash, string recordId, string requestChecksum, string reservationId);
		Task<bool> CompleteAsync(int departmentId, string keyHash, string recordId, string requestChecksum, string reservationId);
	}
}

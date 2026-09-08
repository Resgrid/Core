using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IReadinessAccessService
	{
		/// <summary>Free checklist operation gate. Never queries billing.</summary>
		Task<bool> CanUseChecklistsAsync(int departmentId);

		/// <summary>
		/// Gate for new maintenance/work order operations: rollout, module and a current paid entitlement.
		/// Not a gate for historical evidence reads or releasing an existing safety hold after expiry.
		/// </summary>
		Task<bool> CanUseMaintenanceAsync(int departmentId);
	}
}

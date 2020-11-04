using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface ISignalrProvider
	{
		Task<bool> PersonnelStatusUpdated(int departmentId, ActionLog actionLog);
		Task<bool> UnitStatusUpdated(int departmentId, UnitState unitState);
		Task<bool> CallsUpdated(int departmentId, Call call);
		Task<bool> PersonnelStaffingUpdated(int departmentId, UserState userState);
	}
}

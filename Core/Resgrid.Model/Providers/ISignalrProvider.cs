namespace Resgrid.Model.Providers
{
	public interface ISignalrProvider
	{
		void PersonnelStatusUpdated(int departmentId, ActionLog actionLog);
		void UnitStatusUpdated(int departmentId, UnitState unitState);
		void CallsUpdated(int departmentId, Call call);
		void PersonnelStaffingUpdated(int departmentId, UserState userState);
	}
}

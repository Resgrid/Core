using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface IWorkShiftsService
	/// </summary>
	public interface IWorkShiftsService
	{
		Task<Workshift> AddWorkshiftAsync(Workshift workshift, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<Workshift>> GetAllWorkshiftsByDepartmentAsync(int departmentId);
		Task<Workshift> GetWorkshiftByIdAsync(string workshiftId);
		Task<WorkshiftDay> GetWorkshiftDayByIdAsync(string workshiftDayId);
		Task<Workshift> DeleteWorkshiftByIdAsync(string workshiftId, string userId, int departmentId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken));
		Task<Workshift> EditWorkshiftAsync(Workshift workshift, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken));
	}
}

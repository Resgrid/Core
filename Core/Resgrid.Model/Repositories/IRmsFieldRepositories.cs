using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Work assignments (RMS-1D, registry M0179).</summary>
	public interface IRmsRecordWorkAssignmentsRepository : IRepository<RmsRecordWorkAssignment>
	{
		Task<RmsRecordWorkAssignment> GetByIdForDepartmentAsync(int departmentId, string assignmentId);

		Task<IEnumerable<RmsRecordWorkAssignment>> GetForRecordAsync(int departmentId, string recordId);

		/// <summary>Open/acknowledged assignments addressed to the person, any of the units, any of the groups, or any of the roles.</summary>
		Task<IEnumerable<RmsRecordWorkAssignment>> GetOpenForAssigneesAsync(int departmentId, string userId, IEnumerable<int> unitIds, IEnumerable<int> groupIds, IEnumerable<string> roles, int take);

		/// <summary>Assignments modified after the cursor, oldest first, for the sync delta.</summary>
		Task<IEnumerable<RmsRecordWorkAssignment>> GetModifiedSinceAsync(int departmentId, DateTime? since, int take);
	}
}

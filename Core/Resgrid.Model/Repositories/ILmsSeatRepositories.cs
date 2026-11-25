using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>LMS seats (M0366): one row per LMS-enabled member per department.</summary>
	public interface ILmsMemberSeatsRepository : IRepository<LmsMemberSeat>
	{
		/// <summary>The department's seat rows, oldest first (the licensing order).</summary>
		Task<List<LmsMemberSeat>> GetForDepartmentAsync(int departmentId);

		/// <summary>Adds a seat unless the member already holds one (a repeat is not an error); true when a row was written.</summary>
		Task<bool> AddAsync(LmsMemberSeat seat, CancellationToken cancellationToken = default);

		/// <summary>Removes the members' seats in this department; the rows removed.</summary>
		Task<int> RemoveAsync(int departmentId, IEnumerable<string> userIds, CancellationToken cancellationToken = default);
	}

	/// <summary>What Core last saw of each department's LMS add-on (M0367).</summary>
	public interface ILmsAddonStatesRepository : IRepository<LmsAddonState>
	{
		Task<LmsAddonState> GetForDepartmentAsync(int departmentId);

		/// <summary>Inserts or replaces the department's row (one per department; a concurrent insert falls back to an update).</summary>
		Task SaveForDepartmentAsync(LmsAddonState state, CancellationToken cancellationToken = default);
	}

	/// <summary>LMS add-on lapse periods (M0367).</summary>
	public interface ILmsAddonLapsesRepository : IRepository<LmsAddonLapse>
	{
		/// <summary>The department's lapses, oldest first.</summary>
		Task<List<LmsAddonLapse>> GetForDepartmentAsync(int departmentId);

		Task<LmsAddonLapse> GetOpenAsync(int departmentId);

		/// <summary>Opens a lapse unless one is already open (the filtered unique index decides a race); true when this call opened it.</summary>
		Task<bool> OpenAsync(LmsAddonLapse lapse, CancellationToken cancellationToken = default);

		/// <summary>Closes the department's open lapse at <paramref name="endedOnUtc"/>; the closed lapse, or null when none was open.</summary>
		Task<LmsAddonLapse> CloseAsync(int departmentId, DateTime endedOnUtc, CancellationToken cancellationToken = default);
	}
}

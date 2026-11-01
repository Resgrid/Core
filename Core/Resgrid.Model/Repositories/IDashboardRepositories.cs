using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Members' saved dashboard boards (UserDashboards, home-dashboard-redesign-plan.md §5.2).</summary>
	public interface IUserDashboardsRepository : IRepository<UserDashboard>
	{
		/// <summary>The member's boards in the department, home board (lowest SortOrder) first.</summary>
		Task<List<UserDashboard>> GetForUserAsync(int departmentId, string userId);

		Task<UserDashboard> GetByIdForUserAsync(string userDashboardId, int departmentId, string userId);

		/// <summary>
		/// Writes the board's layout, name, template and classic switch and increments RowVersion, only when the stored
		/// RowVersion still equals <paramref name="expectedRowVersion"/>. Returns false on a concurrent change (plan §5.4).
		/// </summary>
		Task<bool> UpdateWithRowVersionAsync(UserDashboard board, int expectedRowVersion, CancellationToken cancellationToken);

		/// <summary>Sets UseClassic on every board the member has in the department.</summary>
		Task<int> SetUseClassicAsync(int departmentId, string userId, bool useClassic, CancellationToken cancellationToken);

		Task<int> DeleteForUserAsync(int departmentId, string userId, CancellationToken cancellationToken);
	}

	/// <summary>Department dashboard templates (DepartmentDashboardTemplates, plan §5.2).</summary>
	public interface IDepartmentDashboardTemplatesRepository : IRepository<DepartmentDashboardTemplate>
	{
		/// <summary>The department's templates ordered by Priority, then Name.</summary>
		Task<List<DepartmentDashboardTemplate>> GetForDepartmentAsync(int departmentId);

		Task<DepartmentDashboardTemplate> GetByIdForDepartmentAsync(string departmentDashboardTemplateId, int departmentId);

		/// <summary>Clears IsDefault on every template of the department except <paramref name="exceptTemplateId"/>.</summary>
		Task<int> ClearDefaultAsync(int departmentId, string exceptTemplateId, CancellationToken cancellationToken);
	}
}

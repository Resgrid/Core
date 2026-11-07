using System.Threading.Tasks;
using Dapper;
using Resgrid.Model.Repositories;

namespace Resgrid.Repositories.DataRepository
{
	public sealed partial class WorkOrderRepository : IWorkOrderCountRepository
	{
		/// <summary>Completed, Closed, Rejected, Duplicate, Cancelled: no longer anyone's work.</summary>
		private static readonly int[] TerminalStatuses = { 5, 6, 7, 8, 9 };

		public async Task<int> CountOpenAssignedAsync(int departmentId, string userId, int[] roleIds)
		{
			var parameters = new DynamicParameters(new { DepartmentId = departmentId, UserId = userId });
			parameters.Add("Roles", InListValue(roleIds == null || roleIds.Length == 0 ? new[] { -1 } : roleIds));
			parameters.Add("Terminal", InListValue(TerminalStatuses));

			// The list's own "assigned to me" scope (AssignmentScope), counted instead of paged.
			var sql = $"SELECT COUNT(*) FROM {Tbl("WorkOrders")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("IsDeleted")}={(IsPostgres ? "false" : "0")} AND NOT ({InList("Status", "Terminal")}) AND {AssignmentScope()}";

			return await RunAsync(connection => connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, parameters, UnitOfWork.Transaction)));
		}
	}
}

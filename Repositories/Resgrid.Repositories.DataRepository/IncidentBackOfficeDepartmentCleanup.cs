using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model.IncidentLogistics;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Incident Back Office subtree of an authorized department deletion (large-incident-logistics-finance-plan.md §18), in the
	/// caller's transaction, after the legal hold checks. Every module table carries DepartmentId and none has a foreign key (the
	/// Incident Command precedent), so the whole catalog deletes by department; the add-on billing account goes with it.
	/// </summary>
	public static class IncidentBackOfficeDepartmentCleanup
	{
		public static readonly string[] Tables = IncidentBackOfficeTables.All.Select(t => t.Table).Concat(new[] { IncidentBackOfficeTables.BillingAccounts }).ToArray();

		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "Incident Back Office");
			await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, Tables, ct);
		}
	}
}

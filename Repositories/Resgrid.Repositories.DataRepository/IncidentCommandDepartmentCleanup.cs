using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Incident Command subtree of an authorized department deletion, in the caller's transaction, after the legal hold
	/// checks. Only incident attachments and notes have foreign keys (to IncidentCommands); everything else hangs off the
	/// command by id, so the commands go last.</summary>
	public static class IncidentCommandDepartmentCleanup
	{
		internal static readonly string[] Tables =
		{
			"IncidentNeedEntities", "IncidentNeedUpdates", "IncidentNeeds", "IncidentMapAnnotations", "IncidentMaps", "IncidentRoleAssignments",
			"IncidentTimers", "IncidentAdHocPersonnel", "IncidentAdHocUnits", "ResourceAssignments", "TacticalObjectives", "CommandTransfers",
			"CommandLogEntries", "CommandStructureNodes"
		};

		/// <summary>Every table this cleanup deletes by DepartmentId.</summary>
		internal static IEnumerable<string> AllTables => Tables.Concat(new[] { "IncidentAttachments", "IncidentNotes", "IncidentCommands" });

		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "Incident Command");
			await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, Tables, ct);
			foreach (var child in new[] { "IncidentAttachments", "IncidentNotes" })
				await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, child, true, ct, ("IncidentCommands", "IncidentCommandId", "IncidentCommandId"));
			await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, new[] { "IncidentCommands" }, ct);
		}
	}
}

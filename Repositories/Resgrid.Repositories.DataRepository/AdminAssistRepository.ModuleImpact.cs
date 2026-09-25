using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Repositories.DataRepository
{
	public sealed partial class AdminAssistRepository
	{
		public async Task<ModuleImpactCounts> ReadModuleImpactCountsAsync(int departmentId, string module, int bound, CancellationToken ct)
		{
			if (departmentId <= 0 || bound < 1 || bound > 10000) throw new ArgumentException("Invalid preview bounds.");
			// Table identifiers are server-authored; no catalog binding or caller text is executed as SQL.
			var table = module switch { "Messaging" => "Messages", "Shifts" => "Shifts", "Documents" => "Documents",
				"Calendar" => "CalendarItems", "Notes" => "Notes", "Training" => "Trainings",
				"Mapping" or "Reports" or "Logs" or "Inventory" => null, _ => throw new ArgumentException("Unsupported module.") };
			async Task<int> Count(string source, string additional = "")
			{
				var select = $"SELECT {(IsPostgres ? "" : "TOP (" + P + "Take) ")}1 AS n FROM {Tbl(source)} WHERE {Col("DepartmentId")}={P}DepartmentId {additional}{(IsPostgres ? " LIMIT " + P + "Take" : "")}";
				var count = await ScalarAsync<int>($"SELECT COUNT(*) FROM ({select}) sample", new { DepartmentId = departmentId, Take = bound + 1, False = false }, ct);
				if (count > bound) throw new InvalidOperationException("Module preview row bound exceeded.");
				return count;
			}
			var people = await Count("DepartmentMembers", $"AND {Col("IsDeleted")}={P}False AND ({Col("IsDisabled")} IS NULL OR {Col("IsDisabled")}={P}False) AND NULLIF(LTRIM(RTRIM({Col("UserId")})),'') IS NOT NULL");
			return new(people, table == null ? null : await Count(table));
		}
	}
}

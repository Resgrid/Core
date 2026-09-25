using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Repositories.DataRepository
{
	public sealed partial class AdminAssistRepository
	{
		public async Task<IReadOnlyList<RetentionImpactHeader>> ReadRetentionHeadersAsync(int departmentId, int bound, CancellationToken ct)
		{
			if (departmentId <= 0 || bound is < 1 or > 250) throw new ArgumentException("Invalid retention preview bounds.");
			var result = new List<RetentionImpactHeader>();
			foreach (var kind in new[] { RmsRecordKind.Operational, RmsRecordKind.IncidentReport })
			{
				var table = kind == RmsRecordKind.Operational ? "RmsOperationalRecords" : "RmsIncidentReports";
				var id = kind == RmsRecordKind.Operational ? "RmsOperationalRecordId" : "RmsIncidentReportId";
				// All hold rows and content bodies stay in the owning database. Historical/child coverage is conservative.
				var directHold = $"EXISTS (SELECT 1 FROM {Tbl("RmsRecordLegalHolds")} h WHERE h.{Col("DepartmentId")}=r.{Col("DepartmentId")} AND h.{Col("ReleasedOn")} IS NULL AND h.{Col("RecordId")}=r.{Col(id)})";
				var stickyHold = $"EXISTS (SELECT 1 FROM {Tbl("RmsRecordLegalHoldMembers")} m JOIN {Tbl("RmsRecordLegalHolds")} h ON h.{Col("DepartmentId")}=m.{Col("DepartmentId")} AND h.{Col("RmsRecordLegalHoldId")}=m.{Col("HoldId")} WHERE m.{Col("DepartmentId")}=r.{Col("DepartmentId")} AND m.{Col("RecordId")}=r.{Col(id)} AND h.{Col("ReleasedOn")} IS NULL)";
				var permanent = string.Join(" OR ", new[] { "RmsCasualtyRescues", "RmsExposures" }.Select(t => $"EXISTS (SELECT 1 FROM {Tbl(t)} p WHERE p.{Col("DepartmentId")}=r.{Col("DepartmentId")} AND p.{Col("RecordId")}=r.{Col(id)})"));
				// An applicable period hold might cover a historical date. Do not read protected historical JSON or call it clear.
				var historicalHold = $"EXISTS (SELECT 1 FROM {Tbl("RmsRecordLegalHolds")} h WHERE h.{Col("DepartmentId")}=r.{Col("DepartmentId")} AND h.{Col("ReleasedOn")} IS NULL AND h.{Col("RecordId")} IS NULL AND (h.{Col("DefinitionKey")} IS NULL OR h.{Col("DefinitionKey")}=r.{Col("DefinitionKey")}))";
				var select = $"SELECT {(IsPostgres ? "" : "TOP (" + P + "Take) ")}r.{Col(id)} AS {Col("RecordId")}, {(int)kind} AS {Col("Kind")}, {string.Join(",", new[] { "DefinitionKey", "State", "FinalizedOn", "ModifiedOn", "AmendsRevisionId", "RowVersion" }.Select(c => "r." + Col(c)))}, CASE WHEN {directHold} OR {stickyHold} OR {permanent} THEN 1 ELSE 0 END AS {Col("HoldOrPermanentContent")}, CASE WHEN {historicalHold} THEN 1 ELSE 0 END AS {Col("HistoricalHoldUncertainty")} FROM {Tbl(table)} r WHERE r.{Col("DepartmentId")}={P}DepartmentId AND r.{Col("PurgedOn")} IS NULL ORDER BY r.{Col(id)}{(IsPostgres ? " LIMIT " + P + "Take" : "")}";
				result.AddRange(await QueryAsync<RetentionImpactHeader>(select, new { DepartmentId = departmentId, Take = bound + 1 }, ct));
			}
			return result;
		}
	}
}

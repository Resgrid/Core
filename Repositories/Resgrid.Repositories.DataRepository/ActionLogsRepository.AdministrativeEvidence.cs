using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;

namespace Resgrid.Repositories.DataRepository
{
	public partial class ActionLogsRepository
	{
		public async Task<IReadOnlyList<ActionLog>> ReadLatestForAdministrationAsync(int departmentId, bool disableAutoAvailable, DateTime asOfUtc, int maximumRows, CancellationToken ct)
		{
			if (departmentId <= 0 || asOfUtc.Kind != DateTimeKind.Utc || maximumRows < 1 || maximumRows > 10000) throw new ArgumentException("Invalid administrative status scope.");
			var postgres = Config.DataConfig.DatabaseType == Config.DatabaseTypes.Postgres;
			string Q(string name) => postgres ? name.Trim('[', ']', '"').ToLowerInvariant() : "[" + name.Trim('[', ']', '"') + "]";
			string Table(string name) => Q(_sqlConfiguration.SchemaName) + "." + Q(name);
			var columns = new[] { "ActionLogId", "UserId", "DepartmentId", "ActionTypeId", "Timestamp", "GeoLocationData" };
			var sql = $@"WITH selected AS (
SELECT {string.Join(",", columns.Select(c => "al." + Q(c)))}, ROW_NUMBER() OVER (PARTITION BY al.{Q("UserId")} ORDER BY al.{Q("ActionLogId")} DESC) AS position
FROM {Table("ActionLogs")} al
INNER JOIN {Table("AspNetUsers")} u ON u.{Q("Id")}=al.{Q("UserId")}
INNER JOIN {Table("DepartmentMembers")} dm ON dm.{Q("UserId")}=al.{Q("UserId")} AND dm.{Q("DepartmentId")}=al.{Q("DepartmentId")}
WHERE al.{Q("DepartmentId")}=@DepartmentId AND dm.{Q("IsDeleted")}=@False AND dm.{Q("IsDisabled")}=@False AND dm.{Q("IsHidden")}=@False
AND al.{Q("Timestamp")}>=@Earliest AND (@DisableAutoAvailable=@True OR al.{Q("Timestamp")}>=@Threshold))
SELECT {(postgres ? "" : "TOP (@Take) ")}{string.Join(",", columns.Select(Q))} FROM selected WHERE position=1 ORDER BY {Q("UserId")} {(postgres ? "LIMIT @Take" : "")}";
			DateTime Stamp(DateTime value) => postgres ? DateTime.SpecifyKind(value, DateTimeKind.Unspecified) : value;
			var args = new { DepartmentId = departmentId, DisableAutoAvailable = disableAutoAvailable, False = false, True = true,
				Earliest = Stamp(asOfUtc.AddYears(-1)), AsOfUtc = Stamp(asOfUtc), Threshold = Stamp(asOfUtc.AddHours(-1)), Take = maximumRows + 1 };
			var owns = _unitOfWork.Connection == null;
			var connection = owns ? _connectionProvider.Create() : _unitOfWork.Connection;
			try
			{
				if (owns) await connection.OpenAsync(ct);
				var rows = (await connection.QueryAsync<ActionLog>(new Dapper.CommandDefinition(sql, args, _unitOfWork.Transaction, commandTimeout: 20, cancellationToken: ct))).ToList();
				if (rows.Count > maximumRows) throw new InvalidOperationException("Administrative status row bound exceeded.");
				return rows;
			}
			finally { if (owns) await connection.DisposeAsync(); }
		}
	}
}

using System;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <inheritdoc cref="IBadgeCountsRepository" />
	public class BadgeCountsRepository : IBadgeCountsRepository
	{
		/// <summary>Planned, Standby, Active, Demobilizing (Deployment.IsOpen).</summary>
		private const string OpenDeploymentStatuses = "(0, 1, 2, 3)";

		/// <summary>InProgress, AwaitingWitness, Scheduled, Missed: the states the due list shows.</summary>
		private const string DueChecklistStates = "(0, 1, 3, 4)";

		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public BadgeCountsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		private static bool IsPostgres => DataConfig.DatabaseType == DatabaseTypes.Postgres;

		private string P => _sqlConfiguration.ParameterNotation;

		private string Tbl(string name) => IsPostgres ? $"{_sqlConfiguration.SchemaName}.{name.ToLowerInvariant()}" : $"{_sqlConfiguration.SchemaName}.[{name}]";

		private static string Col(string alias, string name) => IsPostgres ? $"{alias}.{name.ToLowerInvariant()}" : $"{alias}.[{name}]";

		private static string False => IsPostgres ? "false" : "0";

		private static string True => IsPostgres ? "true" : "1";

		/// <summary>The user id parameter, compared case-insensitively on PostgreSQL (citext) as SQL Server's collation does.</summary>
		private string User => IsPostgres ? $"{P}UserId::citext" : $"{P}UserId";

		/// <summary>An integer column as text, to compare with string assignment ids.</summary>
		private static string AsText(string column) => IsPostgres ? $"CAST({column} AS TEXT)" : $"CAST({column} AS NVARCHAR(32))";

		public async Task<CertificationAttentionCounts> GetCertificationAttentionAsync(int departmentId, string userId, DateTime today, DateTime horizonEnd)
		{
			// Revoked records are history; a type that never expires has no expiry to watch.
			var sql = $@"SELECT
				COALESCE(SUM(CASE WHEN {Col("c", "Status")} = 1 OR {Col("c", "ExpiresOn")} < {P}Today THEN 1 ELSE 0 END), 0) AS CountA,
				COALESCE(SUM(CASE WHEN {Col("c", "Status")} <> 1 AND {Col("c", "ExpiresOn")} >= {P}Today AND {Col("c", "ExpiresOn")} < {P}HorizonEnd THEN 1 ELSE 0 END), 0) AS CountB
				FROM {Tbl("PersonnelCertifications")} c
				LEFT JOIN {Tbl("DepartmentCertificationTypes")} t ON {Col("t", "DepartmentCertificationTypeId")} = {Col("c", "DepartmentCertificationTypeId")}
				WHERE {Col("c", "DepartmentId")} = {P}DepartmentId AND {Col("c", "UserId")} = {User} AND {Col("c", "IsDeleted")} = {False}
				AND {Col("c", "Status")} <> 3 AND ({Col("c", "ExpiresOn")} IS NOT NULL OR {Col("c", "Status")} = 1)
				AND ({Col("t", "NeverExpires")} IS NULL OR {Col("t", "NeverExpires")} = {False})";

			var row = await QueryFirstAsync<CountPair>(sql, new { DepartmentId = departmentId, UserId = userId, Today = today.Date, HorizonEnd = horizonEnd.Date.AddDays(1) });

			return new CertificationAttentionCounts { Expired = ToInt(row?.CountA), Expiring = ToInt(row?.CountB) };
		}

		public Task<int> CountOpenDeploymentsForUserAsync(int departmentId, string userId)
		{
			var sql = $@"SELECT COUNT(DISTINCT {Col("d", "DeploymentId")})
				FROM {Tbl("DeploymentPersonnel")} p
				INNER JOIN {Tbl("Deployments")} d ON {Col("d", "DeploymentId")} = {Col("p", "DeploymentId")}
				WHERE {Col("p", "DepartmentId")} = {P}DepartmentId AND {Col("p", "UserId")} = {User} AND {Col("p", "RemovedOn")} IS NULL
				AND {Col("d", "DepartmentId")} = {P}DepartmentId AND {Col("d", "IsDeleted")} = {False} AND {Col("d", "Status")} IN {OpenDeploymentStatuses}";

			return ScalarAsync(sql, new { DepartmentId = departmentId, UserId = userId });
		}

		public Task<int> CountDueChecklistsAsync(int departmentId, string userId, int? unitId, DateTime nowUtc)
		{
			// The due list's rules (ChecklistsService.MobileDueAsync) without its per-row reads: an active, unsuspended
			// schedule assigned to the member (anyone / them / a role they hold / their group / a unit they crew), and no run
			// someone else started. Target visibility beyond that (a unit or person the member may not view) is the list's
			// own check and is not repeated here.
			var assigned = $@"(
				{Col("s", "AssignmentType")} = 0
				OR ({Col("s", "AssignmentType")} = 1 AND {Col("s", "AssignmentId")} = {User})
				OR ({Col("s", "AssignmentType")} = 2 AND EXISTS (SELECT 1 FROM {Tbl("PersonnelRoleUsers")} ru WHERE {Col("ru", "UserId")} = {User} AND {Col("ru", "DepartmentId")} = {P}DepartmentId AND {AsText(Col("ru", "PersonnelRoleId"))} = {Col("s", "AssignmentId")}))
				OR ({Col("s", "AssignmentType")} = 3 AND EXISTS (SELECT 1 FROM {Tbl("DepartmentGroupMembers")} gm WHERE {Col("gm", "UserId")} = {User} AND {Col("gm", "DepartmentId")} = {P}DepartmentId AND {AsText(Col("gm", "DepartmentGroupId"))} = {Col("s", "AssignmentId")}))
				OR ({Col("s", "AssignmentType")} = 4 AND EXISTS (SELECT 1 FROM {Tbl("UnitActiveRoles")} ar WHERE {Col("ar", "UserId")} = {User} AND {AsText(Col("ar", "UnitId"))} = {Col("s", "AssignmentId")})))";

			// Unit app: the unit's own checks. Otherwise everything but other members' personal checks.
			var target = unitId.HasValue
				? $"{Col("o", "TargetType")} = 1 AND {Col("o", "TargetId")} = {P}UnitId"
				: $"NOT ({Col("o", "TargetType")} = 3 AND {Col("o", "TargetId")} <> {User})";

			var sql = $@"SELECT COUNT(*)
				FROM {Tbl("ChecklistOccurrences")} o
				INNER JOIN {Tbl("ChecklistSchedules")} s ON {Col("s", "DepartmentId")} = {Col("o", "DepartmentId")} AND {Col("s", "Id")} = {Col("o", "ScheduleId")}
				LEFT JOIN {Tbl("ChecklistCompletions")} c ON {Col("c", "DepartmentId")} = {Col("o", "DepartmentId")} AND {Col("c", "Id")} = {Col("o", "CompletionId")}
				WHERE {Col("o", "DepartmentId")} = {P}DepartmentId AND {Col("o", "ScheduleId")} IS NOT NULL AND {Col("o", "State")} IN {DueChecklistStates}
				AND {Col("o", "PeriodStartUtc")} <= {P}Now
				AND {Col("s", "IsActive")} = {True} AND {Col("s", "IsSuspended")} = {False}
				AND ({Col("c", "Id")} IS NULL OR {Col("c", "CreatedBy")} = {User})
				AND {assigned}
				AND {target}";

			return ScalarAsync(sql, new { DepartmentId = departmentId, UserId = userId, UnitId = unitId?.ToString(), Now = nowUtc });
		}

		public Task<int> CountShiftDaysForUserAsync(int departmentId, string userId, DateTime dayStart, DateTime dayEnd)
		{
			// ShiftRosterBuilder's rules as EXISTS clauses. A trade is complete when someone was picked (a person or a swap
			// target) and it is neither denied nor waiting for approval.
			string completed(string alias) => $"{Col(alias, "Denied")} = {False} AND {Col(alias, "ApprovalPending")} = {False} AND ({Col(alias, "UserId")} IS NOT NULL OR {Col(alias, "TargetShiftSignupId")} IS NOT NULL)";
			string onDay(string alias, string column) => $"{Col(alias, column)} >= {P}DayStart AND {Col(alias, column)} < {P}DayEnd";

			// The member's own signup for the day, not denied and not handed to someone else (as source or swap target).
			var ownSignup = $@"EXISTS (SELECT 1 FROM {Tbl("ShiftSignups")} ss
				WHERE {Col("ss", "ShiftId")} = {Col("sd", "ShiftId")} AND {Col("ss", "UserId")} = {User} AND {Col("ss", "Denied")} = {False} AND {onDay("ss", "ShiftDay")}
				AND NOT EXISTS (SELECT 1 FROM {Tbl("ShiftSignupTrades")} t1 WHERE {Col("t1", "SourceShiftSignupId")} = {Col("ss", "ShiftSignupId")} AND {completed("t1")})
				AND NOT EXISTS (SELECT 1 FROM {Tbl("ShiftSignupTrades")} t2 WHERE {Col("t2", "TargetShiftSignupId")} = {Col("ss", "ShiftSignupId")} AND {completed("t2")}))";

			// A standing assignment, unless the member is off that day (a denied signup, or their signup traded away).
			// Aliases must not be reserved words: OFF is reserved in T-SQL and failed every SQL Server badge request.
			var standing = $@"(EXISTS (SELECT 1 FROM {Tbl("ShiftPersons")} sp WHERE {Col("sp", "ShiftId")} = {Col("sd", "ShiftId")} AND {Col("sp", "UserId")} = {User})
				AND NOT EXISTS (SELECT 1 FROM {Tbl("ShiftSignups")} sx
					WHERE {Col("sx", "ShiftId")} = {Col("sd", "ShiftId")} AND {Col("sx", "UserId")} = {User} AND {onDay("sx", "ShiftDay")}
					AND ({Col("sx", "Denied")} = {True}
						OR EXISTS (SELECT 1 FROM {Tbl("ShiftSignupTrades")} t3 WHERE {Col("t3", "SourceShiftSignupId")} = {Col("sx", "ShiftSignupId")} AND {completed("t3")}))))";

			// A day taken over through a completed trade: picked for it, or swapped into it.
			var tradedIn = $@"EXISTS (SELECT 1 FROM {Tbl("ShiftSignupTrades")} t4
				INNER JOIN {Tbl("ShiftSignups")} src ON {Col("src", "ShiftSignupId")} = {Col("t4", "SourceShiftSignupId")}
				LEFT JOIN {Tbl("ShiftSignups")} tgt ON {Col("tgt", "ShiftSignupId")} = {Col("t4", "TargetShiftSignupId")}
				WHERE {completed("t4")} AND (
					({Col("src", "ShiftId")} = {Col("sd", "ShiftId")} AND {onDay("src", "ShiftDay")} AND ({Col("t4", "UserId")} = {User} OR ({Col("t4", "UserId")} IS NULL AND {Col("tgt", "UserId")} = {User})))
					OR ({Col("tgt", "ShiftId")} = {Col("sd", "ShiftId")} AND {onDay("tgt", "ShiftDay")} AND {Col("src", "UserId")} = {User})))";

			var sql = $@"SELECT COUNT(*)
				FROM {Tbl("ShiftDays")} sd
				INNER JOIN {Tbl("Shifts")} sh ON {Col("sh", "ShiftId")} = {Col("sd", "ShiftId")}
				WHERE {Col("sh", "DepartmentId")} = {P}DepartmentId AND {onDay("sd", "Day")}
				AND ({ownSignup} OR {standing} OR {tradedIn})";

			return ScalarAsync(sql, new { DepartmentId = departmentId, UserId = userId, DayStart = dayStart, DayEnd = dayEnd });
		}

		public async Task<ShiftTradeCounts> GetShiftTradeCountsAsync(int departmentId, string userId, DateTime fromDay)
		{
			// Department and day come from the trade, or from its source signup's shift on rows written before they were.
			var scope = $@"({Col("t", "DepartmentId")} = {P}DepartmentId OR {Col("sh", "DepartmentId")} = {P}DepartmentId)
				AND COALESCE({Col("ss", "ShiftDay")}, {Col("t", "SourceDay")}) >= {P}FromDay";
			var from = $@"FROM {Tbl("ShiftSignupTrades")} t
				LEFT JOIN {Tbl("ShiftSignups")} ss ON {Col("ss", "ShiftSignupId")} = {Col("t", "SourceShiftSignupId")}
				LEFT JOIN {Tbl("Shifts")} sh ON {Col("sh", "ShiftId")} = {Col("ss", "ShiftId")}";
			var open = $"{Col("t", "UserId")} IS NULL AND {Col("t", "TargetShiftSignupId")} IS NULL";

			var sql = $@"SELECT
				(SELECT COUNT(DISTINCT {Col("t", "ShiftSignupTradeId")}) {from}
					INNER JOIN {Tbl("ShiftSignupTradeUsers")} tu ON {Col("tu", "ShiftSignupTradeId")} = {Col("t", "ShiftSignupTradeId")}
					WHERE {Col("tu", "UserId")} = {User} AND {Col("tu", "Declined")} = {False} AND {Col("tu", "Offered")} = {False}
					AND {Col("t", "Denied")} = {False} AND {open} AND {scope}) AS CountA,
				(SELECT COUNT(*) {from}
					WHERE ({Col("ss", "UserId")} = {User} OR {Col("t", "SourceUserId")} = {User})
					AND {Col("t", "Denied")} = {False} AND (({open}) OR {Col("t", "ApprovalPending")} = {True}) AND {scope}) AS CountB";

			var row = await QueryFirstAsync<CountPair>(sql, new { DepartmentId = departmentId, UserId = userId, FromDay = fromDay.Date });

			return new ShiftTradeCounts { AwaitingMe = ToInt(row?.CountA), Mine = ToInt(row?.CountB) };
		}

		/// <summary>Two counts from one statement.</summary>
		private sealed class CountPair
		{
			public long? CountA { get; set; }

			public long? CountB { get; set; }
		}

		private static int ToInt(long? value) => (int)Math.Min(int.MaxValue, Math.Max(0, value ?? 0));

		private async Task<int> ScalarAsync(string sql, object parameters)
		{
			var value = await RunAsync(connection => connection.ExecuteScalarAsync<long?>(new Dapper.CommandDefinition(sql, parameters, _unitOfWork.Transaction)));

			return ToInt(value);
		}

		private Task<T> QueryFirstAsync<T>(string sql, object parameters) =>
			RunAsync(connection => connection.QueryFirstOrDefaultAsync<T>(new Dapper.CommandDefinition(sql, parameters, _unitOfWork.Transaction)));

		private async Task<T> RunAsync<T>(Func<DbConnection, Task<T>> work)
		{
			try
			{
				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await work(connection);
				}

				return await work(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}
}

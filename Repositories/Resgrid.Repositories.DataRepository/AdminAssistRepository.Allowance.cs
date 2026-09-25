using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Admin Assist free allowance on AiUsageLedger (enhanced-ai-addon-plan.md §5.4; columns from M0238). Shares the M0237
	/// admission lock and live-reservation rules with the paid path; free turns may hold at most one of the two slots.
	/// </summary>
	public sealed partial class AdminAssistRepository : IAiFreeAllowanceStore
	{
		private const int AdmissionLiveLimit = 2;
		private const int FreeLiveLimit = 1;
		private static readonly DateTime LedgerEpochUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		// Rows written before M0238 carry no Feature; every such row is an Admin Assist turn.
		private static string AdminAssistRows => $"({Col("Feature")}='AdminAssist' OR {Col("Feature")} IS NULL)";

		public async Task<DateTime?> GetFirstAnsweredAsync(int departmentId, CancellationToken ct)
		{
			if (departmentId <= 0) return null;
			var first = await ScalarAsync<DateTime?>($"SELECT MIN({Col("CreatedOnUtc")}) FROM {Tbl("AiUsageLedger")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {AdminAssistRows} AND {Col("Outcome")}='Answered' AND {Col("CreatedOnUtc")} IS NOT NULL",
				new { DepartmentId = departmentId }, ct);
			return first.HasValue ? DateTime.SpecifyKind(first.Value, DateTimeKind.Utc) : null;
		}

		public async Task<AiFreeUsage> GetFreeUsageAsync(int departmentId, AdminAssistFreeWindow window, DateTime nowUtc, CancellationToken ct)
		{
			if (departmentId <= 0 || window == null || nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Invalid allowance read.");
			var args = FreeArgs(departmentId, window, nowUtc);
			return new AiFreeUsage(await ScalarAsync<int>(CountAnsweredSql, args, ct), await ScalarAsync<int>(CountAttemptsSql, args, ct));
		}

		public async Task<AiFreeReservationResult> ReserveFreeAsync(AdminAssistActor actor, DateTime nowUtc, int tokens, AdminAssistFreeWindow window, int dailyAttemptLimit, CancellationToken ct)
		{
			if (actor == null || actor.DepartmentId <= 0 || string.IsNullOrWhiteSpace(actor.UserId) || tokens is < 1 or > 32768 || nowUtc.Kind != DateTimeKind.Utc || window == null)
				throw new ArgumentException("Invalid reservation.");
			if (UnitOfWork.Transaction != null) throw new InvalidOperationException("Admission owns its short transaction.");
			await UnitOfWork.CreateOrGetConnectionAsync(ct);
			try
			{
				var sql = IsPostgres ? $"SELECT {Col("Id")} FROM {Tbl("AiAdmission")} WHERE {Col("Id")}=1 FOR UPDATE" : $"SELECT {Col("Id")} FROM {Tbl("AiAdmission")} WITH (UPDLOCK,HOLDLOCK) WHERE {Col("Id")}=1";
				if (await ScalarAsync<int>(sql, null, ct) != 1) throw new InvalidOperationException("Admission not provisioned.");
				var live = $"{Col("Outcome")} IS NULL AND {Col("ExpiresOnUtc")}>{P}Now";
				var args = FreeArgs(actor.DepartmentId, window, nowUtc);
				string reason = null;
				if (await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("AiUsageLedger")} WHERE {live}", args, ct) >= AdmissionLiveLimit ||
					await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("AiUsageLedger")} WHERE {live} AND {Col("Tier")}='Free'", args, ct) >= FreeLiveLimit ||
					await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("AiUsageLedger")} WHERE {live} AND {Col("DepartmentId")}={P}DepartmentId", args, ct) > 0)
					reason = "Busy";
				else if (await ScalarAsync<int>(CountAnsweredSql, args, ct) >= window.Allowance)
					reason = "FreeAllowanceExhausted";
				else if (await ScalarAsync<int>(CountAttemptsSql, args, ct) >= Math.Max(0, dailyAttemptLimit))
					reason = "FreeAttemptLimit";
				if (reason != null) { UnitOfWork.DiscardChanges(); return new AiFreeReservationResult(null, reason); }

				var reservation = new AiUsageReservation(Guid.NewGuid().ToString("D"), actor.DepartmentId, actor.UserId, tokens, nowUtc.AddSeconds(120));
				await ExecuteAsync($"INSERT INTO {Tbl("AiUsageLedger")} ({Cols("Id", "DepartmentId", "UserId", "Month", "ReservedTokens", "ExpiresOnUtc", "Feature", "Tier", "CreatedOnUtc")}) VALUES ({P}Id,{P}DepartmentId,{P}UserId,{P}Month,{P}Tokens,{P}Expires,'AdminAssist','Free',{P}Now)",
					new { reservation.Id, reservation.DepartmentId, reservation.UserId, Month = nowUtc.ToString("yyyy-MM", CultureInfo.InvariantCulture), reservation.Tokens, Expires = DatabaseTimestamp(reservation.ExpiresOnUtc), Now = DatabaseTimestamp(nowUtc) }, ct);
				UnitOfWork.CommitChanges();
				return new AiFreeReservationResult(reservation, "Reserved");
			}
			catch { UnitOfWork.DiscardChanges(); throw; }
		}

		// Only answered questions are charged; an in-flight turn holds its question until it completes.
		private string CountAnsweredSql =>
			$"SELECT COUNT(*) FROM {Tbl("AiUsageLedger")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Tier")}='Free' AND {Col("CreatedOnUtc")}>={P}Start AND {Col("CreatedOnUtc")}<{P}End " +
			$"AND ({Col("Outcome")}='Answered' OR ({Col("Outcome")} IS NULL AND {Col("ExpiresOnUtc")}>{P}Now))";

		private string CountAttemptsSql =>
			$"SELECT COUNT(*) FROM {Tbl("AiUsageLedger")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Tier")}='Free' AND {Col("CreatedOnUtc")}>={P}Since";

		private static object FreeArgs(int departmentId, AdminAssistFreeWindow window, DateTime nowUtc) => new
		{
			DepartmentId = departmentId,
			Start = DatabaseTimestamp(window.StartUtc < LedgerEpochUtc ? LedgerEpochUtc : window.StartUtc),
			End = DatabaseTimestamp(window.EndUtc),
			Now = DatabaseTimestamp(nowUtc),
			Since = DatabaseTimestamp(nowUtc.AddHours(-24))
		};
	}
}

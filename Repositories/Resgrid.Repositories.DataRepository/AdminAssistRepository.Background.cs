using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Background admission on the shared AI ledger (enhanced-ai-addon-plan.md §5). Background work such as AI dispatch Enrich mode
	/// starts only when no turn at all is live, so interactive Admin Assist and assistant turns always go first.
	/// </summary>
	public sealed partial class AdminAssistRepository : IAiBackgroundAdmission
	{
		private const string BackgroundUserId = "system:background";

		public async Task<AiUsageReservation> ReserveBackgroundAsync(int departmentId, string feature, string tier, DateTime nowUtc, int tokens, int monthlyLimit,
			CancellationToken ct, int? featureMonthlyLimit = null)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(feature) || feature.Length > 40 || string.IsNullOrWhiteSpace(tier) || tier.Length > 20 ||
				tokens is < 1 or > 32768 || nowUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException("Invalid background reservation.");
			if (UnitOfWork.Transaction != null) throw new InvalidOperationException("Admission owns its short transaction.");
			await UnitOfWork.CreateOrGetConnectionAsync(ct);
			try
			{
				var sql = IsPostgres ? $"SELECT {Col("Id")} FROM {Tbl("AiAdmission")} WHERE {Col("Id")}=1 FOR UPDATE" : $"SELECT {Col("Id")} FROM {Tbl("AiAdmission")} WITH (UPDLOCK,HOLDLOCK) WHERE {Col("Id")}=1";
				if (await ScalarAsync<int>(sql, null, ct) != 1) throw new InvalidOperationException("Admission not provisioned.");
				var args = new { Now = DatabaseTimestamp(nowUtc), DepartmentId = departmentId };
				if (await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("AiUsageLedger")} WHERE {Col("Outcome")} IS NULL AND {Col("ExpiresOnUtc")}>{P}Now", args, ct) > 0 ||
					await RemainingAsync(departmentId, nowUtc, monthlyLimit, ct) < tokens ||
					featureMonthlyLimit.HasValue && await GetFeatureUsageAsync(departmentId, feature, nowUtc, ct) + tokens > featureMonthlyLimit.Value) { UnitOfWork.DiscardChanges(); return null; }
				var reservation = new AiUsageReservation(Guid.NewGuid().ToString("D"), departmentId, BackgroundUserId, tokens, nowUtc.AddSeconds(Math.Max(120, Resgrid.Config.AiDispatchConfig.RequestTimeoutSeconds + 30)));
				await ExecuteAsync($"INSERT INTO {Tbl("AiUsageLedger")} ({Cols("Id", "DepartmentId", "UserId", "Month", "ReservedTokens", "ExpiresOnUtc", "Feature", "Tier", "CreatedOnUtc")}) VALUES ({P}Id,{P}DepartmentId,{P}UserId,{P}Month,{P}Tokens,{P}Expires,{P}Feature,{P}Tier,{P}Now)",
					new { reservation.Id, reservation.DepartmentId, reservation.UserId, Month = nowUtc.ToString("yyyy-MM", CultureInfo.InvariantCulture), reservation.Tokens,
						Expires = DatabaseTimestamp(reservation.ExpiresOnUtc), Feature = feature, Tier = tier, Now = DatabaseTimestamp(nowUtc) }, ct);
				UnitOfWork.CommitChanges();
				return reservation;
			}
			catch { UnitOfWork.DiscardChanges(); throw; }
		}

		public Task<long> GetFeatureUsageAsync(int departmentId, string feature, DateTime nowUtc, CancellationToken ct) =>
			ScalarAsync<long>($"SELECT COALESCE(SUM(CAST(COALESCE({Col("UsedTokens")},{Col("ReservedTokens")}) AS BIGINT)),0) FROM {Tbl("AiUsageLedger")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Feature")}={P}Feature AND {Col("Month")}={P}Month",
				new { DepartmentId = departmentId, Feature = feature, Month = nowUtc.ToString("yyyy-MM", CultureInfo.InvariantCulture) }, ct);
	}
}

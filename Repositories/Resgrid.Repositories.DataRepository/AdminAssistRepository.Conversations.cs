using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Repositories.DataRepository
{
	public sealed partial class AdminAssistRepository
	{
		private sealed class AiPolicy { public bool Disabled { get; set; } public int? MonthlyTokenLimit { get; set; } }
		public async Task<bool> IsDisabledAsync(int departmentId, CancellationToken ct) =>
			(await QueryAsync<AiPolicy>($"SELECT {Cols("Disabled", "MonthlyTokenLimit")} FROM {Tbl("DepartmentAiConfigs")} WHERE {Col("DepartmentId")}={P}DepartmentId", new { DepartmentId = departmentId }, ct)).SingleOrDefault()?.Disabled == true;
		public async Task<long> RemainingAsync(int departmentId, DateTime now, int monthlyLimit, CancellationToken ct)
		{
			if (departmentId <= 0 || now.Kind != DateTimeKind.Utc || monthlyLimit <= 0) return 0;
			var policy = (await QueryAsync<AiPolicy>($"SELECT {Cols("Disabled", "MonthlyTokenLimit")} FROM {Tbl("DepartmentAiConfigs")} WHERE {Col("DepartmentId")}={P}DepartmentId", new { DepartmentId = departmentId }, ct)).SingleOrDefault();
			if (policy?.Disabled == true) return 0;
			var limit = Math.Min(monthlyLimit, Math.Max(0, policy?.MonthlyTokenLimit ?? monthlyLimit));
			var used = await ScalarAsync<long>($"SELECT COALESCE(SUM(CAST(COALESCE({Col("UsedTokens")},{Col("ReservedTokens")}) AS BIGINT)),0) FROM {Tbl("AiUsageLedger")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Month")}={P}Month", new { DepartmentId = departmentId, Month = now.ToString("yyyy-MM", CultureInfo.InvariantCulture) }, ct);
			return Math.Max(0, limit - used);
		}
		public async Task<AiUsageReservation> ReserveAsync(AdminAssistActor actor, DateTime now, int tokens, int monthlyLimit, CancellationToken ct)
		{
			if (actor.DepartmentId <= 0 || string.IsNullOrWhiteSpace(actor.UserId) || tokens is < 1 or > 32768 || now.Kind != DateTimeKind.Utc) throw new ArgumentException("Invalid reservation.");
			if (UnitOfWork.Transaction != null) throw new InvalidOperationException("Admission owns its short transaction.");
			await UnitOfWork.CreateOrGetConnectionAsync(ct);
			try
			{
				var sql = IsPostgres ? $"SELECT {Col("Id")} FROM {Tbl("AiAdmission")} WHERE {Col("Id")}=1 FOR UPDATE" : $"SELECT {Col("Id")} FROM {Tbl("AiAdmission")} WITH (UPDLOCK,HOLDLOCK) WHERE {Col("Id")}=1";
				if (await ScalarAsync<int>(sql, null, ct) != 1) throw new InvalidOperationException("Admission not provisioned.");
				var live = $"{Col("Outcome")} IS NULL AND {Col("ExpiresOnUtc")}>{P}Now";
				var args = new { Now = DatabaseTimestamp(now), DepartmentId = actor.DepartmentId };
				if (await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("AiUsageLedger")} WHERE {live}", args, ct) >= 2 ||
					await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("AiUsageLedger")} WHERE {live} AND {Col("DepartmentId")}={P}DepartmentId", args, ct) > 0 ||
					await RemainingAsync(actor.DepartmentId, now, monthlyLimit, ct) < tokens) { UnitOfWork.DiscardChanges(); return null; }
				var reservation = new AiUsageReservation(Guid.NewGuid().ToString("D"), actor.DepartmentId, actor.UserId, tokens, now.AddSeconds(120));
				// Feature/Tier/CreatedOnUtc (M0238) let the free allowance find a department's first answered question on any tier.
				await ExecuteAsync($"INSERT INTO {Tbl("AiUsageLedger")} ({Cols("Id", "DepartmentId", "UserId", "Month", "ReservedTokens", "ExpiresOnUtc", "Feature", "Tier", "CreatedOnUtc")}) VALUES ({P}Id,{P}DepartmentId,{P}UserId,{P}Month,{P}Tokens,{P}Expires,'AdminAssist','EnhancedAi',{P}Created)",
					new { reservation.Id, reservation.DepartmentId, reservation.UserId, Month = now.ToString("yyyy-MM", CultureInfo.InvariantCulture), reservation.Tokens, Expires = DatabaseTimestamp(reservation.ExpiresOnUtc), Created = DatabaseTimestamp(now) }, ct);
				UnitOfWork.CommitChanges(); return reservation;
			}
			catch { UnitOfWork.DiscardChanges(); throw; }
		}
		public async Task CompleteAsync(AiUsageReservation reservation, int tokens, string outcome, CancellationToken ct)
		{
			if (tokens < 0 || tokens > reservation.Tokens || outcome is not ("Answered" or "Abstained" or "Unavailable" or "Cancelled" or "InvalidOutput")) throw new ArgumentException("Invalid usage completion.");
			await ExecuteAsync($"UPDATE {Tbl("AiUsageLedger")} SET {Col("UsedTokens")}={P}Tokens,{Col("Outcome")}={P}Outcome WHERE {Col("Id")}={P}Id AND {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId AND {Col("Outcome")} IS NULL",
				new { reservation.Id, reservation.DepartmentId, reservation.UserId, Tokens = tokens, Outcome = outcome }, ct);
		}
		private sealed class ConversationHeader { public long Revision { get; set; } public bool Deleted { get; set; } }
		private async Task<ConversationHeader> ConversationAsync(AdminAssistActor actor, string id, CancellationToken ct) =>
			(await QueryAsync<ConversationHeader>($"SELECT {Cols("Revision", "Deleted")} FROM {Tbl("AdminAssistConversations")} WHERE {Col("Id")}={P}Id AND {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId",
				new { Id = id, actor.DepartmentId, actor.UserId }, ct)).SingleOrDefault();
		public async Task<long> GetRevisionAsync(AdminAssistActor actor, string conversationId, CancellationToken ct)
		{
			var header = await ConversationAsync(actor, conversationId, ct);
			if (header == null || header.Deleted) throw new UnauthorizedAccessException();
			return header.Revision;
		}
		public Task SaveAsync(AdminAssistActor actor, AiGenerationRow row, long expectedRevision, CancellationToken ct) => MaintenanceTransactionAsync(actor.DepartmentId, async () =>
		{
			if (row.DepartmentId != actor.DepartmentId || row.UserId != actor.UserId || row.Content == null || row.Content.Length > 65536 || (row.IsProtected ? !Resgrid.Model.ProtectedDataEnvelope.HasEnvelopePrefix(row.Content) : !row.Content.StartsWith("enc2:", StringComparison.Ordinal)) || expectedRevision is < 0 or >= 100 ||
				!Guid.TryParseExact(row.Id, "D", out _) || !Guid.TryParseExact(row.ConversationId, "D", out _)) throw new ArgumentException("Invalid conversation write.");
			var header = await ConversationAsync(actor, row.ConversationId, ct);
			if (header?.Deleted == true || header == null && expectedRevision != 0) throw new UnauthorizedAccessException();
			if (header != null && header.Revision != expectedRevision) throw new AdminAssistConcurrencyException();
			if (header == null)
				await ExecuteAsync($"INSERT INTO {Tbl("AdminAssistConversations")} ({Cols("Id", "DepartmentId", "UserId", "Revision", "CreatedOnUtc", "ModifiedOnUtc", "Deleted")}) VALUES ({P}Id,{P}DepartmentId,{P}UserId,0,{P}Now,{P}Now,{P}False)",
					new { Id = row.ConversationId, actor.DepartmentId, actor.UserId, Now = DatabaseTimestamp(row.CreatedOnUtc), False = false }, ct);
			row.Revision = expectedRevision + 1;
			await ExecuteAsync($"UPDATE {Tbl("AdminAssistConversations")} SET {Col("Revision")}={P}Revision,{Col("ModifiedOnUtc")}={P}Now WHERE {Col("Id")}={P}Id AND {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId",
				new { Id = row.ConversationId, actor.DepartmentId, actor.UserId, row.Revision, Now = DatabaseTimestamp(row.CreatedOnUtc) }, ct);
			await ExecuteAsync($"INSERT INTO {Tbl("AiGenerations")} ({Cols("Id", "ConversationId", "DepartmentId", "UserId", "Revision", "CreatedOnUtc", "Content", "IsProtected", "ProtectedCatalogVersion", "PromptVersion", "ModelRevision", "RuntimeDigest", "RequestDigest", "InputTokens", "OutputTokens", "Outcome")}) VALUES ({P}Id,{P}ConversationId,{P}DepartmentId,{P}UserId,{P}Revision,{P}CreatedOnUtc,{P}Content,{P}IsProtected,{P}ProtectedCatalogVersion,{P}PromptVersion,{P}ModelRevision,{P}RuntimeDigest,{P}RequestDigest,{P}InputTokens,{P}OutputTokens,{P}Outcome)",
				new { row.Id, row.ConversationId, row.DepartmentId, row.UserId, row.Revision, CreatedOnUtc = DatabaseTimestamp(row.CreatedOnUtc), row.Content, row.IsProtected, row.ProtectedCatalogVersion, row.PromptVersion, row.ModelRevision, row.RuntimeDigest, row.RequestDigest, row.InputTokens, row.OutputTokens, row.Outcome }, ct);
			return 0;
		}, ct);
		public Task<IReadOnlyList<AiGenerationRow>> ReadAsync(AdminAssistActor actor, string conversationId, CancellationToken ct) => ReadTurnsAsync(actor, conversationId, 10, ct);
		public Task<IReadOnlyList<AiGenerationRow>> ReadForExportAsync(AdminAssistActor actor, string conversationId, CancellationToken ct) => ReadTurnsAsync(actor, conversationId, 100, ct);
		private async Task<IReadOnlyList<AiGenerationRow>> ReadTurnsAsync(AdminAssistActor actor, string conversationId, int take, CancellationToken ct)
		{
			await GetRevisionAsync(actor, conversationId, ct);
			return (await QueryAsync<AiGenerationRow>($"SELECT * FROM {Tbl("AiGenerations")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId AND {Col("ConversationId")}={P}Id ORDER BY {Col("Revision")} DESC {Paging()}", new { actor.DepartmentId, actor.UserId, Id = conversationId, Skip = 0, Take = take }, ct)).Reverse().ToArray();
		}
		public async Task<IReadOnlyList<AskConversation>> ListAsync(AdminAssistActor actor, CancellationToken ct) =>
			(await QueryAsync<AskConversation>($"SELECT {(IsPostgres ? "" : "TOP (30) ")}{Cols("Id", "Revision", "ModifiedOnUtc")} FROM {Tbl("AdminAssistConversations")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId AND {Col("Deleted")}={P}False ORDER BY {Col("ModifiedOnUtc")} DESC{(IsPostgres ? " LIMIT 30" : "")}", new { actor.DepartmentId, actor.UserId, False = false }, ct)).Select(r => r with { ModifiedOnUtc = DateTime.SpecifyKind(r.ModifiedOnUtc, DateTimeKind.Utc) }).ToArray();
		public Task DeleteAsync(AdminAssistActor actor, string conversationId, long expectedRevision, CancellationToken ct) => MaintenanceTransactionAsync(actor.DepartmentId, async () =>
		{
			if (await GetRevisionAsync(actor, conversationId, ct) != expectedRevision) throw new AdminAssistConcurrencyException();
			await ExecuteAsync($"UPDATE {Tbl("AdminAssistConversations")} SET {Col("Deleted")}={P}True,{Col("ModifiedOnUtc")}={P}Now,{Col("Revision")}={Col("Revision")}+1 WHERE {Col("Id")}={P}Id AND {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId", new { actor.DepartmentId, actor.UserId, Id = conversationId, True = true, Now = DatabaseTimestamp(DateTime.UtcNow) }, ct);
			// Tombstone immediately; the existing hold-aware sweep removes content without reviving stale requests.
			return 0;
		}, ct);
	}
}

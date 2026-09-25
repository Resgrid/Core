using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Llm;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Services.AdminAssist
{
	public sealed class AdminAssistAskService(IAiAccessService aiAccess, IAdminAssistAccessService access, IAiUsageMeter usage,
		IAdminAssistAskQueries queries, IAdminAssistCatalog catalog, IAdminAssistConversationStore store,
		IAdminAssistConversationProtection protection, Lazy<ILlmClient> client, TimeProvider clock) : IAdminAssistAskService
	{
		private static readonly ResourceManager Labels = new(typeof(Resgrid.Localization.Areas.User.AdminAssist.AdminAssist));
		public Task<AdminAssistAskStatus> GetStatusAsync(AdminAssistActor actor, CancellationToken ct) => aiAccess.CanUseAdminAssistAsync(actor, ct);
		private async Task RequireAsync(AdminAssistActor actor, CancellationToken ct) {
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
		}
		public async Task<AdminAssistAskAnswer> AskAsync(AdminAssistActor actor, AdminAssistAskRequest request, CancellationToken ct)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 2000 || request.ExpectedRevision is < 0 or >= 100 ||
				!GroundedAskRunner.Modes.ContainsKey(request.Topic ?? "") || request.ConversationId != null && !Guid.TryParseExact(request.ConversationId, "D", out _) || request.ConversationId == null && request.ExpectedRevision != 0) throw new ArgumentException("Invalid question.");
			using var turn = CancellationTokenSource.CreateLinkedTokenSource(ct); turn.CancelAfter(TimeSpan.FromSeconds(90));
			var token = turn.Token;
			var admitted = await aiAccess.CanUseAdminAssistAsync(actor, token);
			if (!admitted.Available) throw new UnauthorizedAccessException();
			await protection.PreflightAsync(actor, token);
			if (request.ConversationId != null && await store.GetRevisionAsync(actor, request.ConversationId, token) != request.ExpectedRevision) throw new AdminAssistConcurrencyException();
			var seeds = Seeds(actor, request);
			var id = request.ConversationId ?? Guid.NewGuid().ToString("D");
			// Token budget for the add-on, or one free question (enhanced-ai-addon-plan.md §5.4), chosen by the admitting tier.
			var reserved = await aiAccess.ReserveTurnAsync(actor, admitted, token);
			if (reserved == null) return Answer(request.ConversationId, request.ExpectedRevision, "Busy", [], 0, 0);
			GroundedAskResult result = null;
			MeteredLlmClient metered = null;
			var outcome = "Unavailable";
			try {
				// Match locally against reviewed catalog titles. Only the resulting public IDs reach inference.
				metered = new MeteredLlmClient(client.Value);
				result = await new GroundedAskRunner(metered, queries, async guard => { if (!(await aiAccess.CanUseAdminAssistAsync(actor, guard, false)).Available) throw new UnauthorizedAccessException(); }).RunAsync(actor, AiConfig.Model, request.Topic, seeds, reserved.Tokens, token);
				outcome = result.Outcome;
				await RequireAsync(actor, token);
				// Entitlement/kill switch are rechecked even after an in-flight reservation.
				var current = await aiAccess.CanUseAdminAssistAsync(actor, token, false);
				if (!current.Available) throw new UnauthorizedAccessException();
				var row = new AiGenerationRow { Id = Guid.NewGuid().ToString("D"), ConversationId = id, DepartmentId = actor.DepartmentId, UserId = actor.UserId,
					CreatedOnUtc = clock.GetUtcNow().UtcDateTime, Content = JsonSerializer.Serialize(new AskStoredContent(request.Question, result.Reads, result.Evidence.Select(e => e.Id).ToArray())),
					PromptVersion = AdminAssistPrompt.Version, ModelRevision = AiConfig.ModelRevision, RuntimeDigest = AiConfig.RuntimeDigest,
					RequestDigest = Convert.ToHexString(HMACSHA256.HashData(Convert.FromBase64String(AiConfig.AuditHmacKey), Encoding.UTF8.GetBytes(actor.DepartmentId + ":" + actor.UserId + ":" + request.Question))).ToLowerInvariant(),
					InputTokens = result.InputTokens, OutputTokens = result.OutputTokens, Outcome = outcome };
				await protection.ProtectAsync(actor, row, token);
				await store.SaveAsync(actor, row, request.ExpectedRevision, token);
				return Answer(id, row.Revision, outcome, result.Evidence, result.InputTokens, result.OutputTokens);
			}
			catch (UnauthorizedAccessException) { outcome = "Unavailable"; throw; }
			catch (AdminAssistConcurrencyException) { outcome = "Unavailable"; throw; }
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { outcome = "Cancelled"; throw; }
			catch (Exception) { outcome = "Unavailable"; return Answer(request.ConversationId, request.ExpectedRevision, outcome, [], 0, 0); }
			finally {
				// Failed/ambiguous inference remains charged at its reservation; never blindly retry a paid request.
				using var settle = new CancellationTokenSource(TimeSpan.FromSeconds(5));
				try { await usage.CompleteAsync(reserved, metered?.HasUnsettledRequest == true ? reserved.Tokens : Math.Min(reserved.Tokens, metered?.VerifiedTokens ?? 0), outcome, settle.Token); }
				catch (Exception) { /* The persisted reservation remains conservatively charged and expires admission after 120 seconds. */ }
			}
		}
		private IReadOnlyList<AskToolInput> Seeds(AdminAssistActor actor, AdminAssistAskRequest request)
		{
			string Text(string key) => Labels.GetString(key, CultureInfo.GetCultureInfo(actor.Locale ?? "en")) ?? key;
			var words = request.Question.Split(new[] { ' ', ',', '.', '?', ':', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3).Take(64).ToArray();
			int Score(string title) => words.Count(w => title.Contains(w, StringComparison.CurrentCultureIgnoreCase));
			var reads = new List<AskToolInput>();
			if (request.SettingId != null || request.ProposedValue != null) {
				if (request.Topic != "settings" || !Resgrid.AdminAssist.ConfigurationImpactEvaluator.Supports(request.SettingId) || request.ProposedValue?.Length is not (>= 1 and <= 128)) throw new ArgumentException("Invalid impact context.");
				reads.Add(new("evaluate_impact", Id: request.SettingId, Value: request.ProposedValue));
			}
			if (request.Topic == "plans") {
				if (request.PlanTemplateId != null && Resgrid.AdminAssist.ChangePlanPolicy.Templates(catalog).Any(t => t.Id == request.PlanTemplateId)) reads.Add(new("draft_plan", Id: request.PlanTemplateId));
				else if (Guid.TryParseExact(request.PlanId, "D", out _) && Resgrid.AdminAssist.ChangePlanPolicy.IsId(request.PlanStepId)) reads.Add(new("verify_step", Id: request.PlanId, Value: request.PlanStepId));
				else throw new ArgumentException("Select a plan template or step.");
				return reads;
			}
			if (request.Topic == "setup") { reads.Add(new("get_setup_report")); reads.Add(new("get_setup_next_steps")); }
			else if (request.Topic == "permissions") reads.Add(new("get_permissions", Id: "all"));
			else if (request.Topic == "settings") {
				foreach (var setting in catalog.Settings.Select(s => new { Entry = s, Score = Score(Text(s.LabelKey)) }).Where(s => s.Score > 0).OrderByDescending(s => s.Score).ThenBy(s => s.Entry.Id).Take(request.SettingId == null ? 2 : 1)) reads.Add(new("get_setting", Id: setting.Entry.Id));
			}
			else if (request.Topic == "addons") {
				var ids = catalog.Capabilities.Where(c => c.Id.StartsWith("addon-", StringComparison.Ordinal)).OrderByDescending(c => Score(Text(c.LabelKey) + " " + Text(c.PurposeKey))).ThenBy(c => c.Id).Take(5).Select(c => c.Id).ToArray();
				reads.Add(new("compare_addon_capabilities", Ids: ids));
			}
			if (reads.Count < 3) reads.Add(new("search_reference", Query: request.Question.Length > 256 ? request.Question.Substring(0, 256) : request.Question));
			return reads;
		}
		private static AdminAssistAskAnswer Answer(string id, long revision, string outcome, IReadOnlyList<AskEvidence> evidence, int input, int output) => new(id, revision, outcome, evidence, AdminAssistPrompt.Version, AiConfig.ModelRevision, input, output);
		public async Task<IReadOnlyList<AdminAssistAskAnswer>> ReadAsync(AdminAssistActor actor, string conversationId, CancellationToken ct)
		{
			await RequireAsync(actor, ct);
			if (!Guid.TryParseExact(conversationId, "D", out _)) throw new ArgumentException("Invalid conversation.");
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(90));
			var rows = await store.ReadAsync(actor, conversationId, deadline.Token);
			var answers = new List<AdminAssistAskAnswer>();
			foreach (var row in rows) {
				var stored = await protection.ReadAsync(actor, row, deadline.Token);
				var evidence = new Dictionary<string, AskEvidence>();
				if (stored?.Reads?.Count <= 11 && stored.EvidenceIds?.Count <= 8) foreach (var read in stored.Reads.Distinct()) {
					using var tool = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token); tool.CancelAfter(TimeSpan.FromSeconds(20));
					try { foreach (var current in await queries.ReadAsync(actor, read, tool.Token)) evidence[current.Id] = current; }
					catch (ArgumentException) { /* A removed catalog entry is no longer evidence. */ }
				}
				var selected = stored?.EvidenceIds?.Where(evidence.ContainsKey).Select(id => evidence[id]).ToArray() ?? Array.Empty<AskEvidence>();
				answers.Add(new(conversationId, row.Revision, selected.Length > 0 ? "Refreshed" : "Unavailable", selected, row.PromptVersion, row.ModelRevision, row.InputTokens, row.OutputTokens));
			}
			await RequireAsync(actor, deadline.Token);
			await store.GetRevisionAsync(actor, conversationId, deadline.Token); // Reject concurrent deletion.
			return answers;
		}
		public async Task<AskConversationExport> ExportAsync(AdminAssistActor actor, string conversationId, CancellationToken ct) {
			await RequireAsync(actor, ct);
			if (!Guid.TryParseExact(conversationId, "D", out _)) throw new ArgumentException("Invalid conversation.");
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(90));
			var rows = await store.ReadForExportAsync(actor, conversationId, deadline.Token);
			var turns = new List<AskExportTurn>();
			foreach (var row in rows) {
				var content = await protection.ReadAsync(actor, row, deadline.Token);
				if (content == null) throw new UnauthorizedAccessException();
				turns.Add(new(row.Revision, DateTime.SpecifyKind(row.CreatedOnUtc, DateTimeKind.Utc), row.PromptVersion, row.ModelRevision, content));
			}
			await RequireAsync(actor, deadline.Token);
			await store.GetRevisionAsync(actor, conversationId, deadline.Token);
			return new(conversationId, turns);
		}
		public async Task<IReadOnlyList<AskConversation>> ListAsync(AdminAssistActor actor, CancellationToken ct) {
			await RequireAsync(actor, ct);
			var rows = await store.ListAsync(actor, ct);
			await RequireAsync(actor, ct);
			return rows;
		}
		public async Task DeleteAsync(AdminAssistActor actor, string conversationId, long expectedRevision, CancellationToken ct) {
			await RequireAsync(actor, ct);
			if (!Guid.TryParseExact(conversationId, "D", out _) || expectedRevision < 1) throw new ArgumentException("Invalid conversation.");
			await store.DeleteAsync(actor, conversationId, expectedRevision, ct);
		}
	}
}

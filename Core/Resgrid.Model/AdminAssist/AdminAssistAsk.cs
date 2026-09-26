using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	public sealed record AdminAssistAskRequest(string Question, string ConversationId = null, long ExpectedRevision = 0, string Topic = "setup", string SettingId = null, string ProposedValue = null, string PlanTemplateId = null, string PlanId = null, string PlanStepId = null);
	/// <summary>
	/// Conversation admission state. Tier is an <see cref="AdminAssistAskTiers"/> value once an entitlement path applies;
	/// the Free* fields are set only on the free allowance (questions remaining in the current window and when it ends).
	/// </summary>
	public sealed record AdminAssistAskStatus(bool Available, string Reason, long TokensRemaining, string Tier = null,
		int? FreeQuestionsRemaining = null, int? FreeQuestionsAllowance = null, DateTime? FreeWindowEndsUtc = null);
	public sealed record AskEvidence(string Id, string Kind, string TitleKey, IReadOnlyList<string> TextKeys,
		IReadOnlyDictionary<string, decimal> Numbers, string State, string Destination, string CitationId,
		string CatalogVersion, string SnapshotRevision, DateTime AsOfUtc, string PublicText = null);
	public sealed record AdminAssistAskAnswer(string ConversationId, long Revision, string Outcome, IReadOnlyList<AskEvidence> Evidence,
		string PromptVersion, string ModelRevision, int InputTokens, int OutputTokens);
	public sealed record AskExportTurn(long Revision, DateTime CreatedOnUtc, string PromptVersion, string ModelRevision, AskStoredContent Content);
	public sealed record AskConversationExport(string ConversationId, IReadOnlyList<AskExportTurn> Turns);
	public sealed record AskConversation(string Id, long Revision, DateTime ModifiedOnUtc);
	public sealed record AskToolInput(string Name, string Id = null, string Query = null, string Value = null, int WindowDays = 7, IReadOnlyList<string> Ids = null);
	public interface IAdminAssistConversationProtection
	{
		Task PreflightAsync(AdminAssistActor actor, CancellationToken ct);
		Task ProtectAsync(AdminAssistActor actor, AiGenerationRow row, CancellationToken ct);
		Task<AskStoredContent> ReadAsync(AdminAssistActor actor, AiGenerationRow row, CancellationToken ct);
	}
	public interface IAdminAssistAskQueries
	{
		Task<IReadOnlyList<AskEvidence>> ReadAsync(AdminAssistActor actor, AskToolInput tool, CancellationToken ct);
	}
	public interface IAdminAssistAskService
	{
		Task<AdminAssistAskStatus> GetStatusAsync(AdminAssistActor actor, CancellationToken ct);
		Task<AdminAssistAskAnswer> AskAsync(AdminAssistActor actor, AdminAssistAskRequest request, CancellationToken ct);
		Task<AskConversationExport> ExportAsync(AdminAssistActor actor, string conversationId, CancellationToken ct);
		Task<IReadOnlyList<AdminAssistAskAnswer>> ReadAsync(AdminAssistActor actor, string conversationId, CancellationToken ct);
		Task<IReadOnlyList<AskConversation>> ListAsync(AdminAssistActor actor, CancellationToken ct);
		Task DeleteAsync(AdminAssistActor actor, string conversationId, long expectedRevision, CancellationToken ct);
	}
	public interface IAiAccessService
	{
		Task<AdminAssistAskStatus> CanUseAdminAssistAsync(AdminAssistActor actor, CancellationToken ct, bool requireBudget = true);
		/// <summary>Reserves one turn on the path <paramref name="status"/> admitted: the token budget, or one free question. Null when admission is busy or the allowance was used meanwhile.</summary>
		Task<AiUsageReservation> ReserveTurnAsync(AdminAssistActor actor, AdminAssistAskStatus status, CancellationToken ct);
	}
	public sealed record AiUsageReservation(string Id, int DepartmentId, string UserId, int Tokens, DateTime ExpiresOnUtc);
	public interface IAiUsageMeter
	{
		Task<bool> IsDisabledAsync(int departmentId, CancellationToken ct);
		Task<long> RemainingAsync(int departmentId, DateTime now, int monthlyLimit, CancellationToken ct);
		Task<AiUsageReservation> ReserveAsync(AdminAssistActor actor, DateTime now, int tokens, int monthlyLimit, CancellationToken ct);
		Task CompleteAsync(AiUsageReservation reservation, int tokens, string outcome, CancellationToken ct);
	}
	public sealed class AiGenerationRow
	{
		public string Id { get; set; }
		public string ConversationId { get; set; }
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public long Revision { get; set; }
		public DateTime CreatedOnUtc { get; set; }
		public string Content { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public string PromptVersion { get; set; }
		public string ModelRevision { get; set; }
		public string RuntimeDigest { get; set; }
		public string RequestDigest { get; set; }
		public int InputTokens { get; set; }
		public int OutputTokens { get; set; }
		public string Outcome { get; set; }
	}
	public sealed record AskStoredContent(string Question, IReadOnlyList<AskToolInput> Reads, IReadOnlyList<string> EvidenceIds);
	public interface IAdminAssistConversationStore
	{
		Task<long> GetRevisionAsync(AdminAssistActor actor, string conversationId, CancellationToken ct);
		Task SaveAsync(AdminAssistActor actor, AiGenerationRow row, long expectedRevision, CancellationToken ct);
		Task<IReadOnlyList<AiGenerationRow>> ReadForExportAsync(AdminAssistActor actor, string conversationId, CancellationToken ct);
		Task<IReadOnlyList<AiGenerationRow>> ReadAsync(AdminAssistActor actor, string conversationId, CancellationToken ct);
		Task<IReadOnlyList<AskConversation>> ListAsync(AdminAssistActor actor, CancellationToken ct);
		Task DeleteAsync(AdminAssistActor actor, string conversationId, long expectedRevision, CancellationToken ct);
	}
}

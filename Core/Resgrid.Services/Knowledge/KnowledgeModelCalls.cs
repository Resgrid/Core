using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Knowledge
{
	/// <summary>
	/// The Enhanced AI plumbing the K3 model calls share with K1/K2 (scenario practice, its scoring, the recommendation explanation): the
	/// Advanced Data Protection check (any state but Disabled keeps content local; an unreadable state does too), the shared inference slot
	/// and monthly budget waited for briefly, and one AiGenerations row per exchange through the conversation protection (a failure to log
	/// is reported and returns null, never turning a result into an error).
	/// </summary>
	public static class KnowledgeModelCalls
	{
		public const int MaxAdmissionWaitSeconds = 8;

		public sealed record Admission(AiUsageReservation Reservation, string Outcome);

		public static async Task<bool> IsProtectedAsync(IDepartmentDataProtectionService protection, int departmentId)
		{
			try
			{
				return await protection.GetStateAsync(departmentId, true) != DepartmentDataProtectionState.Disabled;
			}
			catch (Exception)
			{
				return true;
			}
		}

		public static async Task<Admission> ReserveAsync(IAiUsageMeter usage, IAiBackgroundAdmission admission, TimeProvider clock, int departmentId, string feature,
			int tokens, CancellationToken cancellationToken)
		{
			var tier = AiOperatorSettings.IsListedDepartment(AiConfig.SelfHostedDepartmentIds, departmentId) ? AdminAssistAskTiers.SelfHosted : AdminAssistAskTiers.EnhancedAi;
			var giveUp = clock.GetUtcNow().UtcDateTime.AddSeconds(MaxAdmissionWaitSeconds);
			while (true)
			{
				var now = clock.GetUtcNow().UtcDateTime;
				if (await usage.RemainingAsync(departmentId, now, AiConfig.MonthlyTokenLimit, cancellationToken) < tokens)
					return new Admission(null, KnowledgeOutcomes.BudgetExhausted);
				var reservation = await admission.ReserveBackgroundAsync(departmentId, feature, tier, now, tokens, AiConfig.MonthlyTokenLimit, cancellationToken);
				if (reservation != null)
					return new Admission(reservation, null);
				if (now >= giveUp)
					return new Admission(null, KnowledgeOutcomes.Busy);
				await Task.Delay(TimeSpan.FromMilliseconds(500), clock, cancellationToken);
			}
		}

		/// <summary>Failed inference is not charged: the GPU and its outages are the operator's, not the department's.</summary>
		public static async Task CompleteAsync(IAiUsageMeter usage, AiUsageReservation reservation, int used, string ledger)
		{
			if (reservation == null)
				return;
			try { await usage.CompleteAsync(reservation, used, ledger, CancellationToken.None); }
			catch (Exception ex) { Logging.LogException(ex); }
		}

		/// <summary>One AiGenerations row (its own single-turn conversation): the stored text and evidence ids, encrypted by the protection.</summary>
		public static async Task<string> LogAsync(IAdminAssistConversationProtection protection, IKnowledgeGapsRepository generations, TimeProvider clock,
			AdminAssistActor actor, string promptVersion, string storedText, IEnumerable<string> evidenceIds, string outcome, int inputTokens, int outputTokens,
			CancellationToken cancellationToken)
		{
			try
			{
				var id = Guid.NewGuid().ToString("D");
				var row = new AiGenerationRow
				{
					Id = id,
					ConversationId = id,
					DepartmentId = actor.DepartmentId,
					UserId = actor.UserId,
					Revision = 1,
					CreatedOnUtc = clock.GetUtcNow().UtcDateTime,
					Content = JsonSerializer.Serialize(new AskStoredContent(storedText ?? string.Empty, Array.Empty<AskToolInput>(), (evidenceIds ?? Enumerable.Empty<string>()).ToArray())),
					PromptVersion = promptVersion,
					ModelRevision = AiConfig.ModelRevision ?? string.Empty,
					RuntimeDigest = AiConfig.RuntimeDigest ?? string.Empty,
					RequestDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.DepartmentId.ToString(CultureInfo.InvariantCulture) + ":" + actor.UserId + ":" +
						promptVersion + ":" + id))).ToLowerInvariant(),
					InputTokens = Math.Max(0, inputTokens),
					OutputTokens = Math.Max(0, outputTokens),
					Outcome = outcome
				};
				await protection.ProtectAsync(actor, row, cancellationToken);
				await generations.InsertGenerationAsync(row, cancellationToken);
				return id;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"A knowledge exchange ({promptVersion}) could not be logged for department {actor.DepartmentId}.");
				return null;
			}
		}

		/// <summary>The membership a gate uses: the department's owner or a current (not deleted, not disabled) member.</summary>
		public static async Task<bool> IsCurrentMemberAsync(IDepartmentsService departments, int departmentId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return false;
			var department = await departments.GetDepartmentByIdAsync(departmentId, false);
			if (department == null || department.DepartmentId != departmentId)
				return false;
			return string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase) ||
				Resgrid.Model.Helpers.DepartmentMemberStateHelper.IsCurrentMember(await departments.GetDepartmentMemberAsync(userId, departmentId, false), departmentId);
		}
	}
}

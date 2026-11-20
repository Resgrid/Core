using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Services;

namespace Resgrid.Services.Intake
{
	/// <summary>
	/// Enhanced AI on intake SMS conversations (call-intake-and-ai-destinations-plan.md §8 D16). Available when the host allows
	/// conversations, the operator's self-hosted model is configured (generative endpoint, reviewed, pinned), the department may use
	/// Ai.Intake (paid Enhanced AI or an operator-listed self-hosted department) and is NOT enrolled in Advanced Data Protection (D9:
	/// call content reaches inference only through a future broker purpose). Each turn waits briefly for a background inference slot
	/// and is metered against the department's monthly Enhanced AI tokens like AI dispatch.
	/// </summary>
	public sealed class IntakeConversationAssistant : IIntakeConversationAssistant
	{
		/// <summary>AiUsageLedger feature name.</summary>
		public const string Feature = "IntakeConversation";

		/// <summary>The texter is waiting: a turn gives up on a busy model sooner than background enrichment does.</summary>
		private const int MaxAdmissionWaitSeconds = 30;
		private const int MaxRequestSeconds = 45;
		private const int ReservedTokens = 2048;

		private readonly IEnhancedAiAccessService _enhancedAi;
		private readonly IFeatureToggleService _flags;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IAiBackgroundAdmission _admission;
		private readonly IAiUsageMeter _usage;
		private readonly Lazy<ILlmClient> _client;
		private readonly TimeProvider _clock;

		public IntakeConversationAssistant(IEnhancedAiAccessService enhancedAi, IFeatureToggleService flags, IDepartmentDataProtectionService protection,
			IAiBackgroundAdmission admission, IAiUsageMeter usage, Lazy<ILlmClient> client, TimeProvider clock)
		{
			_enhancedAi = enhancedAi;
			_flags = flags;
			_protection = protection;
			_admission = admission;
			_usage = usage;
			_client = client;
			_clock = clock;
		}

		public async Task<string> CheckAsync(int departmentId)
		{
			if (departmentId <= 0 || !IntakeConfig.Enabled || !IntakeConfig.ConversationsEnabled)
				return IntakeAssistantOutcomes.Disabled;
			if (!OperatorConfigured())
				return IntakeAssistantOutcomes.Unconfigured;

			try
			{
				var entitled = AiOperatorSettings.IsListedDepartment(AiConfig.SelfHostedDepartmentIds, departmentId)
					? await _enhancedAi.IsEnabledAsync(departmentId) && (await _flags.EvaluateFreshAsync(FeatureFlagKeys.AiIntake, departmentId))?.IsEnabled == true
					: await _enhancedAi.CanUseAsync(departmentId, FeatureFlagKeys.AiIntake);
				if (!entitled)
					return IntakeAssistantOutcomes.NotEntitled;

				return await _protection.ShouldEncryptNewWritesAsync(departmentId) ? IntakeAssistantOutcomes.ProtectionUnsupported : null;
			}
			catch (Exception ex)
			{
				// Unknown entitlement or protection state: a person answers the texter.
				Framework.Logging.LogException(ex, $"Intake conversation AI availability could not be read for department {departmentId}.");
				return IntakeAssistantOutcomes.NotEntitled;
			}
		}

		/// <summary>The operator's generative model is a valid, reviewed, pinned endpoint (AI dispatch's rule).</summary>
		public static bool OperatorConfigured()
		{
			try
			{
				OperatorEndpointPolicy.ValidateUri(AiConfig.Endpoint, AiConfig.AllowPrivateEndpoint);
			}
			catch (LlmUnavailableException)
			{
				return false;
			}

			return !string.IsNullOrWhiteSpace(AiConfig.ApiKey) && AiOperatorSettings.IsReviewedModel(AiConfig.Model) &&
				AiOperatorSettings.IsPinned(AiConfig.ModelRevision, AiConfig.RuntimeDigest);
		}

		public async Task<IntakeAssistantAnswer> AskAsync(IntakeAssistantRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.DepartmentId <= 0)
				return new IntakeAssistantAnswer { Outcome = IntakeAssistantOutcomes.Disabled };

			var departmentId = request.DepartmentId;
			var selfHosted = AiOperatorSettings.IsListedDepartment(AiConfig.SelfHostedDepartmentIds, departmentId);
			var reservation = await ReserveAsync(departmentId, selfHosted, cancellationToken);
			if (reservation.Outcome != null)
				return new IntakeAssistantAnswer { Outcome = reservation.Outcome };

			var usedTokens = 0;
			var ledgerOutcome = "Unavailable";
			try
			{
				// CheckAsync ran before the admission wait (up to 30 s): a department that enrolled in Advanced Data Protection meanwhile must not
				// have this transcript sent to inference (D9). The policy read is invalidated on every protection change, as AI dispatch relies on.
				bool protectedNow;
				try
				{
					protectedNow = await _protection.ShouldEncryptNewWritesAsync(departmentId);
				}
				catch (Exception ex)
				{
					// Unknown protection state fails closed, as CheckAsync does: a person answers the texter.
					Framework.Logging.LogException(ex, $"Intake conversation protection state could not be read again for department {departmentId}.");
					protectedNow = true;
				}

				if (protectedNow)
				{
					ledgerOutcome = IntakeAssistantOutcomes.ProtectionUnsupported;
					return new IntakeAssistantAnswer { Outcome = IntakeAssistantOutcomes.ProtectionUnsupported };
				}

				var context = new AiIntakeConversationContext(request.DepartmentName, IntakeConfig.EmergencyNumber,
					(request.Lines ?? new System.Collections.Generic.List<IntakeAssistantLine>()).Where(l => l != null && !string.IsNullOrWhiteSpace(l.Text))
						.Select(l => new AiIntakeConversationLine(l.FromCaller, l.Text)).ToList(),
					request.Turn, request.MaxTurns);

				LlmResult result;
				using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(AiDispatchConfig.RequestTimeoutSeconds, 5, MaxRequestSeconds)));
					try
					{
						result = await _client.Value.CompleteAsync(new LlmRequest(AiConfig.Model, AiIntakeConversationPrompt.Messages(context),
							new[] { AiIntakeConversationPrompt.Tool() }, 256,
							AiCacheIsolation.SaltFor(departmentId, AiConfig.SendCacheSalt, AiConfig.CacheSaltKey, AiConfig.AuditHmacKey)), deadline.Token);
					}
					catch (Exception ex) when (ex is LlmUnavailableException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
					{
						return new IntakeAssistantAnswer { Outcome = IntakeAssistantOutcomes.Unavailable };
					}
				}

				usedTokens = Math.Min(reservation.Reservation.Tokens, Math.Max(0, result.InputTokens + result.OutputTokens));
				try
				{
					var reply = AiIntakeConversationPrompt.Validate(AiIntakeConversationPrompt.Parse(result));
					ledgerOutcome = "Answered";
					return new IntakeAssistantAnswer
					{
						Outcome = IntakeAssistantOutcomes.Answered,
						ReplyText = reply.ReplyText,
						Done = reply.Done,
						WantsDispatcher = reply.WantsDispatcher
					};
				}
				catch (Exception ex) when (ex is ArgumentException || ex is System.Text.Json.JsonException)
				{
					ledgerOutcome = "InvalidOutput";
					return new IntakeAssistantAnswer { Outcome = IntakeAssistantOutcomes.InvalidOutput };
				}
			}
			finally
			{
				// Failed inference is not charged: the GPU and its outages are the operator's, not the department's.
				try { await _usage.CompleteAsync(reservation.Reservation, usedTokens, ledgerOutcome, CancellationToken.None); }
				catch (Exception ex) { Framework.Logging.LogException(ex); }
			}
		}

		private sealed record Admission(AiUsageReservation Reservation, string Outcome);

		/// <summary>Background priority on the shared inference slots: waits for a moment when no interactive turn is live, briefly.</summary>
		private async Task<Admission> ReserveAsync(int departmentId, bool selfHosted, CancellationToken cancellationToken)
		{
			var tier = selfHosted ? AdminAssistAskTiers.SelfHosted : AdminAssistAskTiers.EnhancedAi;
			var giveUp = _clock.GetUtcNow().UtcDateTime.AddSeconds(Math.Clamp(AiDispatchConfig.AdmissionWaitSeconds, 0, MaxAdmissionWaitSeconds));
			while (true)
			{
				var now = _clock.GetUtcNow().UtcDateTime;
				if (await _usage.RemainingAsync(departmentId, now, AiConfig.MonthlyTokenLimit, cancellationToken) < ReservedTokens)
					return new Admission(null, IntakeAssistantOutcomes.BudgetExhausted);
				var reservation = await _admission.ReserveBackgroundAsync(departmentId, Feature, tier, now, ReservedTokens, AiConfig.MonthlyTokenLimit, cancellationToken);
				if (reservation != null)
					return new Admission(reservation, null);
				if (now >= giveUp)
					return new Admission(null, IntakeAssistantOutcomes.Busy);
				await Task.Delay(TimeSpan.FromSeconds(2), _clock, cancellationToken);
			}
		}
	}
}

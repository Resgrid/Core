using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class InboundEmailReceiptsService : IInboundEmailReceiptsService
	{
		/// <summary>
		/// How long a claim keeps a redelivery waiting (409) before the attempt holding it is presumed dead and the email is taken
		/// over. A failing attempt gives its claim back at once, so this only governs a crashed process or a request still running:
		/// Postmark stops waiting after 2 minutes, retries 1 minute later and then 5, so a crash is picked up on the second retry.
		/// </summary>
		public static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(5);

		/// <summary>Postmark's retries end about 10.5 hours after the first delivery; a week leaves room to investigate.</summary>
		public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

		private readonly IInboundEmailReceiptsRepository _inboundEmailReceiptsRepository;
		private readonly TimeProvider _clock;

		public InboundEmailReceiptsService(IInboundEmailReceiptsRepository inboundEmailReceiptsRepository, TimeProvider clock)
		{
			_inboundEmailReceiptsRepository = inboundEmailReceiptsRepository;
			_clock = clock;
		}

		public async Task<InboundEmailReceiptClaim> ClaimAsync(string providerMessageId, InboundEmailKinds kind, CancellationToken cancellationToken = default)
		{
			var messageId = providerMessageId?.Trim();
			if (string.IsNullOrEmpty(messageId) || messageId.Length > InboundEmailReceipt.ProviderMessageIdMaxLength)
				return InboundEmailReceiptClaim.Untracked;

			try
			{
				var now = Now();
				var receipt = new InboundEmailReceipt
				{
					InboundEmailReceiptId = Guid.NewGuid().ToString(),
					ProviderMessageId = messageId,
					Kind = (int)kind,
					State = (int)InboundEmailReceiptStates.Processing,
					PendingSteps = (int)InboundEmailPendingSteps.None,
					Deliveries = 1,
					ReceivedOn = now,
					ClaimedOn = now
				};

				if (await _inboundEmailReceiptsRepository.TryInsertAsync(receipt, cancellationToken))
					return new InboundEmailReceiptClaim(InboundEmailClaimOutcomes.Process, receipt.InboundEmailReceiptId, null, InboundEmailPendingSteps.None);

				var existing = await _inboundEmailReceiptsRepository.GetByProviderMessageIdAsync(messageId, cancellationToken);

				// Purged between the insert and the read: nothing to compare against, so handle it as before receipts existed.
				if (existing == null)
					return InboundEmailReceiptClaim.Untracked;

				if (existing.State == (int)InboundEmailReceiptStates.Completed)
				{
					await _inboundEmailReceiptsRepository.RecordDeliveryAsync(existing.InboundEmailReceiptId, cancellationToken);
					return new InboundEmailReceiptClaim(InboundEmailClaimOutcomes.AlreadyProcessed, existing.InboundEmailReceiptId, existing.EntityId,
						InboundEmailPendingSteps.None);
				}

				if (await _inboundEmailReceiptsRepository.TryReclaimAsync(existing.InboundEmailReceiptId, existing.State, now, now - ProcessingLease, cancellationToken))
				{
					// Saved without its id would have nothing to resume; process it again rather than drop the email.
					var resume = existing.State == (int)InboundEmailReceiptStates.Saved && existing.EntityId.HasValue;

					return new InboundEmailReceiptClaim(resume ? InboundEmailClaimOutcomes.Resume : InboundEmailClaimOutcomes.Process,
						existing.InboundEmailReceiptId, existing.EntityId, (InboundEmailPendingSteps)existing.PendingSteps);
				}

				await _inboundEmailReceiptsRepository.RecordDeliveryAsync(existing.InboundEmailReceiptId, cancellationToken);
				return new InboundEmailReceiptClaim(InboundEmailClaimOutcomes.InProgress, existing.InboundEmailReceiptId, existing.EntityId,
					(InboundEmailPendingSteps)existing.PendingSteps);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Inbound email receipt claim failed for provider message {messageId}; processing it unguarded.");
				return InboundEmailReceiptClaim.Untracked;
			}
		}

		public Task<InboundEmailReceiptClaim> ClaimApiRequestAsync(InboundEmailKinds kind, int departmentId, string idempotencyKey, CancellationToken cancellationToken = default)
		{
			var key = idempotencyKey?.Trim();
			if (string.IsNullOrEmpty(key) || departmentId <= 0)
				return Task.FromResult(InboundEmailReceiptClaim.Untracked);

			return ClaimAsync(ApiRequestKey(kind, departmentId, key), kind, cancellationToken);
		}

		/// <summary>
		/// "{kind}:{departmentId}:{key}", or the key's SHA-256 when that would not fit the column (a Message-Id can be long). The
		/// kind prefix keeps a call and its attachments, keyed from the same Message-Id, apart; none collides with a Postmark GUID.
		/// </summary>
		public static string ApiRequestKey(InboundEmailKinds kind, int departmentId, string idempotencyKey)
		{
			var scope = $"{(int)kind}:{departmentId}:";
			var scoped = scope + idempotencyKey;
			if (scoped.Length <= InboundEmailReceipt.ProviderMessageIdMaxLength)
				return scoped;

			return scope + "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));
		}

		public async Task MarkSavedAsync(InboundEmailReceiptClaim claim, int entityId, InboundEmailPendingSteps pendingSteps)
		{
			if (claim == null || !claim.IsTracked || entityId <= 0)
				return;

			try
			{
				await _inboundEmailReceiptsRepository.MarkSavedAsync(claim.ReceiptId, entityId, (int)pendingSteps, Now(), CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Inbound email receipt {claim.ReceiptId} could not record saved entity {entityId}.");
			}
		}

		public async Task CompleteStepAsync(InboundEmailReceiptClaim claim, InboundEmailPendingSteps step)
		{
			if (claim == null || !claim.IsTracked || step == InboundEmailPendingSteps.None)
				return;

			try
			{
				await _inboundEmailReceiptsRepository.CompleteStepsAsync(claim.ReceiptId, (int)step, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Inbound email receipt {claim.ReceiptId} could not record finished step {step}.");
			}
		}

		public async Task MarkCompletedAsync(InboundEmailReceiptClaim claim)
		{
			if (claim == null || !claim.IsTracked)
				return;

			try
			{
				await _inboundEmailReceiptsRepository.MarkCompletedAsync(claim.ReceiptId, Now(), CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Inbound email receipt {claim.ReceiptId} could not be marked completed.");
			}
		}

		public async Task ReleaseAsync(InboundEmailReceiptClaim claim)
		{
			if (claim == null || !claim.IsTracked)
				return;

			try
			{
				await _inboundEmailReceiptsRepository.ReleaseAsync(claim.ReceiptId, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Inbound email receipt {claim.ReceiptId} could not give back its claim.");
			}
		}

		public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
		{
			return _inboundEmailReceiptsRepository.PurgeReceivedBeforeAsync(Now() - Retention, cancellationToken);
		}

		private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
	}
}

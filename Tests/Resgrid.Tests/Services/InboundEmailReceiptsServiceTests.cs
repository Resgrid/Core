using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Services;
using Resgrid.Tests.Mocks;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The decision each delivery of an inbound email gets (M0271): process, resume what an earlier attempt saved, acknowledge,
	/// or wait for the attempt holding the claim; and the store never blocks a dispatch when it fails.
	/// </summary>
	[TestFixture]
	public class InboundEmailReceiptsServiceTests
	{
		private static readonly DateTime Now = new DateTime(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc);

		private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new DateTimeOffset(Now); }

		private MockInboundEmailReceiptsRepository _repository;
		private InboundEmailReceiptsService _service;

		[SetUp]
		public void SetUp()
		{
			_repository = new MockInboundEmailReceiptsRepository();
			_service = new InboundEmailReceiptsService(_repository, new Clock());
		}

		private InboundEmailReceipt Seed(InboundEmailReceiptStates state, DateTime? claimedOn, int? entityId = null,
			InboundEmailPendingSteps steps = InboundEmailPendingSteps.None)
		{
			var receipt = new InboundEmailReceipt
			{
				InboundEmailReceiptId = Guid.NewGuid().ToString(),
				ProviderMessageId = "pm-1",
				Kind = (int)InboundEmailKinds.Dispatch,
				State = (int)state,
				EntityId = entityId,
				PendingSteps = (int)steps,
				Deliveries = 1,
				ReceivedOn = Now.AddMinutes(-30),
				ClaimedOn = claimedOn
			};
			_repository.Seed(receipt);
			return receipt;
		}

		[Test]
		public async Task The_first_delivery_is_claimed_for_processing()
		{
			var claim = await _service.ClaimAsync(" pm-1 ", InboundEmailKinds.GroupDispatch);

			claim.Outcome.Should().Be(InboundEmailClaimOutcomes.Process);
			claim.IsTracked.Should().BeTrue();
			var stored = _repository.Find("pm-1");
			stored.Should().NotBeNull("the provider id is stored trimmed");
			stored.InboundEmailReceiptId.Should().Be(claim.ReceiptId);
			stored.Kind.Should().Be((int)InboundEmailKinds.GroupDispatch);
			stored.State.Should().Be((int)InboundEmailReceiptStates.Processing);
			stored.ClaimedOn.Should().Be(Now);
			stored.Deliveries.Should().Be(1);
		}

		[Test]
		public async Task A_redelivery_of_a_completed_email_is_only_acknowledged_and_counted()
		{
			var receipt = Seed(InboundEmailReceiptStates.Completed, null, 42);

			var claim = await _service.ClaimAsync("PM-1", InboundEmailKinds.Dispatch);

			claim.Outcome.Should().Be(InboundEmailClaimOutcomes.AlreadyProcessed, "the provider id matches regardless of case");
			_repository.Find("pm-1").Deliveries.Should().Be(2);
			_repository.Find("pm-1").State.Should().Be((int)InboundEmailReceiptStates.Completed);
			claim.ReceiptId.Should().Be(receipt.InboundEmailReceiptId);
		}

		[Test]
		public async Task A_redelivery_while_the_claim_is_held_is_told_to_come_back()
		{
			Seed(InboundEmailReceiptStates.Processing, Now.AddMinutes(-1));

			var claim = await _service.ClaimAsync("pm-1", InboundEmailKinds.Dispatch);

			claim.Outcome.Should().Be(InboundEmailClaimOutcomes.InProgress);
			_repository.Find("pm-1").ClaimedOn.Should().Be(Now.AddMinutes(-1), "the holder keeps its claim");
			_repository.Find("pm-1").Deliveries.Should().Be(2);
		}

		[Test]
		public async Task A_saved_email_whose_attempt_gave_back_its_claim_is_resumed_with_its_pending_steps()
		{
			Seed(InboundEmailReceiptStates.Saved, null, 900, InboundEmailPendingSteps.CallBroadcast | InboundEmailPendingSteps.AiEnrichment);

			var claim = await _service.ClaimAsync("pm-1", InboundEmailKinds.Dispatch);

			claim.Outcome.Should().Be(InboundEmailClaimOutcomes.Resume);
			claim.EntityId.Should().Be(900);
			claim.Has(InboundEmailPendingSteps.CallBroadcast).Should().BeTrue();
			claim.Has(InboundEmailPendingSteps.AiEnrichment).Should().BeTrue();
			claim.Has(InboundEmailPendingSteps.MessageSend).Should().BeFalse();
			_repository.Find("pm-1").ClaimedOn.Should().Be(Now);
		}

		[Test]
		public async Task A_claim_older_than_the_lease_is_taken_over()
		{
			Seed(InboundEmailReceiptStates.Processing, Now - InboundEmailReceiptsService.ProcessingLease - TimeSpan.FromSeconds(1));

			var claim = await _service.ClaimAsync("pm-1", InboundEmailKinds.Dispatch);

			claim.Outcome.Should().Be(InboundEmailClaimOutcomes.Process, "an attempt that died before saving anything left nothing to resume");
			_repository.Find("pm-1").ClaimedOn.Should().Be(Now);
		}

		[Test]
		public async Task A_claim_just_inside_the_lease_is_still_held()
		{
			Seed(InboundEmailReceiptStates.Saved, Now - InboundEmailReceiptsService.ProcessingLease + TimeSpan.FromSeconds(1), 900, InboundEmailPendingSteps.CallBroadcast);

			(await _service.ClaimAsync("pm-1", InboundEmailKinds.Dispatch)).Outcome.Should().Be(InboundEmailClaimOutcomes.InProgress);
		}

		[TestCase(null)]
		[TestCase("   ")]
		public async Task Without_a_provider_message_id_nothing_is_tracked(string providerMessageId)
		{
			var claim = await _service.ClaimAsync(providerMessageId, InboundEmailKinds.Dispatch);

			claim.Outcome.Should().Be(InboundEmailClaimOutcomes.Untracked);
			claim.IsTracked.Should().BeFalse();
			_repository.Receipts.Should().BeEmpty();
		}

		[Test]
		public async Task A_provider_message_id_longer_than_the_column_is_not_tracked()
		{
			(await _service.ClaimAsync(new string('a', InboundEmailReceipt.ProviderMessageIdMaxLength + 1), InboundEmailKinds.Dispatch))
				.Outcome.Should().Be(InboundEmailClaimOutcomes.Untracked);
		}

		[Test]
		public async Task A_failing_store_never_blocks_the_email()
		{
			var repository = new Mock<IInboundEmailReceiptsRepository>();
			repository.Setup(r => r.TryInsertAsync(It.IsAny<InboundEmailReceipt>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("table missing"));
			repository.Setup(r => r.MarkSavedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("down"));
			var service = new InboundEmailReceiptsService(repository.Object, new Clock());

			(await service.ClaimAsync("pm-1", InboundEmailKinds.Dispatch)).Outcome.Should().Be(InboundEmailClaimOutcomes.Untracked);

			var tracked = new InboundEmailReceiptClaim(InboundEmailClaimOutcomes.Process, "r-1", null, InboundEmailPendingSteps.None);
			await service.Invoking(s => s.MarkSavedAsync(tracked, 900, InboundEmailPendingSteps.CallBroadcast)).Should().NotThrowAsync();
		}

		[Test]
		public async Task Receipt_writes_after_the_claim_ignore_the_request_cancellation()
		{
			var repository = new Mock<IInboundEmailReceiptsRepository>();
			var service = new InboundEmailReceiptsService(repository.Object, new Clock());
			var claim = new InboundEmailReceiptClaim(InboundEmailClaimOutcomes.Process, "r-1", null, InboundEmailPendingSteps.None);

			await service.MarkSavedAsync(claim, 900, InboundEmailPendingSteps.CallBroadcast);
			await service.MarkCompletedAsync(claim);
			await service.ReleaseAsync(claim);

			repository.Verify(r => r.MarkSavedAsync("r-1", 900, (int)InboundEmailPendingSteps.CallBroadcast, Now, CancellationToken.None), Times.Once);
			repository.Verify(r => r.MarkCompletedAsync("r-1", Now, CancellationToken.None), Times.Once);
			repository.Verify(r => r.ReleaseAsync("r-1", CancellationToken.None), Times.Once);
		}

		[Test]
		public async Task Untracked_claims_write_nothing()
		{
			var repository = new Mock<IInboundEmailReceiptsRepository>(MockBehavior.Strict);
			var service = new InboundEmailReceiptsService(repository.Object, new Clock());

			await service.MarkSavedAsync(InboundEmailReceiptClaim.Untracked, 900, InboundEmailPendingSteps.CallBroadcast);
			await service.MarkCompletedAsync(InboundEmailReceiptClaim.Untracked);
			await service.ReleaseAsync(InboundEmailReceiptClaim.Untracked);
		}

		[Test]
		public async Task Api_request_keys_are_scoped_to_the_kind_and_department()
		{
			var call = await _service.ClaimApiRequestAsync(InboundEmailKinds.ApiCall, 10, "msg-1@cad.example");
			var otherDepartment = await _service.ClaimApiRequestAsync(InboundEmailKinds.ApiCall, 11, "msg-1@cad.example");
			var file = await _service.ClaimApiRequestAsync(InboundEmailKinds.ApiCallFile, 10, "msg-1@cad.example");
			var repeat = await _service.ClaimApiRequestAsync(InboundEmailKinds.ApiCall, 10, " msg-1@cad.example ");

			call.Outcome.Should().Be(InboundEmailClaimOutcomes.Process);
			otherDepartment.Outcome.Should().Be(InboundEmailClaimOutcomes.Process, "one department's key never finds another department's call");
			file.Outcome.Should().Be(InboundEmailClaimOutcomes.Process, "a call and its attachments are keyed from the same Message-Id");
			repeat.Outcome.Should().Be(InboundEmailClaimOutcomes.InProgress);
			_repository.Find("5:10:msg-1@cad.example").Should().NotBeNull();
			_repository.Find("6:10:msg-1@cad.example").Kind.Should().Be((int)InboundEmailKinds.ApiCallFile);
		}

		[Test]
		public void A_key_too_long_for_the_column_is_stored_as_its_hash()
		{
			var longKey = new string('m', 200) + "@cad.example";

			var stored = InboundEmailReceiptsService.ApiRequestKey(InboundEmailKinds.ApiCall, 10, longKey);

			stored.Should().StartWith("5:10:sha256:");
			stored.Length.Should().BeLessThanOrEqualTo(InboundEmailReceipt.ProviderMessageIdMaxLength);
			InboundEmailReceiptsService.ApiRequestKey(InboundEmailKinds.ApiCall, 10, longKey).Should().Be(stored);
		}

		[TestCase(null, 10)]
		[TestCase("  ", 10)]
		[TestCase("msg-1@cad.example", 0)]
		public async Task Api_requests_without_a_key_or_department_are_not_tracked(string key, int departmentId)
		{
			(await _service.ClaimApiRequestAsync(InboundEmailKinds.ApiCall, departmentId, key)).Outcome.Should().Be(InboundEmailClaimOutcomes.Untracked);
			_repository.Receipts.Should().BeEmpty();
		}

		[Test]
		public async Task Finished_steps_are_cleared_so_a_resume_skips_them()
		{
			var claim = await _service.ClaimApiRequestAsync(InboundEmailKinds.ApiCall, 10, "msg-1@cad.example");
			await _service.MarkSavedAsync(claim, 700, InboundEmailPendingSteps.CallAddedEvent | InboundEmailPendingSteps.CallBroadcast);
			await _service.CompleteStepAsync(claim, InboundEmailPendingSteps.CallAddedEvent);
			await _service.ReleaseAsync(claim);

			var resume = await _service.ClaimApiRequestAsync(InboundEmailKinds.ApiCall, 10, "msg-1@cad.example");

			resume.Outcome.Should().Be(InboundEmailClaimOutcomes.Resume);
			resume.EntityId.Should().Be(700);
			resume.Has(InboundEmailPendingSteps.CallBroadcast).Should().BeTrue();
			resume.Has(InboundEmailPendingSteps.CallAddedEvent).Should().BeFalse();
		}

		[Test]
		public async Task Receipts_past_the_retention_window_are_purged()
		{
			var old = Seed(InboundEmailReceiptStates.Completed, null);
			old.ReceivedOn = Now - InboundEmailReceiptsService.Retention - TimeSpan.FromMinutes(1);
			_repository.Seed(new InboundEmailReceipt { InboundEmailReceiptId = "recent", ProviderMessageId = "pm-2", ReceivedOn = Now.AddHours(-11) });

			(await _service.PurgeExpiredAsync()).Should().Be(1);
			_repository.Find("pm-2").Should().NotBeNull("Postmark is still retrying an email this young");
		}
	}
}

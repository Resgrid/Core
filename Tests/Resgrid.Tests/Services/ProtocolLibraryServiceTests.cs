using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The protocol lifecycle (P1) over a real signing layer: only a draft's content changes, and a save against an older copy
	/// is refused with who saved last; an approval needs an L2 signature bound to the version's content hash (and is voided
	/// when another approver won the step); publishing needs every approval to still match the content and makes the
	/// version's text the protocol's text; a review stamp only moves the review dates; anything ever submitted or published is
	/// kept from user deletes.
	/// </summary>
	[TestFixture]
	public class ProtocolLibraryServiceTests
	{
		private const int DepartmentId = ProtocolLibraryWorld.DepartmentId;
		private const string ManagerId = ProtocolLibraryWorld.ManagerId;
		private const string OldHtml = "<h2>Scene safety</h2><p>Stay upwind of the spill.</p>";
		private const string NewHtml = "<h2>Scene safety</h2><p>Stay uphill and upwind of the spill.</p>";

		private ProtocolLibraryWorld _world;

		[SetUp]
		public void SetUp()
		{
			_world = new ProtocolLibraryWorld();
		}

		private static ProtocolReader Manager() => ProtocolLibraryWorld.Manager();

		#region Immutability

		[TestCase(ProtocolVersionStatuses.InReview)]
		[TestCase(ProtocolVersionStatuses.Approved)]
		public async Task SaveDraftAsync_refuses_a_version_that_left_draft_and_never_writes(ProtocolVersionStatuses status)
		{
			_world.AddProtocol(1);
			var locked = _world.AddVersion(100, 1, status, OldHtml);

			var result = await _world.Service.SaveDraftAsync(Manager(), 1, new ProtocolDraftInput { ContentHtml = NewHtml, ExpectedRowVersion = locked.RowVersion });

			result.Error.Should().Be(ProtocolLifecycleErrors.NotDraft);
			_world.Versions[100].ContentHtml.Should().Be(OldHtml);
			_world.VersionsRepository.Verify(x => x.TryUpdateAsync(It.IsAny<ProtocolVersion>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public void IsContentEditable_is_true_only_for_a_draft()
		{
			foreach (ProtocolVersionStatuses status in Enum.GetValues(typeof(ProtocolVersionStatuses)))
				ProtocolLifecycle.IsContentEditable((int)status).Should().Be(status == ProtocolVersionStatuses.Draft, status.ToString());
		}

		[Test]
		public async Task SaveDraftAsync_on_the_current_copy_writes_the_sanitized_content_and_its_hash()
		{
			_world.AddProtocol(1);
			_world.AddVersion(100, 1, ProtocolVersionStatuses.Draft, OldHtml);

			var result = await _world.Service.SaveDraftAsync(Manager(), 1, new ProtocolDraftInput
			{
				ContentHtml = NewHtml + "<script>alert(1)</script>",
				ChangeSummary = "  Uphill too.  ",
				ExpectedRowVersion = 3
			});

			result.Succeeded.Should().BeTrue();
			var stored = _world.Versions[100];
			stored.ContentHtml.Should().NotContain("<script");
			stored.ContentHtml.Should().Contain("uphill");
			stored.ContentHash.Should().Be(ProtocolContentHash.Compute(stored.ContentHtml));
			stored.ChangeSummary.Should().Be("Uphill too.");
			stored.RowVersion.Should().Be(4);
			stored.UpdatedByUserId.Should().Be(ManagerId);
		}

		#endregion Immutability

		#region Co-editing

		[Test]
		public async Task SaveDraftAsync_with_an_older_row_version_is_refused_with_who_saved_last()
		{
			_world.AddProtocol(1);
			var draft = _world.AddVersion(100, 1, ProtocolVersionStatuses.Draft, OldHtml);
			draft.UpdatedByUserId = "author-2";
			draft.UpdatedOn = new DateTime(2026, 11, 23, 14, 5, 0, DateTimeKind.Utc);

			var result = await _world.Service.SaveDraftAsync(Manager(), 1, new ProtocolDraftInput { ContentHtml = NewHtml, ExpectedRowVersion = draft.RowVersion - 1 });

			result.Error.Should().Be(ProtocolLifecycleErrors.StaleCopy);
			result.ConflictUserId.Should().Be("author-2");
			result.ConflictOn.Should().Be(new DateTime(2026, 11, 23, 14, 5, 0, DateTimeKind.Utc));
			_world.Versions[100].ContentHtml.Should().Be(OldHtml);
			_world.VersionsRepository.Verify(x => x.TryUpdateAsync(It.IsAny<ProtocolVersion>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task SaveDraftAsync_that_loses_the_race_at_the_write_is_refused_as_stale()
		{
			_world.AddProtocol(1);
			_world.AddVersion(100, 1, ProtocolVersionStatuses.Draft, OldHtml);
			// Another editor's save lands between the read and the conditional write.
			_world.VersionsRepository.Setup(x => x.TryUpdateAsync(It.IsAny<ProtocolVersion>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ProtocolVersion v, long expected, CancellationToken _) =>
				{
					var stored = _world.Versions[v.ProtocolVersionId];
					stored.RowVersion = expected + 1;
					stored.UpdatedByUserId = "author-2";
					stored.UpdatedOn = new DateTime(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);
					return false;
				});

			var result = await _world.Service.SaveDraftAsync(Manager(), 1, new ProtocolDraftInput { ContentHtml = NewHtml, ExpectedRowVersion = 3 });

			result.Error.Should().Be(ProtocolLifecycleErrors.StaleCopy);
			result.ConflictUserId.Should().Be("author-2");
			result.ConflictOn.Should().Be(new DateTime(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc));
		}

		[Test]
		public async Task UpdateLibraryFieldsAsync_with_a_stale_row_version_is_refused_and_writes_nothing()
		{
			var protocol = _world.AddProtocol(1);
			protocol.UpdatedByUserId = "author-2";
			protocol.UpdatedOn = new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc);

			var result = await _world.Service.UpdateLibraryFieldsAsync(Manager(), 1, new ProtocolLibraryFields { Kind = (int)ProtocolKinds.Policy, Tags = "hazmat" }, protocol.RowVersion - 1);

			// Detected by the conditional RowVersion bump on DispatchProtocols, before anything is written.
			result.Error.Should().Be(ProtocolLifecycleErrors.StaleCopy);
			result.ConflictUserId.Should().Be("author-2");
			result.ConflictOn.Should().Be(new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc));
			_world.Library.Verify(x => x.TryBumpRowVersionAsync(DepartmentId, 1, protocol.RowVersion - 1, It.IsAny<CancellationToken>()), Times.Once);
			_world.ProtocolRepository.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_world.Protocols[1].Kind.Should().Be((int)ProtocolKinds.Sog);
		}

		[Test]
		public async Task UpdateLibraryFieldsAsync_on_the_current_row_version_saves()
		{
			var protocol = _world.AddProtocol(1);

			var result = await _world.Service.UpdateLibraryFieldsAsync(Manager(), 1, new ProtocolLibraryFields { Kind = (int)ProtocolKinds.Policy, Tags = "hazmat; spill" }, protocol.RowVersion);

			result.Succeeded.Should().BeTrue();
			_world.Protocols[1].Kind.Should().Be((int)ProtocolKinds.Policy);
			_world.Protocols[1].Tags.Should().Be("hazmat, spill");
			_world.ProtocolRepository.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		#endregion Co-editing

		#region Submit

		/// <summary>A Dispatch protocol's draft carrying <paramref name="graph"/> as its decision graph (P4).</summary>
		private ProtocolVersion DraftWithGraph(ProtocolDecisionGraph graph)
		{
			_world.AddProtocol(1, kind: ProtocolKinds.Dispatch);
			var version = _world.AddVersion(100, 1, ProtocolVersionStatuses.Draft, OldHtml);
			var document = ProtocolContentDocument.FromStoredHtml(OldHtml);
			document.Graph = graph;
			version.ContentJson = document.ToJson();
			return version;
		}

		[Test]
		public async Task SubmitAsync_refuses_a_draft_whose_decision_graph_does_not_validate_and_opens_no_review()
		{
			// No first step: at dispatch this graph would be dropped silently, so it must never reach review.
			var graph = Resgrid.Tests.Protocols.ProtocolDecisionEngineTests.Sample();
			graph.Nodes.ForEach(n => n.IsRoot = false);
			ProtocolDecisionGraphs.Validate(graph).Should().NotBeEmpty();
			DraftWithGraph(graph);

			var result = await _world.Service.SubmitAsync(Manager(), 100, "Case flow");

			result.Error.Should().Be(ProtocolLifecycleErrors.DecisionGraphInvalid);
			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.Draft);
			_world.Approvals.Values.Should().NotContain(a => a.ProtocolVersionId == 100);
		}

		[Test]
		public async Task SubmitAsync_sends_a_draft_with_a_valid_decision_graph_to_review()
		{
			DraftWithGraph(Resgrid.Tests.Protocols.ProtocolDecisionEngineTests.Sample());

			var result = await _world.Service.SubmitAsync(Manager(), 100, "Case flow");

			result.Succeeded.Should().BeTrue();
			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.InReview);
		}

		#endregion Submit

		#region Approval

		private ProtocolVersion InReview(int steps = 1)
		{
			_world.AddProtocol(1);
			var version = _world.AddVersion(100, 1, ProtocolVersionStatuses.InReview, NewHtml);
			for (var i = 1; i <= steps; i++)
				_world.AddStep(300 + i, 100, 1, i);
			return version;
		}

		[Test]
		public async Task ApproveAsync_with_only_a_session_is_refused_and_leaves_the_step_open()
		{
			InReview();

			var result = await _world.Service.ApproveAsync(Manager(), 100, "Looks right", ProtocolLibraryWorld.Session());

			result.Error.Should().Be(ProtocolLifecycleErrors.SignatureRequired);
			_world.Approvals[301].Decision.Should().Be((int)ProtocolApprovalDecisions.Pending);
			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.InReview);
			_world.ApprovalsRepository.Verify(x => x.TryDecideAsync(It.IsAny<ProtocolVersionApproval>(), It.IsAny<CancellationToken>()), Times.Never);
			_world.SignatureRepository.Verify(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task ApproveAsync_with_another_users_proof_is_refused()
		{
			InReview();

			var result = await _world.Service.ApproveAsync(Manager(), 100, null, ProtocolLibraryWorld.StepUp("someone-else"));

			result.Error.Should().Be(ProtocolLifecycleErrors.SignatureRequired);
			_world.SignatureRepository.Verify(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task ApproveAsync_with_a_step_up_signs_the_content_hash_decides_the_step_and_approves_the_last_one()
		{
			var version = InReview();

			var result = await _world.Service.ApproveAsync(Manager(), 100, "  Looks right  ", ProtocolLibraryWorld.StepUp());

			result.Succeeded.Should().BeTrue();
			result.Signature.Should().NotBeNull();
			result.Signature.Level.Should().Be((int)SignatureLevels.StepUp);
			result.Signature.ArtifactSha256.Should().Be(version.ContentHash);
			result.Signature.StatementText.Should().Contain(SignaturePolicy.ShortCode(version.ContentHash));
			_world.SignatureRepository.Verify(x => x.InsertAsync(It.Is<SignatureRecord>(r =>
				r.SubjectKind == (int)SignatureSubjectKinds.ProtocolVersion && r.StatementVersion == SignatureUse.ProtocolApproval.StatementVersion &&
				r.SubjectId == "100" && r.RevisionId == "round-1/step-1" && r.ArtifactSha256 == version.ContentHash && r.SignerUserId == ManagerId),
				It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);

			var step = _world.Approvals[301];
			step.Decision.Should().Be((int)ProtocolApprovalDecisions.Approved);
			step.SignatureRecordId.Should().Be(result.Signature.SignatureRecordId);
			step.DecidedByUserId.Should().Be(ManagerId);
			step.Comment.Should().Be("Looks right");

			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.Approved);
			_world.Versions[100].ApprovedByUserId.Should().Be(ManagerId);
			_world.Events.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.ProtocolVersionApproved)), Times.Once);
		}

		[Test]
		public async Task ApproveAsync_of_the_first_of_two_steps_keeps_the_version_in_review()
		{
			InReview(steps: 2);

			var result = await _world.Service.ApproveAsync(Manager(), 100, null, ProtocolLibraryWorld.StepUp());

			result.Succeeded.Should().BeTrue();
			_world.Approvals[301].Decision.Should().Be((int)ProtocolApprovalDecisions.Approved);
			_world.Approvals[302].Decision.Should().Be((int)ProtocolApprovalDecisions.Pending);
			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.InReview);
		}

		[Test]
		public async Task ApproveAsync_when_the_content_no_longer_hashes_to_its_stored_hash_is_ArtifactChanged_and_signs_nothing()
		{
			InReview();
			// Changed behind the lifecycle: the hash column still describes the text that was submitted.
			_world.Versions[100].ContentHtml = "<p>Something else entirely</p>";

			var result = await _world.Service.ApproveAsync(Manager(), 100, null, ProtocolLibraryWorld.StepUp());

			result.Error.Should().Be(ProtocolLifecycleErrors.ArtifactChanged);
			_world.SignatureRepository.Verify(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_world.ApprovalsRepository.Verify(x => x.TryDecideAsync(It.IsAny<ProtocolVersionApproval>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task ApproveAsync_with_open_review_comments_is_refused()
		{
			InReview();
			_world.OpenReviewComments = 2;

			var result = await _world.Service.ApproveAsync(Manager(), 100, null, ProtocolLibraryWorld.StepUp());

			result.Error.Should().Be(ProtocolLifecycleErrors.OpenComments);
			_world.SignatureRepository.Verify(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_world.Approvals[301].Decision.Should().Be((int)ProtocolApprovalDecisions.Pending);
		}

		[Test]
		public async Task ApproveAsync_that_loses_the_step_to_another_approver_is_AlreadyDecided_and_voids_its_signature()
		{
			InReview();
			_world.ApprovalsRepository.Setup(x => x.TryDecideAsync(It.IsAny<ProtocolVersionApproval>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			var result = await _world.Service.ApproveAsync(Manager(), 100, null, ProtocolLibraryWorld.StepUp());

			result.Error.Should().Be(ProtocolLifecycleErrors.AlreadyDecided);
			var signature = _world.Signatures.Values.Should().ContainSingle().Subject;
			signature.VoidedOn.Should().NotBeNull();
			signature.VoidedByUserId.Should().Be(ManagerId);
			signature.VoidReason.Should().NotBeNullOrWhiteSpace();
			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.InReview);
		}

		[Test]
		public async Task ApproveAsync_when_the_department_requires_a_passkey_refuses_a_step_up()
		{
			InReview();
			_world.Settings = new ProtocolLibrarySettings { RequirePasskeyForApproval = true };

			var result = await _world.Service.ApproveAsync(Manager(), 100, null, ProtocolLibraryWorld.StepUp());

			result.Error.Should().Be(ProtocolLifecycleErrors.PasskeyRequired);
			_world.Approvals[301].Decision.Should().Be((int)ProtocolApprovalDecisions.Pending);
		}

		#endregion Approval

		#region Publish

		/// <summary>Protocol 1 in force at 1.0 (version 90) with version 100 approved over new text.</summary>
		private void PublishedWithApprovedRevision()
		{
			var protocol = _world.AddProtocol(1, ProtocolStatuses.Published, currentVersionId: 90);
			protocol.ProtocolText = OldHtml;
			_world.AddVersion(90, 1, ProtocolVersionStatuses.Published, OldHtml, 1, 0);
			_world.AddApprovedVersion(100, 1, NewHtml);
		}

		[Test]
		public async Task PublishAsync_needs_an_approved_version()
		{
			_world.AddProtocol(1);
			_world.AddVersion(100, 1, ProtocolVersionStatuses.InReview, NewHtml);

			var result = await _world.Service.PublishAsync(Manager(), 100, false, "Change", null, null);

			result.Error.Should().Be(ProtocolLifecycleErrors.NotApproved);
			_world.VersionsRepository.Verify(x => x.TryUpdateAsync(It.IsAny<ProtocolVersion>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task PublishAsync_without_a_change_summary_is_refused()
		{
			PublishedWithApprovedRevision();
			_world.Versions[100].ChangeSummary = null;

			var result = await _world.Service.PublishAsync(Manager(), 100, false, "   ", null, null);

			result.Error.Should().Be(ProtocolLifecycleErrors.ChangeSummaryRequired);
			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.Approved);
		}

		[Test]
		public async Task PublishAsync_now_puts_the_version_in_force_and_the_protocol_text_follows_it()
		{
			PublishedWithApprovedRevision();
			var now = DateTime.UtcNow;

			var result = await _world.Service.PublishAsync(Manager(), 100, false, "Uphill as well as upwind.", null, null);

			result.Succeeded.Should().BeTrue();
			var published = _world.Versions[100];
			published.Status.Should().Be((int)ProtocolVersionStatuses.Published);
			(published.Major, published.Minor).Should().Be((1, 1));
			published.PublishedOn.Should().BeCloseTo(now, TimeSpan.FromMinutes(1));
			published.EffectiveOn.Should().BeCloseTo(now, TimeSpan.FromMinutes(1));
			published.ChangeSummary.Should().Be("Uphill as well as upwind.");

			_world.Versions[90].Status.Should().Be((int)ProtocolVersionStatuses.Superseded);
			_world.Versions[90].SupersededOn.Should().BeCloseTo(now, TimeSpan.FromMinutes(1));

			var protocol = _world.Protocols[1];
			protocol.ProtocolText.Should().Be(published.ContentHtml);
			ProtocolContentHash.Compute(protocol.ProtocolText).Should().Be(published.ContentHash);
			protocol.Status.Should().Be((int)ProtocolStatuses.Published);
			protocol.CurrentVersionId.Should().Be(100);
			protocol.NextReviewOn.Should().BeCloseTo(now.AddDays(365), TimeSpan.FromMinutes(1));
			protocol.LastReviewedOn.Should().BeCloseTo(now, TimeSpan.FromMinutes(1));
			protocol.ReviewReminderStage.Should().Be(0);

			_world.ProtocolsService.Verify(x => x.InvalidateProtocolCachesAsync(DepartmentId), Times.Once);
			_world.Events.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.ProtocolVersionPublished)), Times.Once);
		}

		[Test]
		public async Task Publishing_and_retiring_queue_the_protocol_and_its_files_for_the_knowledge_index()
		{
			PublishedWithApprovedRevision();

			(await _world.Service.PublishAsync(Manager(), 100, false, "Uphill as well as upwind.", null, null)).Succeeded.Should().BeTrue();
			_world.KnowledgeQueue.Verify(x => x.EnqueueAsync(DepartmentId, KnowledgeSourceKinds.Protocol, "1", It.IsAny<CancellationToken>()), Times.Once);
			_world.KnowledgeQueue.Verify(x => x.EnqueueAsync(DepartmentId, KnowledgeSourceKinds.ProtocolFile, "1", It.IsAny<CancellationToken>()), Times.Once);

			(await _world.Service.RetireAsync(Manager(), 1)).Succeeded.Should().BeTrue();
			_world.KnowledgeQueue.Verify(x => x.EnqueueAsync(DepartmentId, KnowledgeSourceKinds.Protocol, "1", It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task Publishing_calls_the_content_version_hooks_and_a_failing_hook_never_fails_the_publish()
		{
			// T5 (round 6F): flashcards drafted from an earlier version are flagged through this seam.
			PublishedWithApprovedRevision();
			var failing = new Mock<IContentVersionPublishedHook>();
			failing.Setup(x => x.OnVersionPublishedAsync(It.IsAny<ContentVersionPublished>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("hook down"));
			var seen = new List<ContentVersionPublished>();
			var recording = new Mock<IContentVersionPublishedHook>();
			recording.Setup(x => x.OnVersionPublishedAsync(It.IsAny<ContentVersionPublished>(), It.IsAny<CancellationToken>()))
				.Callback((ContentVersionPublished p, CancellationToken _) => seen.Add(p)).Returns(Task.CompletedTask);
			_world.PublishedHooks.Add(failing.Object);
			_world.PublishedHooks.Add(recording.Object);

			(await _world.Service.PublishAsync(Manager(), 100, false, "Uphill as well as upwind.", null, null)).Succeeded.Should().BeTrue();
			seen.Should().ContainSingle();
			seen[0].Should().Match<ContentVersionPublished>(p => p.DepartmentId == DepartmentId && p.Kind == ContentVersionKinds.ProtocolVersion && p.ItemId == 1 && p.VersionId == 100);
		}

		[Test]
		public async Task A_scheduled_publish_calls_no_content_version_hook_until_it_is_in_force()
		{
			PublishedWithApprovedRevision();
			var hook = new Mock<IContentVersionPublishedHook>();
			_world.PublishedHooks.Add(hook.Object);

			(await _world.Service.PublishAsync(Manager(), 100, false, "Later.", DateTime.UtcNow.AddDays(3), null)).Succeeded.Should().BeTrue();

			hook.Verify(x => x.OnVersionPublishedAsync(It.IsAny<ContentVersionPublished>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_scheduled_publish_does_not_reindex_until_worker_84_puts_it_in_force()
		{
			PublishedWithApprovedRevision();

			(await _world.Service.PublishAsync(Manager(), 100, false, "Later.", DateTime.UtcNow.AddDays(3), null)).Succeeded.Should().BeTrue();

			_world.KnowledgeQueue.Verify(x => x.EnqueueAsync(It.IsAny<int>(), It.IsAny<KnowledgeSourceKinds>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task PublishAsync_of_a_first_version_numbers_it_1_0_and_a_major_revision_moves_to_the_next_major()
		{
			_world.AddProtocol(1, reviewIntervalDays: null);
			_world.AddApprovedVersion(100, 1, NewHtml);

			var first = await _world.Service.PublishAsync(Manager(), 100, false, "First issue.", null, null);

			first.Succeeded.Should().BeTrue();
			(_world.Versions[100].Major, _world.Versions[100].Minor).Should().Be((1, 0));
			// No review interval: no review date.
			_world.Protocols[1].NextReviewOn.Should().BeNull();

			_world.AddApprovedVersion(110, 1, OldHtml);
			var major = await _world.Service.PublishAsync(Manager(), 110, true, "Rewritten.", null, null);

			major.Succeeded.Should().BeTrue();
			(_world.Versions[110].Major, _world.Versions[110].Minor).Should().Be((2, 0));
			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.Superseded);
		}

		[Test]
		public async Task PublishAsync_when_an_approval_signature_no_longer_matches_the_content_is_ArtifactChanged()
		{
			PublishedWithApprovedRevision();
			_world.Signatures[600].ArtifactSha256 = ProtocolContentHash.Compute("<p>What the approver actually saw</p>");

			var result = await _world.Service.PublishAsync(Manager(), 100, false, "Change", null, null);

			result.Error.Should().Be(ProtocolLifecycleErrors.ArtifactChanged);
			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.Approved);
			_world.Protocols[1].ProtocolText.Should().Be(OldHtml);
			_world.ProtocolRepository.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task PublishAsync_when_the_content_changed_after_approval_is_ArtifactChanged()
		{
			PublishedWithApprovedRevision();
			_world.Versions[100].ContentHtml = "<p>Edited after approval</p>";

			var result = await _world.Service.PublishAsync(Manager(), 100, false, "Change", null, null);

			result.Error.Should().Be(ProtocolLifecycleErrors.ArtifactChanged);
			_world.Protocols[1].ProtocolText.Should().Be(OldHtml);
		}

		[Test]
		public async Task PublishAsync_when_an_approval_signature_was_voided_or_is_only_L1_is_ArtifactChanged()
		{
			PublishedWithApprovedRevision();
			_world.Signatures[600].VoidedOn = DateTime.UtcNow.AddMinutes(-5);

			(await _world.Service.PublishAsync(Manager(), 100, false, "Change", null, null)).Error.Should().Be(ProtocolLifecycleErrors.ArtifactChanged);

			_world.Signatures[600].VoidedOn = null;
			_world.Signatures[600].Level = (int)SignatureLevels.Attest;

			(await _world.Service.PublishAsync(Manager(), 100, false, "Change", null, null)).Error.Should().Be(ProtocolLifecycleErrors.ArtifactChanged);
			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.Approved);
		}

		[Test]
		public async Task PublishAsync_with_a_future_effective_date_schedules_it_and_leaves_the_protocol_alone()
		{
			PublishedWithApprovedRevision();
			var effective = DateTime.UtcNow.AddDays(3);

			var result = await _world.Service.PublishAsync(Manager(), 100, false, "Uphill as well.", effective, null);

			result.Succeeded.Should().BeTrue();
			var scheduled = _world.Versions[100];
			scheduled.Status.Should().Be((int)ProtocolVersionStatuses.Approved);
			scheduled.EffectiveOn.Should().Be(effective);
			scheduled.PublishedByUserId.Should().Be(ManagerId);
			(scheduled.Major, scheduled.Minor).Should().Be((1, 1));

			_world.Versions[90].Status.Should().Be((int)ProtocolVersionStatuses.Published);
			_world.Protocols[1].ProtocolText.Should().Be(OldHtml);
			_world.Protocols[1].CurrentVersionId.Should().Be(90);
			_world.ProtocolRepository.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task PublishAsync_with_an_effective_date_in_the_past_is_refused()
		{
			PublishedWithApprovedRevision();

			var result = await _world.Service.PublishAsync(Manager(), 100, false, "Change", DateTime.UtcNow.AddDays(-1), null);

			result.Error.Should().Be(ProtocolLifecycleErrors.EffectiveDateInPast);
			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.Approved);
		}

		#endregion Publish

		#region Review stamp

		private ProtocolReader Owner() => new ProtocolReader { DepartmentId = DepartmentId, UserId = "owner-1", IsCurrentMember = true };

		private void PublishedForReview()
		{
			var protocol = _world.AddProtocol(1, ProtocolStatuses.Published, currentVersionId: 90, reviewIntervalDays: 180, owner: "owner-1");
			protocol.ProtocolText = OldHtml;
			protocol.NextReviewOn = DateTime.UtcNow.AddDays(-3);
			protocol.LastReviewedOn = DateTime.UtcNow.AddDays(-183);
			protocol.ReviewReminderStage = (int)ProtocolReviewReminderStages.Overdue;
			_world.AddVersion(90, 1, ProtocolVersionStatuses.Published, OldHtml, 1, 0);
		}

		[Test]
		public async Task RecordReviewAsync_by_the_owner_with_step_up_signs_at_L2_and_only_moves_the_review_dates()
		{
			PublishedForReview();
			var now = DateTime.UtcNow;

			var result = await _world.Service.RecordReviewAsync(Owner(), 1, ProtocolLibraryWorld.StepUp("owner-1"));

			result.Succeeded.Should().BeTrue();
			result.Signature.Level.Should().Be((int)SignatureLevels.StepUp);
			result.Signature.SubjectKind.Should().Be((int)SignatureSubjectKinds.ProtocolReview);
			result.Signature.StatementVersion.Should().Be(SignatureUse.ProtocolReviewStamp.StatementVersion);
			result.Signature.ArtifactSha256.Should().Be(_world.Versions[90].ContentHash);
			result.Signature.RevisionId.Should().Be("1.0");
			result.Signature.SignerUserId.Should().Be("owner-1");

			var protocol = _world.Protocols[1];
			protocol.NextReviewOn.Should().BeCloseTo(now.AddDays(180), TimeSpan.FromMinutes(1));
			protocol.LastReviewedOn.Should().BeCloseTo(now, TimeSpan.FromMinutes(1));
			protocol.ReviewReminderStage.Should().Be(0);
			protocol.ProtocolText.Should().Be(OldHtml);
			protocol.CurrentVersionId.Should().Be(90);

			// R-P9: no new version and the version in force is not touched.
			_world.VersionsRepository.Verify(x => x.InsertAsync(It.IsAny<ProtocolVersion>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_world.VersionsRepository.Verify(x => x.TryUpdateAsync(It.IsAny<ProtocolVersion>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
			_world.Versions.Should().HaveCount(1);
			_world.Versions[90].Status.Should().Be((int)ProtocolVersionStatuses.Published);
		}

		[Test]
		public async Task RecordReviewAsync_with_only_a_session_is_refused_and_moves_nothing()
		{
			PublishedForReview();
			var before = _world.Protocols[1].NextReviewOn;

			var result = await _world.Service.RecordReviewAsync(Owner(), 1, ProtocolLibraryWorld.Session("owner-1"));

			result.Succeeded.Should().BeFalse();
			result.Error.Should().Be(ProtocolLifecycleErrors.SignatureRequired);
			_world.Protocols[1].NextReviewOn.Should().Be(before);
			_world.Protocols[1].ReviewReminderStage.Should().Be((int)ProtocolReviewReminderStages.Overdue);
			_world.SignatureRepository.Verify(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_world.ProtocolRepository.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task RecordReviewAsync_by_a_member_who_neither_owns_nor_approves_is_refused()
		{
			PublishedForReview();
			var reader = new ProtocolReader { DepartmentId = DepartmentId, UserId = "member-7", IsCurrentMember = true, CanAuthor = true };

			var result = await _world.Service.RecordReviewAsync(reader, 1, ProtocolLibraryWorld.StepUp("member-7"));

			result.Error.Should().Be(ProtocolLifecycleErrors.NotAuthorized);
		}

		#endregion Review stamp

		#region Retention

		[TestCase(ProtocolVersionStatuses.InReview)]
		[TestCase(ProtocolVersionStatuses.Approved)]
		[TestCase(ProtocolVersionStatuses.Published)]
		[TestCase(ProtocolVersionStatuses.Superseded)]
		[TestCase(ProtocolVersionStatuses.Retired)]
		[TestCase(ProtocolVersionStatuses.Legacy)]
		public async Task DiscardDraftAsync_never_deletes_a_version_that_left_draft(ProtocolVersionStatuses status)
		{
			_world.AddProtocol(1);
			_world.AddVersion(100, 1, status, OldHtml);

			var result = await _world.Service.DiscardDraftAsync(Manager(), 100);

			result.Error.Should().Be(ProtocolLifecycleErrors.RetentionProtected);
			_world.Versions.Should().ContainKey(100);
			_world.VersionsRepository.Verify(x => x.DeleteAsync(It.IsAny<ProtocolVersion>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task DiscardDraftAsync_never_deletes_a_draft_that_was_submitted_before()
		{
			_world.AddProtocol(1);
			_world.AddVersion(100, 1, ProtocolVersionStatuses.Draft, OldHtml);
			// Rejected back to draft: its approval rows are the record of that review.
			_world.AddStep(301, 100, 1, 1, ProtocolApprovalDecisions.Rejected, decidedBy: "approver-2");

			var result = await _world.Service.DiscardDraftAsync(Manager(), 100);

			result.Error.Should().Be(ProtocolLifecycleErrors.RetentionProtected);
			_world.VersionsRepository.Verify(x => x.DeleteAsync(It.IsAny<ProtocolVersion>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task DiscardDraftAsync_deletes_a_draft_that_was_never_submitted()
		{
			_world.AddProtocol(1);
			_world.AddVersion(100, 1, ProtocolVersionStatuses.Draft, OldHtml);

			var result = await _world.Service.DiscardDraftAsync(Manager(), 100);

			result.Succeeded.Should().BeTrue();
			_world.Versions.Should().NotContainKey(100);
			_world.VersionsRepository.Verify(x => x.DeleteAsync(It.Is<ProtocolVersion>(v => v.ProtocolVersionId == 100), It.IsAny<CancellationToken>()), Times.Once);
		}

		#endregion Retention
	}

	/// <summary>
	/// An in-memory department for <see cref="ProtocolLibraryService"/> with a real <see cref="SignatureService"/>: repositories
	/// hand out copies and only a write changes what is stored, so a test sees exactly what the service persisted.
	/// </summary>
	internal sealed class ProtocolLibraryWorld
	{
		public const int DepartmentId = 10;
		public const string ManagerId = "manager-1";

		public readonly Dictionary<int, DispatchProtocol> Protocols = new Dictionary<int, DispatchProtocol>();
		public readonly Dictionary<int, ProtocolVersion> Versions = new Dictionary<int, ProtocolVersion>();
		public readonly Dictionary<int, ProtocolVersionApproval> Approvals = new Dictionary<int, ProtocolVersionApproval>();
		public readonly Dictionary<int, SignatureRecord> Signatures = new Dictionary<int, SignatureRecord>();
		public ProtocolLibrarySettings Settings;
		public int OpenReviewComments;

		private int _nextVersionId = 5000;
		private int _nextApprovalId = 8000;
		private int _nextSignatureId = 9000;

		public readonly Mock<IProtocolsService> ProtocolsService = new Mock<IProtocolsService>();
		public readonly Mock<IDispatchProtocolRepository> ProtocolRepository = new Mock<IDispatchProtocolRepository>();
		public readonly Mock<IProtocolVersionsRepository> VersionsRepository = new Mock<IProtocolVersionsRepository>();
		public readonly Mock<IProtocolVersionApprovalsRepository> ApprovalsRepository = new Mock<IProtocolVersionApprovalsRepository>();
		public readonly Mock<IProtocolAudiencesRepository> Audiences = new Mock<IProtocolAudiencesRepository>();
		public readonly Mock<IProtocolCommentsRepository> Comments = new Mock<IProtocolCommentsRepository>();
		public readonly Mock<IProtocolCategoriesRepository> Categories = new Mock<IProtocolCategoriesRepository>();
		public readonly Mock<IProtocolLibraryRepository> Library = new Mock<IProtocolLibraryRepository>();
		public readonly Mock<ISignatureRecordsRepository> SignatureRepository = new Mock<ISignatureRecordsRepository>();
		public readonly Mock<IPersonnelCertificationRepository> Certifications = new Mock<IPersonnelCertificationRepository>();
		public readonly Mock<IUserProfileService> Profiles = new Mock<IUserProfileService>();
		public readonly Mock<IPersonnelRolesService> Roles = new Mock<IPersonnelRolesService>();
		public readonly Mock<IDepartmentSettingsRepository> SettingsRepository = new Mock<IDepartmentSettingsRepository>();
		public readonly Mock<IDepartmentSettingsService> SettingsService = new Mock<IDepartmentSettingsService>();
		public readonly Mock<IMemberPinnedItemsRepository> Pins = new Mock<IMemberPinnedItemsRepository>();
		public readonly Mock<IDepartmentsService> Departments = new Mock<IDepartmentsService>();
		public readonly Mock<IDepartmentGroupsService> Groups = new Mock<IDepartmentGroupsService>();
		public readonly Mock<IEventAggregator> Events = new Mock<IEventAggregator>();
		public readonly Mock<IUnitOfWork> UnitOfWork = new Mock<IUnitOfWork>();
		public readonly Mock<ICallsService> Calls = new Mock<ICallsService>();
		public readonly Mock<IShiftsService> Shifts = new Mock<IShiftsService>();
		public readonly Mock<ICommunicationService> Communication = new Mock<ICommunicationService>();
		public readonly Mock<IDocumentNumberingService> Numbering = new Mock<IDocumentNumberingService>();
		public readonly Mock<ISearchProjectionService> SearchProjections = new Mock<ISearchProjectionService>();
		public readonly Mock<IKnowledgeIndexQueue> KnowledgeQueue = new Mock<IKnowledgeIndexQueue>();
		// Round 3 A: document-backed versions (file + text) and the P2 acknowledgment / notification hooks.
		public readonly Mock<IProtocolVersionFilesRepository> Files = new Mock<IProtocolVersionFilesRepository>();
		public readonly Mock<IProtocolFileTextsRepository> FileTexts = new Mock<IProtocolFileTextsRepository>();
		public readonly Mock<IProtocolAcknowledgmentService> Acknowledgments = new Mock<IProtocolAcknowledgmentService>();
		public readonly Mock<IProtocolNotificationService> Notifications = new Mock<IProtocolNotificationService>();
		// Round 6 A (P5): attribution kept on versions, the attachments manifest and approved translations of the open format.
		public readonly Dictionary<(int Kind, int Id), ContentAttribution> Attributions = new Dictionary<(int Kind, int Id), ContentAttribution>();
		public readonly Mock<IContentAttributionsRepository> AttributionsRepository = new Mock<IContentAttributionsRepository>();
		public readonly Mock<IDispatchProtocolAttachmentRepository> AttachmentHashes = new Mock<IDispatchProtocolAttachmentRepository>();
		public readonly Mock<IProtocolVersionTranslationsRepository> Translations = new Mock<IProtocolVersionTranslationsRepository>();
		public readonly Mock<IUserProfileService> UserProfiles = new Mock<IUserProfileService>();
		public readonly Mock<IContentSetCopyLocks> SetLocks = new Mock<IContentSetCopyLocks>();

		public readonly SignatureService SignatureService;
		public readonly ProtocolLibraryService Service;

		/// <summary>T5: the publish hooks the service calls after a version goes into force (round 6F flashcards).</summary>
		public readonly List<IContentVersionPublishedHook> PublishedHooks = new List<IContentVersionPublishedHook>();

		public ProtocolLibraryWorld()
		{
			AttributionsRepository.Setup(x => x.GetForSubjectAsync(It.IsAny<int>(), It.IsAny<ContentAttributionSubjects>(), It.IsAny<int>()))
				.ReturnsAsync((int departmentId, ContentAttributionSubjects kind, int id) => Attributions.TryGetValue(((int)kind, id), out var a) && a.DepartmentId == departmentId ? a : null);
			AttributionsRepository.Setup(x => x.SaveForSubjectAsync(It.IsAny<ContentAttribution>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ContentAttribution a, CancellationToken _) => { Attributions[(a.SubjectKind, a.SubjectId)] = a; return a; });
			ProtocolsService.Setup(x => x.GetProtocolHeaderByIdAsync(It.IsAny<int>()))
				.ReturnsAsync((int id) => Protocols.TryGetValue(id, out var p) ? Copy(p) : null);
			ProtocolsService.Setup(x => x.InvalidateProtocolCachesAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
			ProtocolRepository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DispatchProtocol p, CancellationToken _, bool __) =>
				{
					Protocols[p.DispatchProtocolId] = Copy(p);
					return p;
				});

			VersionsRepository.Setup(x => x.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int departmentId, int id) => Versions.TryGetValue(id, out var v) && v.DepartmentId == departmentId ? Copy(v) : null);
			VersionsRepository.Setup(x => x.GetForProtocolAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int departmentId, int protocolId, bool _) => Versions.Values
					.Where(v => v.DepartmentId == departmentId && v.DispatchProtocolId == protocolId)
					.OrderByDescending(v => v.ProtocolVersionId).Select(Copy).ToList());
			VersionsRepository.Setup(x => x.TryUpdateAsync(It.IsAny<ProtocolVersion>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ProtocolVersion v, long expected, CancellationToken _) =>
				{
					// The repository's conditional UPDATE: only from the expected RowVersion, which it then bumps.
					if (!Versions.TryGetValue(v.ProtocolVersionId, out var stored) || stored.RowVersion != expected)
						return false;
					v.RowVersion = expected + 1;
					Versions[v.ProtocolVersionId] = Copy(v);
					return true;
				});
			VersionsRepository.Setup(x => x.InsertAsync(It.IsAny<ProtocolVersion>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ProtocolVersion v, CancellationToken _, bool __) =>
				{
					v.ProtocolVersionId = _nextVersionId++;
					Versions[v.ProtocolVersionId] = Copy(v);
					return v;
				});
			VersionsRepository.Setup(x => x.DeleteAsync(It.IsAny<ProtocolVersion>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ProtocolVersion v, CancellationToken _) => Versions.Remove(v.ProtocolVersionId));
			VersionsRepository.Setup(x => x.GetDueActivationsAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<ProtocolVersion>());
			VersionsRepository.Setup(x => x.GetDueExpirationsAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<ProtocolVersion>());
			VersionsRepository.Setup(x => x.GetPurgeCandidatesAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<ProtocolVersion>());
			VersionsRepository.Setup(x => x.PurgeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int departmentId, int id, CancellationToken _) => Versions.Remove(id) ? 1 : 0);

			ApprovalsRepository.Setup(x => x.GetForVersionAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int departmentId, int versionId) => Approvals.Values
					.Where(a => a.DepartmentId == departmentId && a.ProtocolVersionId == versionId).Select(Copy).ToList());
			ApprovalsRepository.Setup(x => x.TryDecideAsync(It.IsAny<ProtocolVersionApproval>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ProtocolVersionApproval a, CancellationToken _) =>
				{
					if (!Approvals.TryGetValue(a.ProtocolVersionApprovalId, out var stored) || stored.Decision != (int)ProtocolApprovalDecisions.Pending)
						return false;
					Approvals[a.ProtocolVersionApprovalId] = Copy(a);
					return true;
				});
			ApprovalsRepository.Setup(x => x.InsertAsync(It.IsAny<ProtocolVersionApproval>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ProtocolVersionApproval a, CancellationToken _, bool __) =>
				{
					a.ProtocolVersionApprovalId = _nextApprovalId++;
					Approvals[a.ProtocolVersionApprovalId] = Copy(a);
					return a;
				});

			Comments.Setup(x => x.CountOpenReviewCommentsAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(() => OpenReviewComments);

			Library.Setup(x => x.TryBumpRowVersionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int departmentId, int protocolId, long expected, CancellationToken _) =>
				{
					if (!Protocols.TryGetValue(protocolId, out var stored) || stored.DepartmentId != departmentId || stored.RowVersion != expected)
						return false;
					stored.RowVersion = expected + 1;
					return true;
				});
			Library.Setup(x => x.GetReviewRemindersDueAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<ProtocolReviewDue>());
			Library.Setup(x => x.MarkReviewReminderAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			Library.Setup(x => x.GetDepartmentsWithRetentionWorkAsync(It.IsAny<int>())).ReturnsAsync(new List<int>());

			SignatureRepository.Setup(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRecord r, CancellationToken _, bool __) =>
				{
					r.SignatureRecordId = _nextSignatureId++;
					Signatures[r.SignatureRecordId] = Copy(r);
					return r;
				});
			SignatureRepository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRecord r, CancellationToken _, bool __) =>
				{
					Signatures[r.SignatureRecordId] = Copy(r);
					return r;
				});
			SignatureRepository.Setup(x => x.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int departmentId, int id) => Signatures.TryGetValue(id, out var r) && r.DepartmentId == departmentId ? Copy(r) : null);

			SettingsRepository.Setup(x => x.GetDepartmentSettingByIdTypeAsync(It.IsAny<int>(), DepartmentSettingTypes.ProtocolLibrarySettings))
				.ReturnsAsync((int departmentId, DepartmentSettingTypes type) => Settings == null ? null : new DepartmentSetting
				{
					DepartmentId = departmentId,
					SettingType = (int)type,
					// Stored the way SaveSettingsAsync writes it (ProtoBuf, base64).
					Setting = ObjectSerialization.Serialize(Settings)
				});
			SettingsService.Setup(x => x.GetTextToCallNumberForDepartmentAsync(It.IsAny<int>())).ReturnsAsync("5550100");

			Profiles.Setup(x => x.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync(new UserProfile { FirstName = "Pat", LastName = "Approver" });
			Roles.Setup(x => x.GetRolesForUserAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(new List<PersonnelRole>());

			Communication.Setup(x => x.SendNotificationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(),
				It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>())).ReturnsAsync(true);

			SignatureService = new SignatureService(SignatureRepository.Object, VersionsRepository.Object, Certifications.Object, Profiles.Object, Roles.Object, Events.Object,
				Files.Object, Resgrid.Tests.Security.StepUpTestPolicies.Strict());
			Service = new ProtocolLibraryService(ProtocolsService.Object, ProtocolRepository.Object, VersionsRepository.Object, ApprovalsRepository.Object,
				Audiences.Object, Comments.Object, Categories.Object, Library.Object, SignatureService, SettingsRepository.Object, SettingsService.Object,
				Pins.Object, Departments.Object, Roles.Object, Groups.Object, Events.Object, UnitOfWork.Object,
				new Lazy<ICallsService>(() => Calls.Object), new Lazy<IShiftsService>(() => Shifts.Object),
				new Lazy<ICommunicationService>(() => Communication.Object), new Lazy<IDocumentNumberingService>(() => Numbering.Object),
				new Lazy<ISearchProjectionService>(() => SearchProjections.Object), KnowledgeQueue.Object, Files.Object, FileTexts.Object,
				new Lazy<IProtocolAcknowledgmentService>(() => Acknowledgments.Object), new Lazy<IProtocolNotificationService>(() => Notifications.Object),
				AttributionsRepository.Object, AttachmentHashes.Object, Translations.Object, PublishedHooks, UserProfiles.Object, SetLocks.Object);
		}

		public static ProtocolReader Manager() => new ProtocolReader
		{
			DepartmentId = DepartmentId,
			UserId = ManagerId,
			IsCurrentMember = true,
			CanAuthor = true,
			CanApprove = true
		};

		public static SignatureProof StepUp(string userId = ManagerId) => new SignatureProof
		{
			UserId = userId,
			StepUpVerifiedOnUtc = DateTime.UtcNow.AddMinutes(-1),
			StepUpMethod = 1
		};

		public static SignatureProof Session(string userId = ManagerId) => SignatureProof.SessionOnly(userId);

		public DispatchProtocol AddProtocol(int id, ProtocolStatuses status = ProtocolStatuses.Draft, int? currentVersionId = null, ProtocolKinds kind = ProtocolKinds.Sog,
			int? reviewIntervalDays = 365, string owner = ManagerId)
		{
			var protocol = new DispatchProtocol
			{
				DispatchProtocolId = id,
				DepartmentId = DepartmentId,
				Name = "Hazmat spill " + id,
				Code = "HZ-" + id,
				Kind = (int)kind,
				Status = (int)status,
				CurrentVersionId = currentVersionId,
				ReviewIntervalDays = reviewIntervalDays,
				OwnerUserId = owner,
				RowVersion = 7,
				CreatedOn = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc),
				UpdatedOn = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc),
				UpdatedByUserId = "author-0"
			};
			Protocols[id] = protocol;
			return protocol;
		}

		public ProtocolVersion AddVersion(int id, int protocolId, ProtocolVersionStatuses status, string html, int major = 0, int minor = 0)
		{
			var version = new ProtocolVersion
			{
				ProtocolVersionId = id,
				DepartmentId = DepartmentId,
				DispatchProtocolId = protocolId,
				Status = (int)status,
				Major = major,
				Minor = minor,
				ContentHtml = html,
				ContentJson = ProtocolContentDocument.FromStoredHtml(html).ToJson(),
				ContentHash = ProtocolContentHash.Compute(html),
				RowVersion = 3,
				CreatedOn = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
				CreatedByUserId = "author-0",
				UpdatedOn = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc),
				UpdatedByUserId = "author-0"
			};
			Versions[id] = version;
			return version;
		}

		public ProtocolVersionApproval AddStep(int id, int versionId, int round = 1, int order = 1, ProtocolApprovalDecisions decision = ProtocolApprovalDecisions.Pending,
			int? signatureRecordId = null, string decidedBy = null)
		{
			var step = new ProtocolVersionApproval
			{
				ProtocolVersionApprovalId = id,
				DepartmentId = DepartmentId,
				ProtocolVersionId = versionId,
				Round = round,
				StepOrder = order,
				Decision = (int)decision,
				SignatureRecordId = signatureRecordId,
				DecidedByUserId = decidedBy,
				DecidedOn = decidedBy == null ? (DateTime?)null : DateTime.UtcNow.AddDays(-1),
				CreatedOn = DateTime.UtcNow.AddDays(-2)
			};
			Approvals[id] = step;
			return step;
		}

		public SignatureRecord AddSignature(int id, int versionId, string hash, SignatureLevels level = SignatureLevels.StepUp)
		{
			var record = new SignatureRecord
			{
				SignatureRecordId = id,
				DepartmentId = DepartmentId,
				SubjectKind = (int)SignatureSubjectKinds.ProtocolVersion,
				SubjectId = versionId.ToString(),
				SignerUserId = "approver-2",
				Intent = (int)SignatureIntents.Approve,
				Level = (int)level,
				Method = level == SignatureLevels.StepUp ? (int)SignatureMethods.StepUp : (int)SignatureMethods.Session,
				ArtifactSha256 = hash,
				SignedOn = DateTime.UtcNow.AddDays(-1),
				CreatedOn = DateTime.UtcNow.AddDays(-1)
			};
			Signatures[id] = record;
			return record;
		}

		/// <summary>An approved version whose one step (id 300 + id) carries a valid L2 signature (id 500 + id) over its content.</summary>
		public ProtocolVersion AddApprovedVersion(int id, int protocolId, string html)
		{
			var version = AddVersion(id, protocolId, ProtocolVersionStatuses.Approved, html);
			version.ChangeSummary = "Clarified the size-up.";
			version.ApprovedByUserId = "approver-2";
			version.ApprovedOn = DateTime.UtcNow.AddDays(-1);
			AddSignature(500 + id, id, version.ContentHash);
			AddStep(300 + id, id, 1, 1, ProtocolApprovalDecisions.Approved, 500 + id, "approver-2");
			return version;
		}

		/// <summary>A shallow copy of the settable properties: what a repository read would hand back.</summary>
		public static T Copy<T>(T source) where T : class, new()
		{
			if (source == null)
				return null;

			var copy = new T();
			foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
			{
				if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0 || property.Name == "IdValue")
					continue;
				property.SetValue(copy, property.GetValue(source));
			}

			return copy;
		}
	}
}

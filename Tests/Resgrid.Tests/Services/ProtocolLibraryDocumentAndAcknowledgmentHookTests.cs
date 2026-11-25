using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;
using static Resgrid.Tests.Services.ProtocolLibraryWorld;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The library's round-3 seams: a document-backed version's hash is its file's SHA-256, recomputed from the stored bytes on every
	/// approval, publish and verification (a swapped file fails like a changed text); a document draft needs its file to be submitted;
	/// approvers hear about a submitted or advanced step; publish, schedule, retire and audience changes drive acknowledgment; and
	/// the department import wizard's preview runs the P1 exchange format's import without writing anything.
	/// </summary>
	[TestFixture]
	public class ProtocolLibraryDocumentAndAcknowledgmentHookTests
	{
		private ProtocolLibraryWorld _world;
		private readonly Dictionary<int, ProtocolVersionFile> _files = new Dictionary<int, ProtocolVersionFile>();

		[SetUp]
		public void SetUp()
		{
			_world = new ProtocolLibraryWorld();
			_files.Clear();
			_world.Files.Setup(x => x.GetWithDataForVersionAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int v) => _files.TryGetValue(v, out var f) && f.DepartmentId == d ? f : null);
			_world.Files.Setup(x => x.GetMetadataForVersionAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int v) => _files.TryGetValue(v, out var f) && f.DepartmentId == d
					? new ProtocolVersionFile { ProtocolVersionFileId = f.ProtocolVersionFileId, DepartmentId = d, ProtocolVersionId = v, Sha256 = f.Sha256, FileName = f.FileName }
					: null);
		}

		private ProtocolVersion DocumentVersion(int versionId, int protocolId, ProtocolVersionStatuses status, byte[] bytes)
		{
			var version = _world.AddVersion(versionId, protocolId, status, ProtocolDocumentRules.StubHtml("Mayday", "mayday.pdf"));
			version.ContentKind = (int)ProtocolVersionContentKinds.Document;
			version.ContentHash = ProtocolDocumentRules.HashFile(bytes);
			_files[versionId] = new ProtocolVersionFile
			{
				ProtocolVersionFileId = versionId * 10, DepartmentId = DepartmentId, DispatchProtocolId = protocolId, ProtocolVersionId = versionId,
				FileName = "mayday.pdf", ContentType = ProtocolDocumentRules.PdfContentType, Data = bytes, Size = bytes.Length, Sha256 = ProtocolDocumentRules.HashFile(bytes)
			};
			return version;
		}

		[Test]
		public async Task A_document_version_hashes_to_its_file_and_a_swapped_file_fails_approval()
		{
			_world.AddProtocol(1);
			var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 1, 2, 3 };
			var version = DocumentVersion(100, 1, ProtocolVersionStatuses.InReview, bytes);
			_world.AddStep(301, 100);

			(await ProtocolVersionHashing.CurrentHashAsync(version, _world.Files.Object)).Should().Be(ProtocolDocumentRules.HashFile(bytes));
			ProtocolVersionHashing.CurrentHashAsync(version, _world.Files.Object).Result.Should().NotBe(ProtocolContentHash.Compute(version.ContentHtml),
				"the stub text a document version carries is not its content");

			// The bytes are replaced behind the lifecycle: the stored hash no longer describes the file.
			_files[100].Data = new byte[] { 0x25, 0x50, 0x44, 0x46, 9, 9, 9 };
			var refused = await _world.Service.ApproveAsync(Manager(), 100, null, StepUp());
			refused.Error.Should().Be(ProtocolLifecycleErrors.ArtifactChanged);

			_files[100].Data = bytes;
			var approved = await _world.Service.ApproveAsync(Manager(), 100, null, StepUp());
			approved.Succeeded.Should().BeTrue();
			approved.Signature.ArtifactSha256.Should().Be(ProtocolDocumentRules.HashFile(bytes), "the approval is bound to the file's SHA-256");
		}

		[Test]
		public async Task A_document_version_whose_file_is_gone_never_verifies()
		{
			_world.AddProtocol(1);
			var version = DocumentVersion(100, 1, ProtocolVersionStatuses.Published, new byte[] { 1, 2, 3 });
			_files.Remove(100);

			(await ProtocolVersionHashing.CurrentHashAsync(version, _world.Files.Object)).Should().BeNull();
		}

		[Test]
		public async Task The_signing_layer_verifies_a_document_version_against_its_file_bytes()
		{
			_world.AddProtocol(1);
			var bytes = new byte[] { 7, 7, 7, 7 };
			DocumentVersion(100, 1, ProtocolVersionStatuses.Published, bytes);
			_world.AddSignature(600, 100, ProtocolDocumentRules.HashFile(bytes));

			(await _world.SignatureService.VerifyAsync(DepartmentId, 600)).State.Should().Be(SignatureVerificationStates.Valid);

			_files[100].Data = new byte[] { 8, 8, 8, 8 };
			(await _world.SignatureService.VerifyAsync(DepartmentId, 600)).State.Should().Be(SignatureVerificationStates.ArtifactChanged);
		}

		[Test]
		public async Task A_document_draft_cannot_be_submitted_without_its_file()
		{
			_world.AddProtocol(1);
			var draft = DocumentVersion(100, 1, ProtocolVersionStatuses.Draft, new byte[] { 1 });
			_files.Remove(100);

			var result = await _world.Service.SubmitAsync(Manager(), 100, "First issue");

			result.Error.Should().Be(ProtocolLifecycleErrors.FileRequired);
			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.Draft);
		}

		[Test]
		public async Task Saving_a_document_draft_never_replaces_its_file_content_with_editor_html()
		{
			_world.AddProtocol(1);
			var bytes = new byte[] { 1, 2, 3 };
			var draft = DocumentVersion(100, 1, ProtocolVersionStatuses.Draft, bytes);

			var result = await _world.Service.SaveDraftAsync(Manager(), 1, new ProtocolDraftInput
			{
				ContentHtml = "<p>typed over</p>", ChangeSummary = "New file", RequiresAcknowledgment = true, ExpectedRowVersion = draft.RowVersion
			});

			result.Succeeded.Should().BeTrue();
			_world.Versions[100].ContentHash.Should().Be(ProtocolDocumentRules.HashFile(bytes));
			_world.Versions[100].ContentKind.Should().Be((int)ProtocolVersionContentKinds.Document);
			_world.Versions[100].ChangeSummary.Should().Be("New file");
		}

		[Test]
		public async Task Submitting_notifies_the_approvers_of_the_first_step_and_approving_notifies_the_next()
		{
			_world.AddProtocol(1);
			_world.AddVersion(100, 1, ProtocolVersionStatuses.Draft, "<p>Text</p>");

			(await _world.Service.SubmitAsync(Manager(), 100, "First")).Succeeded.Should().BeTrue();
			_world.Notifications.Verify(x => x.NotifyApproversAsync(DepartmentId, It.Is<DispatchProtocol>(p => p.DispatchProtocolId == 1), It.Is<ProtocolVersion>(v => v.ProtocolVersionId == 100),
				It.Is<ProtocolVersionApproval>(s => s.StepOrder == 1), It.IsAny<CancellationToken>()), Times.Once);

			// A two-step review: approving step 1 tells step 2's approvers.
			_world.AddProtocol(2);
			_world.AddVersion(200, 2, ProtocolVersionStatuses.InReview, "<p>Two</p>");
			_world.AddStep(401, 200, 1, 1);
			_world.AddStep(402, 200, 1, 2);
			(await _world.Service.ApproveAsync(Manager(), 200, null, StepUp())).Succeeded.Should().BeTrue();
			_world.Notifications.Verify(x => x.NotifyApproversAsync(DepartmentId, It.IsAny<DispatchProtocol>(), It.Is<ProtocolVersion>(v => v.ProtocolVersionId == 200),
				It.Is<ProtocolVersionApproval>(s => s.ProtocolVersionApprovalId == 402), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_failing_notifier_never_fails_the_submit()
		{
			_world.AddProtocol(1);
			_world.AddVersion(100, 1, ProtocolVersionStatuses.Draft, "<p>Text</p>");
			_world.Notifications.Setup(x => x.NotifyApproversAsync(It.IsAny<int>(), It.IsAny<DispatchProtocol>(), It.IsAny<ProtocolVersion>(), It.IsAny<ProtocolVersionApproval>(),
				It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("push provider down"));

			(await _world.Service.SubmitAsync(Manager(), 100, "First")).Succeeded.Should().BeTrue();
		}

		[Test]
		public async Task Publishing_now_activates_acknowledgment_and_freezes_the_quiz_training_into_the_version()
		{
			var protocol = _world.AddProtocol(1);
			protocol.AckTrainingId = 77;
			_world.AddApprovedVersion(100, 1, "<p>Policy</p>");
			_world.Versions[100].RequiresAcknowledgment = true;

			var result = await _world.Service.PublishAsync(Manager(), 100, false, "First issue", null, null);

			result.Succeeded.Should().BeTrue();
			_world.Versions[100].QuizTrainingId.Should().Be(77);
			_world.Acknowledgments.Verify(x => x.OnVersionActivatedAsync(It.Is<DispatchProtocol>(p => p.DispatchProtocolId == 1), It.Is<ProtocolVersion>(v => v.ProtocolVersionId == 100),
				It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Scheduling_a_version_assigns_it_from_publish()
		{
			_world.AddProtocol(1);
			_world.AddApprovedVersion(100, 1, "<p>Policy</p>");
			_world.Versions[100].RequiresAcknowledgment = true;

			var result = await _world.Service.PublishAsync(Manager(), 100, false, "Next quarter", DateTime.UtcNow.AddDays(20), null);

			result.Succeeded.Should().BeTrue();
			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.Approved);
			_world.Acknowledgments.Verify(x => x.AssignVersionAsync(It.IsAny<DispatchProtocol>(), It.Is<ProtocolVersion>(v => v.ProtocolVersionId == 100 && v.EffectiveOn.HasValue),
				It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_world.Acknowledgments.Verify(x => x.OnVersionActivatedAsync(It.IsAny<DispatchProtocol>(), It.IsAny<ProtocolVersion>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Retiring_withdraws_open_acknowledgments_and_an_audience_change_re_evaluates_them()
		{
			_world.AddProtocol(1, ProtocolStatuses.Published, currentVersionId: 90);
			_world.AddVersion(90, 1, ProtocolVersionStatuses.Published, "<p>x</p>", 1, 0);

			await _world.Service.SetAudienceAsync(Manager(), 1, new[] { new ContentAudienceRule(ContentAudienceKinds.Group, "5") });
			_world.Acknowledgments.Verify(x => x.ReevaluateProtocolAsync(DepartmentId, 1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

			(await _world.Service.RetireAsync(Manager(), 1)).Succeeded.Should().BeTrue();
			_world.Acknowledgments.Verify(x => x.OnProtocolRetiredAsync(It.Is<DispatchProtocol>(p => p.DispatchProtocolId == 1), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task An_expiring_bulletin_is_retired_by_worker_84_which_withdraws_its_open_acknowledgments()
		{
			_world.AddProtocol(9, ProtocolStatuses.Published, currentVersionId: 901, kind: ProtocolKinds.Bulletin);
			var version = _world.AddVersion(901, 9, ProtocolVersionStatuses.Published, "<p>Hydrant 42</p>", 1, 0);
			version.ExpiresOn = DateTime.UtcNow.AddMinutes(-1);
			_world.VersionsRepository.Setup(x => x.GetDueExpirationsAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<ProtocolVersion> { version });

			(await _world.Service.ExpireDueVersionsAsync(DateTime.UtcNow)).Should().Be(1);

			_world.Protocols[9].Status.Should().Be((int)ProtocolStatuses.Retired, "an expired bulletin leaves the library by itself");
			_world.Acknowledgments.Verify(x => x.OnProtocolRetiredAsync(It.Is<DispatchProtocol>(p => p.DispatchProtocolId == 9), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task An_acknowledgment_failure_never_fails_the_publish()
		{
			_world.AddProtocol(1);
			_world.AddApprovedVersion(100, 1, "<p>Policy</p>");
			_world.Acknowledgments.Setup(x => x.OnVersionActivatedAsync(It.IsAny<DispatchProtocol>(), It.IsAny<ProtocolVersion>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("database busy"));

			(await _world.Service.PublishAsync(Manager(), 100, false, "First issue", null, null)).Succeeded.Should().BeTrue();
			_world.Versions[100].Status.Should().Be((int)ProtocolVersionStatuses.Published);
		}

		[Test]
		public async Task Bulletins_are_never_offered_as_an_authorable_kind_or_taken_from_an_import()
		{
			ProtocolKindRules.AuthorableKinds.Should().NotContain(ProtocolKinds.Bulletin);
			ProtocolKindRules.IsAuthorable((int)ProtocolKinds.Bulletin).Should().BeFalse();

			_world.ProtocolsService.Setup(x => x.SaveProtocolAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DispatchProtocol p, CancellationToken _) => { p.DispatchProtocolId = 50; _world.Protocols[50] = p; return p; });
			var document = new ProtocolExchangeDocument
			{
				Protocols = new List<ProtocolExchangeItem> { new ProtocolExchangeItem { Code = "B1", Name = "Sneaky", Kind = "Bulletin", Content = ProtocolContentDocument.FromHtml("<p>x</p>") } }
			};
			var preview = await _world.Service.PreviewImportAsync(Manager(), document);
			preview.Created.Should().Be(1);
			await _world.Service.ImportAsync(Manager(), document);
			_world.Protocols[50].Kind.Should().Be((int)ProtocolKinds.Dispatch, "an imported protocol is never a bulletin");
		}

		[Test]
		public async Task The_import_wizard_preview_reads_the_p1_exchange_format_and_writes_nothing()
		{
			var exported = new ProtocolExchangeDocument
			{
				ExportedOn = DateTime.UtcNow,
				Protocols = new List<ProtocolExchangeItem>
				{
					new ProtocolExchangeItem { Code = "SOG204", Name = "Mayday", Kind = "Sog", Content = ProtocolContentDocument.FromHtml("<p>Call mayday</p>"), RequiresAcknowledgment = true },
					new ProtocolExchangeItem { Code = "POL1", Name = "Uniforms", Kind = "Policy", Content = ProtocolContentDocument.FromHtml("<p>Wear them</p>") },
					new ProtocolExchangeItem { Code = "", Name = "", Kind = "Sog" }
				}
			};

			// The one serializer: what the library exports, the wizard reads back.
			var parsed = ProtocolExchangeDocument.Parse(exported.ToJson());
			parsed.Should().NotBeNull();
			parsed.Protocols.Should().HaveCount(3);
			ProtocolExchangeDocument.Parse("not json").Should().BeNull();

			var preview = await _world.Service.PreviewImportAsync(Manager(), parsed);

			preview.DryRun.Should().BeTrue();
			preview.Created.Should().Be(2);
			preview.Names.Should().BeEquivalentTo("SOG204", "POL1");
			preview.Skipped.Should().ContainSingle("the nameless protocol fails validation exactly as the import would");
			_world.ProtocolsService.Verify(x => x.SaveProtocolAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>()), Times.Never);
			_world.VersionsRepository.Verify(x => x.InsertAsync(It.IsAny<ProtocolVersion>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_world.Events.Verify(x => x.SendMessage(It.Is<Resgrid.Model.Events.AuditEvent>(e => e.Type == AuditLogTypes.ProtocolsImported)), Times.Never);

			var unknown = await _world.Service.PreviewImportAsync(Manager(), new ProtocolExchangeDocument { Format = "something-else" });
			unknown.Error.Should().Be("unknown_format");
		}
	}
}

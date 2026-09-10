using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;
using Resgrid.Services.Records.Evidence;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Evidence artifacts (RMS plan sections 4.5, 5.2; RMS-3c). The plan requires every one of the six sources to
	/// prove authorization, provenance, classification, checksum and retention, and requires that none of them
	/// hydrates a live source. Those are the properties under test — plus immutability, which is what makes an
	/// artifact worth storing instead of a link.
	/// </summary>
	[TestFixture]
	public partial class RecordsEvidenceServiceTests
	{
		private const int Dept = 9;

		private FakeRmsStore _store;
		private FakeIncidentStore _incidents;
		private FakeAdapter _adapter;
		private RecordsEvidenceService _service;
		private RmsOperationalRecord _record;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<Resgrid.Model.Repositories.IRmsExternalReferencesRepository> _references;

		/// <summary>A stand-in source so the service's own rules can be tested without six real subsystems.</summary>
		private sealed class FakeAdapter : IRecordEvidenceAdapter
		{
			public RmsEvidenceKind Kind { get; set; } = RmsEvidenceKind.RunCardActivation;
			public bool Available { get; set; } = true;
			public RecordEvidenceCapture Result { get; set; }
			public int Calls { get; private set; }
			public Action DuringCapture { get; set; }

			public Task<bool> IsAvailableAsync(int departmentId) => Task.FromResult(Available);

			public Task<RecordEvidenceCapture> CaptureAsync(RecordEvidenceCaptureRequest request, CancellationToken cancellationToken = default)
			{
				Calls++;
				DuringCapture?.Invoke();
				return Task.FromResult(Result);
			}
		}

		[SetUp]
		public void SetUp()
		{
			_store = new FakeRmsStore();
			_incidents = new FakeIncidentStore();

			_record = new RmsOperationalRecord
			{
				RmsOperationalRecordId = Guid.NewGuid().ToString(),
				DepartmentId = Dept,
				ProtectionId = Guid.NewGuid().ToString(),
				DefinitionKey = RmsDefinitionKeys.Run,
				DefinitionVersion = 1,
				RecordType = (int)RmsOperationalRecordType.Run,
				State = (int)RmsRecordState.Draft,
				AuthorUserId = "author",
				CallId = 501,
				CreatedOn = DateTime.UtcNow.AddHours(-3),
				ModifiedOn = DateTime.UtcNow.AddHours(-3),
				RowVersion = 1
			};
			_store.Records.Add(_record);

			_adapter = new FakeAdapter
			{
				Result = new RecordEvidenceCapture
				{
					Title = "Run card activation for call 501",
					SourceSubsystem = "RunCards",
					SourceEntityType = "RunCardActivation",
					SourceEntityId = "7",
					IdentifierScheme = "resgrid:runcardactivation",
					SourceItemCount = 1,
					Manifest = new { call_id = 501, activations = new[] { new { activation_id = 7 } } }
				}
			};

			_authorization = new Mock<IRecordsAuthorizationService>();
			_references = new();
			_authorization.Setup(a => a.HasPermissionAsync("author", Dept, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
			_authorization.Setup(a => a.CanUserViewRecordAsync("author", It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.CanReadSourceCallAsync("author", Dept, It.IsAny<Call>())).ReturnsAsync(true);
			_service = new RecordsEvidenceService(_store.EvidenceRepo.Object, _store.RecordsRepo.Object,
				_incidents.ReportsRepo.Object, _store.AuditsRepo.Object, _store.UnitOfWork.Object, new[] { (IRecordEvidenceAdapter)_adapter }, _authorization.Object, Mock.Of<ICallsService>(), _references.Object,
				new PassthroughRecordsProtection(), new DomainEventOutboxService(_store.OutboxRepo.Object, Mock.Of<Resgrid.Model.Providers.IEventAggregator>()), Mock.Of<Resgrid.Model.Repositories.IInventoryStore>());
		}

		private RecordEvidenceCaptureRequest Request(RmsEvidenceKind kind = RmsEvidenceKind.RunCardActivation)
		{
			return new RecordEvidenceCaptureRequest
			{
				DepartmentId = Dept,
				RecordId = _record.RmsOperationalRecordId,
				Kind = kind,
				CallId = 501,
				CaptureReason = "Attached to the run report",
				CapturedByUserId = "author"
			};
		}

		[Test]
		public async Task Capturing_evidence_emits_record_evidence_captured_with_identity_and_checksum_only()
		{
			var artifact = await _service.CaptureAsync(Request());

			var entry = _store.Outbox.Single(o => o.EventName == "RecordEvidenceCaptured");
			entry.TriggerEventType.Should().Be((int)WorkflowTriggerEventType.RecordEvidenceCaptured);
			var payload = Newtonsoft.Json.Linq.JObject.Parse(entry.PayloadJson);
			((string)payload["evidence"]["id"]).Should().Be(artifact.RmsEvidenceArtifactId);
			((string)payload["evidence"]["kind"]).Should().Be("RunCardActivation");
			((string)payload["evidence"]["checksum"]).Should().Be(artifact.Checksum);
			((string)payload["record"]["id"]).Should().Be(_record.RmsOperationalRecordId);
			entry.PayloadJson.Should().NotContain("activations", "the manifest is record content").And.NotContain("Run card activation for call", "so is the title");
			artifact.ManifestJson.Should().Contain("activation_id", "the caller keeps the plaintext artifact");
		}

		[Test]
		public async Task Separate_chat_selections_survive_signing_and_only_an_exact_selection_supersedes_its_draft_predecessor()
		{
			var channels = new Mock<Resgrid.Model.Repositories.IChatChannelRepository>();
			var messages = new Mock<Resgrid.Model.Repositories.IChatMessageRepository>();
			var permission = new Mock<IChatPermissionService>();
			var channel = new ChatChannel { ChatChannelId = "incident-chat", DepartmentId = Dept, CallId = 501 };
			channels.Setup(c => c.GetByCallIdAsync(501)).ReturnsAsync(new[] { channel });
			permission.Setup(p => p.CanAccessChannelAsync(channel, "author", null)).ReturnsAsync(true);
			messages.Setup(m => m.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((string id) => new ChatMessage {
				DepartmentId = Dept, ChatChannelId = channel.ChatChannelId, ChatMessageId = id, Body = "Message " + id, SentOn = DateTime.UtcNow });
			var adapter = new ChatPromotionEvidenceAdapter(messages.Object, channels.Object, new Lazy<IChatPermissionService>(() => permission.Object));
			var service = new RecordsEvidenceService(_store.EvidenceRepo.Object, _store.RecordsRepo.Object, _incidents.ReportsRepo.Object,
				_store.AuditsRepo.Object, _store.UnitOfWork.Object, new[] { adapter }, _authorization.Object, Mock.Of<ICallsService>(), _references.Object,
				new PassthroughRecordsProtection(), new DomainEventOutboxService(_store.OutboxRepo.Object, Mock.Of<Resgrid.Model.Providers.IEventAggregator>()), Mock.Of<Resgrid.Model.Repositories.IInventoryStore>());
			var request = Request(RmsEvidenceKind.ChatPromotion); request.SourceIds = new() { "one", "two" };
			var first = await service.CaptureAsync(request); var original = first.ManifestJson;
			request.SourceIds = new() { "three" }; var second = await service.CaptureAsync(request);
			first.IsCurrent.Should().BeTrue(); second.IsCurrent.Should().BeTrue();
			request.SourceIds = new() { "two", "one" }; var corrected = await service.CaptureAsync(request);
			first.SupersededByArtifactId.Should().Be(corrected.RmsEvidenceArtifactId); first.ManifestJson.Should().Be(original);
			second.IsCurrent.Should().BeTrue();
			await service.BindToRevisionAsync(Dept, _record.RmsOperationalRecordId, "signed-revision");
			var signed = await service.GetForRecordAsync(Dept, _record.RmsOperationalRecordId, "signed-revision");
			signed.Select(a => a.RmsEvidenceArtifactId).Should().BeEquivalentTo(second.RmsEvidenceArtifactId, corrected.RmsEvidenceArtifactId);
			(await service.VerifyAsync(Dept, first.RmsEvidenceArtifactId)).Should().BeTrue();
		}

		[Test]
		public async Task Every_consumption_requires_matching_immutable_evidence_before_finalization()
		{
			var reference = new RmsExternalReference {DepartmentId=Dept,RecordId=_record.RmsOperationalRecordId,RmsExternalReferenceId="consumption",SemanticRole=RmsInventoryUsageAdapter.SemanticRole,SnapshotJson="{\"Quantity\":2}"};
			reference.Checksum=RecordSnapshotSerializer.Checksum(reference.SnapshotJson);
			_references.Setup(r=>r.GetForRecordAsync(Dept,_record.RmsOperationalRecordId)).ReturnsAsync(new[]{reference});
			Func<Task> missing=()=>_service.RequireInventoryCoverageAsync(Dept,_record.RmsOperationalRecordId,Array.Empty<RmsEvidenceArtifact>()); await missing.Should().ThrowAsync<ArgumentException>();
			var manifest=RecordsEvidenceService.Serialize(new {usage=new[]{new {reference_id="consumption",reference_checksum=reference.Checksum}}});
			var artifact=new RmsEvidenceArtifact {DepartmentId=Dept,RecordId=_record.RmsOperationalRecordId,Kind=(int)RmsEvidenceKind.InventoryUsage,ManifestJson=manifest,Checksum=RecordSnapshotSerializer.Checksum(manifest)};
			await _service.RequireInventoryCoverageAsync(Dept,_record.RmsOperationalRecordId,new[]{artifact});
			reference.SnapshotJson="{\"Quantity\":3}";reference.Checksum=RecordSnapshotSerializer.Checksum(reference.SnapshotJson);
			Func<Task> stale=()=>_service.RequireInventoryCoverageAsync(Dept,_record.RmsOperationalRecordId,new[]{artifact});await stale.Should().ThrowAsync<ArgumentException>();
		}
		[TestCase(RmsRecordKind.Operational)]
		[TestCase(RmsRecordKind.IncidentReport)]
		public async Task Witnessed_ledger_reversal_requires_a_linked_Record_correction_and_refreshed_evidence_before_finalization(RmsRecordKind kind)
		{
			var recordId = _record.RmsOperationalRecordId;
			var inventory = new Mock<IInventoryStore>();
			var original = new RecordInventoryUsage { DepartmentId = Dept, SourceType = (int)InventoryUsageSourceType.RmsRecord, SourceId = recordId, RecordKind = (int)kind,
				TransactionId = Guid.NewGuid().ToString("D"), ItemId = Guid.NewGuid().ToString("D"), SourceLocationId = Guid.NewGuid().ToString("D"), Quantity = 2 };
			var originalReference = UsageReference(original);
			var references = new List<RmsExternalReference> { originalReference };
			var reversals = new List<InventoryTransaction>(); var corrections = new List<RecordInventoryUsage>();
			_references.Setup(x => x.GetForRecordAsync(Dept, recordId)).ReturnsAsync(() => references);
			inventory.Setup(x => x.GetAsync<RecordInventoryUsage>(Dept, original.Id)).ReturnsAsync(original);
			inventory.Setup(x => x.RelatedAsync<InventoryTransaction>(Dept, "ReversesTransactionId", original.TransactionId)).ReturnsAsync(() => reversals);
			inventory.Setup(x => x.RelatedAsync<RecordInventoryUsage>(Dept, "ReversesUsageId", original.Id)).ReturnsAsync(() => corrections);
			var service = InventoryCoverageService(inventory.Object);
			var signedArtifact = UsageArtifact(recordId, originalReference); signedArtifact.RevisionId = Guid.NewGuid().ToString("D");
			var frozenManifest = signedArtifact.ManifestJson; var frozenChecksum = signedArtifact.Checksum;
			await service.RequireInventoryCoverageAsync(Dept, recordId, new[] { signedArtifact });

			var reversal = new InventoryTransaction { DepartmentId = Dept, ItemId = original.ItemId, Quantity = original.Quantity, ToLocationId = original.SourceLocationId,
				TransactionType = (int)InventoryTransactionType.Adjust, ReversesTransactionId = original.TransactionId };
			reversals.Add(reversal);
			Func<Task> finalize = () => service.RequireInventoryCoverageAsync(Dept, recordId, new[] { signedArtifact });
			await finalize.Should().ThrowAsync<ArgumentException>().WithMessage("Attach the inventory reversal*");

			var correction = new RecordInventoryUsage { DepartmentId = Dept, SourceType = original.SourceType, SourceId = recordId, RecordKind = (int)kind,
				TransactionId = reversal.Id, ReversesUsageId = original.Id, ItemId = original.ItemId, SourceLocationId = original.SourceLocationId, Quantity = original.Quantity };
			corrections.Add(correction);
			inventory.Setup(x => x.GetAsync<RecordInventoryUsage>(Dept, correction.Id)).ReturnsAsync(correction);
			await finalize.Should().ThrowAsync<ArgumentException>().WithMessage("Attach the inventory reversal*", "a source usage row without a Records reference cannot satisfy the signed evidence contract");

			var correctionReference = UsageReference(correction); references.Add(correctionReference);
			await finalize.Should().ThrowAsync<ArgumentException>().WithMessage("Refresh the inventory evidence*");
			var refreshed = UsageArtifact(recordId, originalReference, correctionReference);
			await service.RequireInventoryCoverageAsync(Dept, recordId, new[] { refreshed });
			signedArtifact.ManifestJson.Should().Be(frozenManifest); signedArtifact.Checksum.Should().Be(frozenChecksum);
			RecordSnapshotSerializer.Checksum(signedArtifact.ManifestJson).Should().Be(frozenChecksum, "the earlier signed artifact remains verifiable after a later correction");
			var totals = JObject.Parse(refreshed.ManifestJson)["usage"].Sum(x => x.Value<decimal>("quantity"));
			totals.Should().Be(0, "the corrected snapshot includes the consumption and one negative reversal, without replacing history");
		}

		[Test]
		public async Task A_correction_from_another_Record_cannot_cover_a_pending_inventory_ledger_reversal()
		{
			var recordId = _record.RmsOperationalRecordId; var inventory = new Mock<IInventoryStore>();
			var original = new RecordInventoryUsage { DepartmentId = Dept, SourceType = (int)InventoryUsageSourceType.RmsRecord, SourceId = recordId, RecordKind = (int)RmsRecordKind.Operational, TransactionId = Guid.NewGuid().ToString("D"), Quantity = 1 };
			var originalReference = UsageReference(original);
			var reversal = new InventoryTransaction { DepartmentId = Dept, ReversesTransactionId = original.TransactionId, Quantity = 1 };
			var foreign = new RecordInventoryUsage { DepartmentId = Dept, SourceType = original.SourceType, SourceId = Guid.NewGuid().ToString("D"), RecordKind = original.RecordKind, ReversesUsageId = original.Id, TransactionId = reversal.Id, Quantity = 1 };
			_references.Setup(x => x.GetForRecordAsync(Dept, recordId)).ReturnsAsync(new[] { originalReference });
			inventory.Setup(x => x.GetAsync<RecordInventoryUsage>(Dept, original.Id)).ReturnsAsync(original);
			inventory.Setup(x => x.RelatedAsync<InventoryTransaction>(Dept, "ReversesTransactionId", original.TransactionId)).ReturnsAsync(new List<InventoryTransaction> { reversal });
			inventory.Setup(x => x.RelatedAsync<RecordInventoryUsage>(Dept, "ReversesUsageId", original.Id)).ReturnsAsync(new List<RecordInventoryUsage> { foreign });
			Func<Task> finalize = () => InventoryCoverageService(inventory.Object).RequireInventoryCoverageAsync(Dept, recordId, new[] { UsageArtifact(recordId, originalReference) });
			await finalize.Should().ThrowAsync<ArgumentException>().WithMessage("Attach the inventory reversal*");
		}

		[Test]
		public async Task Modern_inventory_evidence_fails_closed_when_source_usage_is_missing()
		{
			var usage = new RecordInventoryUsage { DepartmentId = Dept, SourceId = _record.RmsOperationalRecordId, RecordKind = (int)RmsRecordKind.Operational, TransactionId = Guid.NewGuid().ToString("D"), Quantity = 1 };
			var reference = UsageReference(usage);
			_references.Setup(x => x.GetForRecordAsync(Dept, _record.RmsOperationalRecordId)).ReturnsAsync(new[] { reference });
			Func<Task> finalize = () => _service.RequireInventoryCoverageAsync(Dept, _record.RmsOperationalRecordId, new[] { UsageArtifact(_record.RmsOperationalRecordId, reference) });
			await finalize.Should().ThrowAsync<InvalidOperationException>().WithMessage("Inventory usage provenance is unavailable.");
		}

		[Test]
		public void Evidence_service_requires_inventory_storage_at_construction()
		{
			FluentActions.Invoking(() => InventoryCoverageService(null)).Should().Throw<ArgumentNullException>().WithParameterName("inventoryStore");
		}

		private RecordsEvidenceService InventoryCoverageService(IInventoryStore inventory) => new(_store.EvidenceRepo.Object, _store.RecordsRepo.Object,
			_incidents.ReportsRepo.Object, _store.AuditsRepo.Object, _store.UnitOfWork.Object, new[] { (IRecordEvidenceAdapter)_adapter }, _authorization.Object, Mock.Of<ICallsService>(), _references.Object,
			new PassthroughRecordsProtection(), new DomainEventOutboxService(_store.OutboxRepo.Object, Mock.Of<Resgrid.Model.Providers.IEventAggregator>()), inventory);
		private static RmsExternalReference UsageReference(RecordInventoryUsage usage)
		{
			var snapshot = RecordsEvidenceService.Serialize(new { SchemaVersion = 3, UsageId = usage.Id, usage.TransactionId, usage.ItemId, usage.Quantity, usage.ReversesUsageId });
			return new RmsExternalReference { RmsExternalReferenceId = usage.Id, DepartmentId = usage.DepartmentId, RecordId = usage.SourceId, RecordKind = usage.RecordKind.Value,
				SourceSubsystem = RmsInventoryUsageAdapter.SourceSubsystem, SourceEntityType = "RecordInventoryUsage", SourceEntityId = usage.Id, SemanticRole = RmsInventoryUsageAdapter.SemanticRole,
				SnapshotJson = snapshot, Checksum = RecordSnapshotSerializer.Checksum(snapshot) };
		}
		private static RmsEvidenceArtifact UsageArtifact(string recordId, params RmsExternalReference[] references)
		{
			var manifest = RecordsEvidenceService.Serialize(new { usage = references.Select(reference => {
				var snapshot = JObject.Parse(reference.SnapshotJson); return new { reference_id = reference.RmsExternalReferenceId, reference_checksum = reference.Checksum,
					quantity = snapshot.Value<string>("ReversesUsageId") == null ? snapshot.Value<decimal>("Quantity") : -snapshot.Value<decimal>("Quantity") }; }).ToArray() });
			return new RmsEvidenceArtifact { DepartmentId = Dept, RecordId = recordId, Kind = (int)RmsEvidenceKind.InventoryUsage, ManifestJson = manifest, Checksum = RecordSnapshotSerializer.Checksum(manifest) };
		}
		[Test]
		public async Task Forged_call_and_non_author_capture_are_denied_before_reading_the_source()
		{
			var request = Request(); request.CallId = 999;
			Func<Task> forged = () => _service.CaptureAsync(request); await forged.Should().ThrowAsync<UnauthorizedAccessException>();
			request = Request(); request.CapturedByUserId = "viewer";
			_authorization.Setup(a => a.CanUserViewRecordAsync("viewer", It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.HasPermissionAsync("viewer", Dept, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
			Func<Task> viewer = () => _service.CaptureAsync(request); await viewer.Should().ThrowAsync<UnauthorizedAccessException>();
			_adapter.Calls.Should().Be(0); _store.EvidenceArtifacts.Should().BeEmpty();
		}
		[Test]
		public async Task Evidence_cannot_attach_to_a_finalized_record_without_an_amendment()
		{
			_record.State = (int)RmsRecordState.Finalized;
			Func<Task> capture = () => _service.CaptureAsync(Request()); await capture.Should().ThrowAsync<InvalidOperationException>();
			_adapter.Calls.Should().Be(0);
		}
		[Test]
		public async Task Parent_edit_during_source_capture_invalidates_the_capture_without_storing_an_artifact()
		{
			_adapter.DuringCapture = () => _record.RowVersion++;
			Func<Task> capture = () => _service.CaptureAsync(Request()); await capture.Should().ThrowAsync<RecordConcurrencyException>();
			_store.EvidenceArtifacts.Should().BeEmpty();
		}
		[Test]
		public async Task Restricted_boolean_cannot_override_live_permission_or_revocation_during_capture()
		{
			_adapter.Result.Classification = RmsEvidenceClassification.Restricted;
			_adapter.DuringCapture = () => _authorization.Setup(a => a.HasPermissionAsync("author", Dept, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			Func<Task> capture = () => _service.CaptureAsync(Request(), true); await capture.Should().ThrowAsync<UnauthorizedAccessException>();
			_store.EvidenceArtifacts.Should().BeEmpty();
		}
		[Test]
		public async Task A_capture_records_provenance_and_a_checksum_over_its_manifest()
		{
			var artifact = await _service.CaptureAsync(Request());

			artifact.SourceSubsystem.Should().Be("RunCards");
			artifact.SourceEntityId.Should().Be("7");
			artifact.IdentifierScheme.Should().Be("resgrid:runcardactivation");
			artifact.CaptureReason.Should().Be("Attached to the run report");
			artifact.CapturedByUserId.Should().Be("author");
			artifact.Checksum.Should().NotBeNullOrWhiteSpace();
			artifact.ByteSize.Should().BeGreaterThan(0);

			(await _service.VerifyAsync(Dept, artifact.RmsEvidenceArtifactId)).Should().BeTrue();
		}

		[Test]
		public async Task Serialization_is_deterministic_so_a_checksum_still_verifies_later()
		{
			var first = RecordsEvidenceService.Serialize(new { b = 2, a = 1, when = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc) });
			var second = RecordsEvidenceService.Serialize(new { b = 2, a = 1, when = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc) });

			first.Should().Be(second, "an auditor re-computing the checksum years later must get the same bytes");
		}

		[Test]
		public async Task A_tampered_manifest_fails_verification()
		{
			var artifact = await _service.CaptureAsync(Request());

			// Somebody edited the stored manifest directly.
			_store.EvidenceArtifacts.Single().ManifestJson = "{\"call_id\":999}";

			(await _service.VerifyAsync(Dept, artifact.RmsEvidenceArtifactId)).Should().BeFalse();
		}

		[Test]
		public async Task A_capture_without_a_reason_is_refused()
		{
			var request = Request();
			request.CaptureReason = "  ";

			Func<Task> act = () => _service.CaptureAsync(request);

			await act.Should().ThrowAsync<ArgumentException>("evidence never enters an official record anonymously");
		}

		[Test]
		public async Task Restricted_evidence_needs_the_restricted_grant()
		{
			_adapter.Result.Classification = RmsEvidenceClassification.Restricted;

			Func<Task> act = () => _service.CaptureAsync(Request(), canCaptureRestricted: false);

			await act.Should().ThrowAsync<UnauthorizedAccessException>("a missing grant is a refusal, not a bad request");
			_store.EvidenceArtifacts.Should().BeEmpty();

			var artifact = await _service.CaptureAsync(Request(), canCaptureRestricted: true);
			artifact.Classification.Should().Be((int)RmsEvidenceClassification.Restricted);
		}

		[Test]
		public async Task A_recapture_supersedes_the_earlier_artifact_and_leaves_it_readable()
		{
			var first = await _service.CaptureAsync(Request());
			var second = await _service.CaptureAsync(Request());

			_store.EvidenceArtifacts.Should().HaveCount(2, "a correction is a new artifact, never an edit");

			var superseded = _store.EvidenceArtifacts.Single(a => a.RmsEvidenceArtifactId == first.RmsEvidenceArtifactId);
			superseded.SupersededByArtifactId.Should().Be(second.RmsEvidenceArtifactId);
			superseded.SupersededOn.Should().NotBeNull();
			superseded.IsCurrent.Should().BeFalse();
			superseded.ManifestJson.Should().NotBeNull("what an earlier revision attested to stays readable");

			(await _service.GetForRecordAsync(Dept, _record.RmsOperationalRecordId)).Should().ContainSingle()
				.Which.RmsEvidenceArtifactId.Should().Be(second.RmsEvidenceArtifactId);
			(await _service.GetForRecordAsync(Dept, _record.RmsOperationalRecordId, includeSuperseded: true)).Should().HaveCount(2);
		}

		[Test]
		public async Task Evidence_cannot_be_captured_against_a_voided_record()
		{
			_record.State = (int)RmsRecordState.Voided;

			Func<Task> act = () => _service.CaptureAsync(Request());

			await act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task An_unavailable_source_says_so_rather_than_capturing_nothing_silently()
		{
			_adapter.Available = false;

			Func<Task> act = () => _service.CaptureAsync(Request());

			await act.Should().ThrowAsync<InvalidOperationException>();
			_adapter.Calls.Should().Be(0, "an unavailable source is not asked");
		}

		[Test]
		public async Task Binding_stamps_draft_artifacts_with_the_revision_and_leaves_bound_ones_alone()
		{
			var draft = await _service.CaptureAsync(Request());
			draft.RevisionId.Should().BeNull();

			await _service.BindToRevisionAsync(Dept, _record.RmsOperationalRecordId, "rev-1");
			_store.EvidenceArtifacts.Single().RevisionId.Should().Be("rev-1");

			// A later capture belongs to the draft again, and binding to revision 2 must not move revision 1's.
			_adapter.Result.SourceEntityId = "8";
			await _service.CaptureAsync(Request());
			await _service.BindToRevisionAsync(Dept, _record.RmsOperationalRecordId, "rev-2");

			_store.EvidenceArtifacts.Should().Contain(a => a.RevisionId == "rev-1");
			_store.EvidenceArtifacts.Should().Contain(a => a.RevisionId == "rev-2");
		}

		[Test]
		public async Task Every_one_of_the_six_sources_reports_its_state()
		{
			var states = await _service.GetSourceStatesAsync(Dept);

			states.Select(s => s.Kind).Should().BeEquivalentTo(Enum.GetValues(typeof(RmsEvidenceKind)).Cast<RmsEvidenceKind>(),
				"the plan ships all six; a source with no adapter still reports, so an empty list is never mistaken for no evidence");
			states.Single(s => s.Kind == RmsEvidenceKind.RunCardActivation).Available.Should().BeTrue();
			states.Single(s => s.Kind == RmsEvidenceKind.ReadinessPacket).Reason.Should().NotBeNullOrWhiteSpace();
		}

		[Test]
		public async Task The_capture_is_audited_with_its_checksum_and_reason()
		{
			await _service.CaptureAsync(Request());

			var audit = _store.Audits.Should().ContainSingle().Subject;
			audit.RecordId.Should().Be(_record.RmsOperationalRecordId);
			audit.Purpose.Should().Contain("Evidence captured");
			JObject.Parse(audit.DetailJson).Value<string>("Checksum").Should().NotBeNullOrWhiteSpace();
		}

		[Test]
		public async Task The_readiness_adapter_ships_and_explains_why_it_has_nothing()
		{
			// The checklists module is planned, not built. "Unavailable" and "there was no readiness evidence" are
			// different answers, and the adapter has to give the first one rather than an empty second.
			var adapter = new ReadinessPacketEvidenceAdapter();

			(await adapter.IsAvailableAsync(Dept)).Should().BeFalse();
			var capture = await adapter.CaptureAsync(Request(RmsEvidenceKind.ReadinessPacket));
			capture.Available.Should().BeFalse();
			capture.UnavailableReason.Should().Be(Resgrid.Services.ChecklistReportDocuments.Text("Checklists are disabled for this department."));
		}
	}
}

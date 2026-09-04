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
	public class RecordsEvidenceServiceTests
	{
		private const int Dept = 9;

		private FakeRmsStore _store;
		private FakeIncidentStore _incidents;
		private FakeAdapter _adapter;
		private RecordsEvidenceService _service;
		private RmsOperationalRecord _record;

		/// <summary>A stand-in source so the service's own rules can be tested without six real subsystems.</summary>
		private sealed class FakeAdapter : IRecordEvidenceAdapter
		{
			public RmsEvidenceKind Kind { get; set; } = RmsEvidenceKind.RunCardActivation;
			public bool Available { get; set; } = true;
			public RecordEvidenceCapture Result { get; set; }
			public int Calls { get; private set; }

			public Task<bool> IsAvailableAsync(int departmentId) => Task.FromResult(Available);

			public Task<RecordEvidenceCapture> CaptureAsync(RecordEvidenceCaptureRequest request, CancellationToken cancellationToken = default)
			{
				Calls++;
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

			_service = new RecordsEvidenceService(_store.EvidenceRepo.Object, _store.RecordsRepo.Object,
				_incidents.ReportsRepo.Object, _store.AuditsRepo.Object, _store.UnitOfWork.Object, new[] { (IRecordEvidenceAdapter)_adapter });
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

			await act.Should().ThrowAsync<InvalidOperationException>();
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
			capture.UnavailableReason.Should().Be(ReadinessPacketEvidenceAdapter.UnavailableReason);
		}
	}
}

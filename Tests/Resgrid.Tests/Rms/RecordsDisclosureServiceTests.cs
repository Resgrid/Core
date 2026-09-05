using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Public-records workflow (RMS plan section 4.7, RMS-3d). The three properties the plan actually cares about
	/// are the ones under test: a production never mutates a source revision, the produced set is frozen so a
	/// later amendment cannot change what was released, and redaction is logged rather than silent.
	/// </summary>
	[TestFixture]
	public class RecordsDisclosureServiceTests
	{
		private const int Dept = 21;

		private FakeRmsStore _store;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<IDepartmentSettingsService> _settings;
		private RecordsDisclosureService _service;
		private RmsOperationalRecord _finalized;
		private RmsRevision _revision;

		[SetUp]
		public void SetUp()
		{
			_store = new FakeRmsStore();

			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync(It.IsAny<string>(), Dept)).ReturnsAsync((List<int>)null);
			_authorization.Setup(a => a.CanUserViewRecordAsync(It.IsAny<string>(), It.IsAny<string>(), Dept)).ReturnsAsync(true);

			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetRecordsDisclosureConfigAsync(Dept, It.IsAny<bool>()))
				.ReturnsAsync(new RecordsDisclosureConfig { StatutoryClockDays = 5, DefaultRedactionProfile = RmsRedactionProfiles.Standard });

			_finalized = SeedRecord(RmsRecordState.Finalized);
			_revision = SeedRevision(_finalized);

			_service = new RecordsDisclosureService(_store.DisclosureRequestsRepo.Object, _store.DisclosureProductionsRepo.Object,
				_store.RecordsRepo.Object, _store.RevisionsRepo.Object, _store.AuditsRepo.Object,
				_authorization.Object, _settings.Object, _store.UnitOfWork.Object);
		}

		private RmsOperationalRecord SeedRecord(RmsRecordState state, string summary = "Structure fire response")
		{
			var record = new RmsOperationalRecord
			{
				RmsOperationalRecordId = Guid.NewGuid().ToString(),
				DepartmentId = Dept,
				ProtectionId = Guid.NewGuid().ToString(),
				DefinitionKey = RmsDefinitionKeys.Run,
				DefinitionVersion = 1,
				RecordType = (int)RmsOperationalRecordType.Run,
				State = (int)state,
				RecordNumber = "RUN-2026-0007",
				DisplaySummary = summary,
				AuthorUserId = "author",
				StartedOn = DateTime.UtcNow.AddDays(-30),
				FinalizedOn = DateTime.UtcNow.AddDays(-29),
				CreatedOn = DateTime.UtcNow.AddDays(-30),
				ModifiedOn = DateTime.UtcNow.AddDays(-29),
				RowVersion = 2
			};
			_store.Records.Add(record);
			return record;
		}

		private RmsRevision SeedRevision(RmsOperationalRecord record)
		{
			var snapshot = new RecordSnapshot
			{
				RecordId = record.RmsOperationalRecordId,
				DepartmentId = Dept,
				DefinitionKey = record.DefinitionKey,
				RecordNumber = record.RecordNumber,
				Details = new RmsOperationalRecordDetail
				{
					RecordId = record.RmsOperationalRecordId,
					Narrative = "Crew made entry and knocked the fire down.",
					ContactName = "Jane Public",
					// A restricted-class field: the standard profile must withhold it and say so.
					CaseNumber = "CASE-2026-014",
					Location = "100 Main St"
				},
				Participants = new List<RmsRecordParticipant>
				{
					new RmsRecordParticipant { UserId = "member-1", DisplayNameSnapshot = "A. Firefighter", Role = "Attended", GroupNameSnapshot = "Station 1" }
				}
			};

			var json = RecordSnapshotSerializer.Serialize(snapshot);
			var revision = new RmsRevision
			{
				RmsRevisionId = Guid.NewGuid().ToString(),
				DepartmentId = Dept,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = record.RmsOperationalRecordId,
				RecordKind = (int)RmsRecordKind.Operational,
				RevisionNumber = 1,
				Transition = (int)RmsRevisionTransition.Finalized,
				DefinitionKey = record.DefinitionKey,
				DefinitionVersion = 1,
				SnapshotJson = json,
				Checksum = RecordSnapshotSerializer.Checksum(json),
				ActorUserId = "author",
				CreatedOn = DateTime.UtcNow.AddDays(-29)
			};
			_store.Revisions.Add(revision);
			record.CurrentRevisionId = revision.RmsRevisionId;
			return revision;
		}

		private async Task<RmsDisclosureRequest> OpenRequestAsync(string profile = RmsRedactionProfiles.Standard)
		{
			var request = await _service.CreateRequestAsync(Dept, "clerk", new RmsDisclosureRequest
			{
				RequesterName = "A. Reporter",
				RequesterOrganization = "Local Paper",
				JurisdictionProfile = "US-IL",
				ReceivedOn = DateTime.UtcNow.AddDays(-1)
			});

			return await _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "All run reports from last month",
				new RmsRecordQuery { States = new List<int> { (int)RmsRecordState.Finalized }, DefinitionKey = RmsDefinitionKeys.Run }, profile);
		}

		[Test]
		public async Task A_new_request_gets_a_number_and_a_statutory_clock()
		{
			var request = await _service.CreateRequestAsync(Dept, "clerk", new RmsDisclosureRequest
			{
				RequesterName = "A. Reporter",
				ReceivedOn = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)
			});

			request.RequestNumber.Should().Be("PRR-2026-0001");
			request.State.Should().Be((int)RmsDisclosureState.Received);
			request.StatutoryDueOn.Should().Be(new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc),
				"the clock runs from when the department received it, not from when it was logged");
			request.RedactionProfile.Should().Be(RmsRedactionProfiles.Standard);
		}

		[Test]
		public async Task A_request_without_a_requester_is_refused()
		{
			Func<Task> act = () => _service.CreateRequestAsync(Dept, "clerk", new RmsDisclosureRequest());

			await act.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task The_scope_preview_runs_through_the_same_authorization_path_as_the_queue()
		{
			var request = await OpenRequestAsync();
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync("clerk", Dept)).ReturnsAsync(new List<int> { 5 });
			_authorization.Setup(a => a.CanUserViewRecordAsync("clerk", It.IsAny<string>(), Dept)).ReturnsAsync(false);

			var preview = await _service.PreviewScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			preview.MatchedCount.Should().Be(1);
			preview.WithheldWholeRecordCount.Should().Be(1, "a disclosure officer sees no more of the department than their queue shows them");
			preview.Items.Should().BeEmpty();
		}

		[Test]
		public async Task A_draft_is_listed_but_never_producible()
		{
			SeedRecord(RmsRecordState.Draft, "Half-written report");
			var request = await _service.CreateRequestAsync(Dept, "clerk", new RmsDisclosureRequest { RequesterName = "A. Reporter" });
			await _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "Everything",
				new RmsRecordQuery { DefinitionKey = RmsDefinitionKeys.Run }, RmsRedactionProfiles.Standard);

			var preview = await _service.PreviewScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			preview.Items.Should().HaveCount(2);
			var draft = preview.Items.Single(i => i.Summary == "Half-written report");
			draft.Producible.Should().BeFalse();
			draft.NotProducibleReason.Should().Contain("not finalized");
		}

		[Test]
		public async Task A_production_redacts_restricted_fields_and_logs_what_it_withheld()
		{
			var request = await OpenRequestAsync();

			var production = await _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			production.RecordCount.Should().Be(1);
			production.WithheldFieldCount.Should().BeGreaterThan(0);

			var artifact = JObject.Parse(production.ArtifactJson);
			var details = artifact["documents"][0]["details"];
			details["Narrative"].Value<string>().Should().Contain("knocked the fire down");
			details["ContactName"].Value<string>().Should().Be("Jane Public", "an unrestricted field is released");
			details["CaseNumber"].Should().BeNull("a restricted-class field is not released under the standard profile");

			var withheld = JArray.Parse(production.WithheldFieldsJson);
			withheld.Should().Contain(w => w["Field"].Value<string>() == "CaseNumber",
				"a requester is entitled to know something was withheld even when they cannot have it");
		}

		[Test]
		public async Task The_no_identifiers_profile_withholds_participant_identity()
		{
			var request = await OpenRequestAsync(RmsRedactionProfiles.NoPersonalIdentifiers);

			var production = await _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			var artifact = JObject.Parse(production.ArtifactJson);
			((JArray)artifact["documents"][0]["participants"]).Should().BeEmpty();
			JArray.Parse(production.WithheldFieldsJson).Should().Contain(w => w["Section"].Value<string>() == "Participants");
		}

		[Test]
		public async Task A_production_never_mutates_the_source_revision()
		{
			var request = await OpenRequestAsync();
			var before = _revision.SnapshotJson;
			var beforeChecksum = _revision.Checksum;
			var beforeRowVersion = _finalized.RowVersion;

			await _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			_revision.SnapshotJson.Should().Be(before);
			_revision.Checksum.Should().Be(beforeChecksum);
			_finalized.RowVersion.Should().Be(beforeRowVersion, "answering a request must never damage the record it answers from");
		}

		[Test]
		public async Task The_produced_set_freezes_the_revision_and_its_checksum()
		{
			var request = await OpenRequestAsync();
			var production = await _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			var produced = JArray.Parse(production.ProducedSetJson);
			produced.Should().ContainSingle();
			produced[0]["revision_id"].Value<string>().Should().Be(_revision.RmsRevisionId);
			produced[0]["revision_checksum"].Value<string>().Should().Be(_revision.Checksum);

			// The record is amended after release. What was produced must still describe revision 1.
			var amended = SeedRevision(_finalized);
			amended.RevisionNumber = 2;

			var reread = _store.DisclosureProductions.Single();
			JArray.Parse(reread.ProducedSetJson)[0]["revision_id"].Value<string>().Should().Be(_revision.RmsRevisionId,
				"a later amendment cannot silently change what the department released");
		}

		[Test]
		public async Task A_production_is_checksummed_and_verifiable()
		{
			var request = await OpenRequestAsync();
			var production = await _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			(await _service.VerifyProductionAsync(Dept, production.RmsDisclosureProductionId)).Should().BeTrue();

			_store.DisclosureProductions.Single().ArtifactJson = "{\"documents\":[]}";
			(await _service.VerifyProductionAsync(Dept, production.RmsDisclosureProductionId)).Should().BeFalse();
		}

		[Test]
		public async Task Releasing_closes_the_statutory_clock_and_audits_the_handover()
		{
			var request = await OpenRequestAsync();
			var production = await _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			var released = await _service.ReleaseAsync(Dept, "chief", production.RmsDisclosureProductionId);

			released.ReleasedOn.Should().NotBeNull();
			released.ReleasedByUserId.Should().Be("chief");
			_store.DisclosureRequests.Single().State.Should().Be((int)RmsDisclosureState.Released);
			_store.DisclosureRequests.Single().ClosedOn.Should().NotBeNull();
			_store.Audits.Should().Contain(a => a.Purpose == "Disclosure released");
		}

		[Test]
		public async Task Releasing_twice_is_refused()
		{
			var request = await OpenRequestAsync();
			var production = await _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);
			await _service.ReleaseAsync(Dept, "chief", production.RmsDisclosureProductionId);

			Func<Task> act = () => _service.ReleaseAsync(Dept, "chief", production.RmsDisclosureProductionId);

			await act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task The_scope_cannot_change_once_something_has_been_produced()
		{
			var request = await OpenRequestAsync();
			await _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			Func<Task> act = () => _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "Actually, everything",
				new RmsRecordQuery(), RmsRedactionProfiles.FullDisclosure);

			await act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task Closing_without_a_reason_is_refused()
		{
			var request = await OpenRequestAsync();

			Func<Task> act = () => _service.CloseAsync(Dept, "chief", request.RmsDisclosureRequestId, RmsDisclosureState.Denied, "   ");

			await act.Should().ThrowAsync<ArgumentException>("a refusal without a recorded basis is not defensible");
		}

		[Test]
		public async Task Denying_records_the_exemption_relied_on()
		{
			var request = await OpenRequestAsync();

			var denied = await _service.CloseAsync(Dept, "chief", request.RmsDisclosureRequestId, RmsDisclosureState.Denied, "Active investigation exemption");

			denied.State.Should().Be((int)RmsDisclosureState.Denied);
			denied.DispositionReason.Should().Be("Active investigation exemption");
			denied.ClosedOn.Should().NotBeNull();
		}

		[Test]
		public async Task Every_produced_record_is_audited_against_the_record_itself()
		{
			var request = await OpenRequestAsync();
			await _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			// "What did we hand out about this record" has to be answerable from the record, not only the request.
			_store.Audits.Should().Contain(a => a.RecordId == _finalized.RmsOperationalRecordId && a.Purpose.StartsWith("Disclosure production"));
		}

		[Test]
		public async Task A_scope_that_resolves_to_nothing_producible_is_refused()
		{
			_finalized.State = (int)RmsRecordState.Draft;
			var request = await OpenRequestAsync();

			Func<Task> act = () => _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			await act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task A_client_supplied_scope_cannot_widen_the_viewer()
		{
			var request = await _service.CreateRequestAsync(Dept, "clerk", new RmsDisclosureRequest { RequesterName = "A. Reporter" });

			// A caller tries to scope the request to somebody else's groups.
			await _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "Everything",
				new RmsRecordQuery { VisibleGroupIds = new List<int> { 99 }, ViewerUserId = "someone-else" }, RmsRedactionProfiles.Standard);

			var stored = JsonConvert.DeserializeObject<RmsRecordQuery>(_store.DisclosureRequests.Single().ScopeQueryJson);
			stored.VisibleGroupIds.Should().BeNull("the viewer fields come from the caller's own authorization, never the request body");
			stored.ViewerUserId.Should().BeNull();
		}
	}
}

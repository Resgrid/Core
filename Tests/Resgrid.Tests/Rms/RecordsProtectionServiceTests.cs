using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// The RMS protection seam (RMS plan section 5.9) against the real ProtectedReadService with a fake broker:
	/// an enrolled department's cataloged columns leave the seam as envelopes and come back as plaintext for a
	/// grant holder or as the REDACTED sentinel without one; a refused write throws with the machine-readable
	/// reason; the NERIS workload lane opens only behind the profile acknowledgement.
	/// </summary>
	[TestFixture]
	public class RecordsProtectionServiceTests
	{
		private const int Dept = 31;
		private Mock<IDepartmentDataProtectionService> _adp;
		private FakeGrantService _grants;
		private Mock<IProtectedDataBrokerClient> _broker;
		private Mock<INerisProfileService> _neris;
		private FixedProtectedGrantContext _context;
		private bool _enrolled;
		private bool _ackEgress;
		private int _workloadCalls;

		[SetUp]
		public void SetUp()
		{
			_enrolled = true;
			_ackEgress = false;
			_workloadCalls = 0;
			_adp = new Mock<IDepartmentDataProtectionService>();
			_adp.Setup(a => a.ShouldEncryptNewWritesAsync(Dept)).ReturnsAsync(() => _enrolled);
			_adp.Setup(a => a.IsProtectionEnforcedAsync(Dept)).ReturnsAsync(() => _enrolled);
			_adp.Setup(a => a.GetPinnedCatalogVersionAsync(Dept)).ReturnsAsync(() => _enrolled ? ProtectedFieldCatalog.RecordsCatalogVersion : 0);
			_adp.Setup(a => a.GetPolicyByDepartmentIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(() => new DepartmentDataProtectionPolicy { DepartmentId = Dept, CatalogVersion = ProtectedFieldCatalog.RecordsCatalogVersion, PolicyEpoch = 3, State = (int)DepartmentDataProtectionState.Enabled });

			_grants = new FakeGrantService();

			_broker = new Mock<IProtectedDataBrokerClient>();
			_broker.Setup(b => b.EncryptAsync(Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string g, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken c) => new ProtectedDataBrokerResult
				{
					Success = true,
					Items = items.Select(i => new ProtectedFieldOperationResult { FieldId = i.FieldId, RowKey = i.RowKey, Value = i.IsBinary ? Convert.ToBase64String(Seal(Convert.FromBase64String(i.Value))) : "rgdp:" + Reverse(i.Value) }).ToList()
				});
			_broker.Setup(b => b.DecryptAsync(Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string g, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken c) => new ProtectedDataBrokerResult
				{
					Success = true,
					Items = items.Select(i => new ProtectedFieldOperationResult { FieldId = i.FieldId, RowKey = i.RowKey, Value = i.IsBinary ? Convert.ToBase64String(Open(Convert.FromBase64String(i.Value))) : Reverse(i.Value.Substring(5)) }).ToList()
				});
			_broker.Setup(b => b.DecryptForWorkloadAsync(Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string purpose, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken c) =>
				{
					_workloadCalls++;
					return new ProtectedDataBrokerResult { Success = true, Items = items.Select(i => new ProtectedFieldOperationResult { FieldId = i.FieldId, RowKey = i.RowKey, Value = Reverse(i.Value.Substring(5)) }).ToList() };
				});

			_neris = new Mock<INerisProfileService>();
			_neris.Setup(n => n.GetProfileAsync(Dept)).ReturnsAsync(() => new RmsNerisProfile { DepartmentId = Dept, AllowProtectedContentEgress = _ackEgress });
			_context = new FixedProtectedGrantContext("good-grant", false, "officer");
		}

		private static string Reverse(string value) => new string(value.Reverse().ToArray());
		private static byte[] Seal(byte[] value) => System.Text.Encoding.ASCII.GetBytes("rgdpb:").Concat(value.Reverse()).ToArray();
		private static byte[] Open(byte[] value) => value.Skip(6).Reverse().ToArray();

		private RecordsProtectionService Build(IProtectedGrantContext context = null)
		{
			var reads = new ProtectedReadService(_adp.Object, _grants, _broker.Object, new ProtectedFieldCatalog());
			return new RecordsProtectionService(reads, reads, context ?? _context, _adp.Object, _neris.Object);
		}

		[Test]
		public async Task Narrative_is_sealed_on_write_and_revealed_for_a_grant_holder()
		{
			var service = Build();
			var narrative = new RmsNarrative { RmsNarrativeId = "n1", DepartmentId = Dept, Narrative = "Engine 5 found smoke showing.", OutcomeNarrative = "Fire out." };

			await service.ProtectNarrativeAsync(Dept, narrative, null, "officer");

			narrative.IsProtected.Should().BeTrue();
			narrative.ProtectedCatalogVersion.Should().Be(ProtectedFieldCatalog.RecordsCatalogVersion);
			narrative.Narrative.Should().StartWith("rgdp:");
			narrative.OutcomeNarrative.Should().StartWith("rgdp:");
			narrative.ImpedimentNarrative.Should().BeNull("an empty column is never sealed");

			var aggregate = new IncidentReportAggregate { Narrative = narrative };
			var result = await service.RevealAsync(Dept, aggregate);
			result.IsProtected.Should().BeTrue();
			result.RedactedFields.Should().BeEmpty();
			narrative.Narrative.Should().Be("Engine 5 found smoke showing.");
			aggregate.Protection.Should().BeSameAs(result);
		}

		[Test]
		public async Task Without_a_grant_the_reveal_leaves_the_sentinel_and_names_the_reason()
		{
			var writer = Build();
			var details = new RmsOperationalRecordDetail { RmsOperationalRecordDetailId = "d1", DepartmentId = Dept, RecordId = "r1", Narrative = "Private narrative", Type = "Drill" };
			await writer.ProtectDetailsAsync(Dept, details, null, "officer");
			details.Type.Should().Be("Drill", "Type is not a cataloged column");

			var reader = Build(FixedProtectedGrantContext.Workload);
			var aggregate = new RecordAggregate { Details = details };
			var result = await reader.RevealAsync(Dept, aggregate);

			result.IsProtected.Should().BeTrue();
			result.ProtectedReason.Should().Be("step_up_required");
			result.RedactedFields.Should().Contain("rmsoperationalrecorddetails.narrative");
			details.Narrative.Should().Be(ProtectedDataEnvelope.RedactionValue);
			FluentActions.Invoking(() => result.RequireRevealed("finalize")).Should().Throw<RecordProtectedContentException>().Which.Reason.Should().Be("step_up_required");
		}

		[Test]
		public async Task A_stale_grant_refuses_the_write_with_the_reason()
		{
			var service = Build(new FixedProtectedGrantContext("stale-grant", false, "officer"));
			var hold = new RmsRecordLegalHold { RmsRecordLegalHoldId = "h1", DepartmentId = Dept, Notes = "Preserve everything" };

			Func<Task> write = () => service.ProtectLegalHoldAsync(Dept, hold, null, "officer");

			(await write.Should().ThrowAsync<RecordProtectedContentException>()).Which.Reason.Should().Be("grant_expired");
			hold.Notes.Should().Be("Preserve everything", "a refused write never half-applies");
		}

		[Test]
		public async Task Unenrolled_departments_pass_through_untouched()
		{
			_enrolled = false;
			var service = Build(FixedProtectedGrantContext.Workload);
			var location = new RmsLocation { RmsLocationId = "l1", DepartmentId = Dept, AddressText = "1 Main St", Latitude = 39.5m, Longitude = -104.9m };

			await service.ProtectLocationAsync(Dept, location, null, null);
			var result = await service.RevealAsync(Dept, new IncidentReportAggregate { Location = location });

			location.AddressText.Should().Be("1 Main St");
			location.Latitude.Should().Be(39.5m);
			location.ProtectedLatitudeEnvelope.Should().BeNull();
			result.IsProtected.Should().BeFalse();
		}

		[Test]
		public async Task Coordinates_ride_companion_envelopes_and_come_back_as_numbers()
		{
			var service = Build();
			var exposure = new RmsExposure { RmsExposureId = "e1", DepartmentId = Dept, AddressText = "2 Side St", Latitude = 39.25m, Longitude = -104.75m };

			await service.ProtectExposureAsync(Dept, exposure, null, "officer");
			exposure.Latitude.Should().BeNull();
			exposure.ProtectedLatitudeEnvelope.Should().StartWith("rgdp:");
			exposure.IsProtected.Should().BeTrue();

			await service.RevealAsync(Dept, new IncidentReportAggregate { Exposures = new List<RmsExposure> { exposure } });
			exposure.Latitude.Should().Be(39.25m);
			exposure.Longitude.Should().Be(-104.75m);
			exposure.AddressText.Should().Be("2 Side St");
		}

		[Test]
		public async Task Attachment_bytes_are_sealed_and_only_opened_when_the_download_asks_for_them()
		{
			var service = Build();
			var bytes = System.Text.Encoding.UTF8.GetBytes("scene photo");
			var attachment = new RmsRecordAttachment { RmsRecordAttachmentId = "a1", DepartmentId = Dept, FileName = "scene.jpg", Data = bytes };

			await service.ProtectAttachmentAsync(Dept, attachment, null, "officer");
			attachment.FileName.Should().StartWith("rgdp:");
			ProtectedReadService.IsBinaryEnveloped(attachment.Data).Should().BeTrue();

			var metadata = await service.RevealAttachmentsAsync(Dept, new[] { attachment }, false);
			attachment.FileName.Should().Be("scene.jpg");
			attachment.Data.Should().BeNull("a metadata read strips the ciphertext rather than carrying it out");
			metadata.RedactedFields.Should().Contain("rmsrecordattachments.data");

			attachment.Data = Seal(bytes);
			var full = await service.RevealAttachmentsAsync(Dept, new[] { attachment }, true);
			full.RedactedFields.Should().BeEmpty();
			attachment.Data.Should().Equal(bytes);
		}

		[Test]
		public async Task Neris_workload_lane_opens_only_behind_the_profile_acknowledgement()
		{
			var writer = Build();
			var submission = new RmsSubmission { RmsSubmissionId = "s1", DepartmentId = Dept, PayloadJson = "{\"incident\":1}" };
			await writer.ProtectSubmissionAsync(Dept, submission, "officer");
			submission.PayloadJson.Should().StartWith("rgdp:");

			var worker = Build(FixedProtectedGrantContext.Workload);
			(await worker.ResolveSubmissionForWorkloadAsync(Dept, submission, RecordsProtectionService.NerisSubmissionPurpose)).Should().BeFalse("no acknowledgement, no egress");
			_workloadCalls.Should().Be(0, "the broker is never asked without the department's decision");
			submission.PayloadJson.Should().StartWith("rgdp:", "a refused workload read leaves the ciphertext in place");

			_ackEgress = true;
			(await worker.ResolveSubmissionForWorkloadAsync(Dept, submission, RecordsProtectionService.NerisSubmissionPurpose)).Should().BeTrue();
			_workloadCalls.Should().Be(1);
			submission.PayloadJson.Should().Be("{\"incident\":1}");
		}

		[Test]
		public async Task Sentinel_on_an_update_restores_the_stored_envelope_instead_of_overwriting_it()
		{
			var service = Build();
			var stored = new RmsDisclosureRequest { RmsDisclosureRequestId = "q1", DepartmentId = Dept, RequesterName = "Jane Requester", ScopeNarrative = "All runs in July" };
			await service.ProtectDisclosureRequestAsync(Dept, stored, null, "officer");
			var sealedName = stored.RequesterName;

			// An editor who never had the requester revealed posts the placeholder back with a new scope.
			var edited = new RmsDisclosureRequest { RmsDisclosureRequestId = "q1", DepartmentId = Dept, RequesterName = ProtectedDataEnvelope.RedactionValue, ScopeNarrative = "All runs in July and August" };
			await service.ProtectDisclosureRequestAsync(Dept, edited, stored, "officer");

			edited.RequesterName.Should().Be(sealedName, "the placeholder restores the stored envelope for the same row");
			edited.ScopeNarrative.Should().StartWith("rgdp:").And.NotBe(stored.ScopeNarrative);
		}
	}
	/// <summary>Grant validation the way the broker sees it: "good-grant" is the officer's live grant, "stale-grant" has expired.</summary>
	internal sealed class FakeGrantService : IProtectedDataGrantService
	{
		public bool CanIssueGrants => false;
		public bool CanValidateGrants => true;
		public ProtectedDataGrantIssueResult IssueGrant(ProtectedDataGrantIssueRequest request) => throw new NotSupportedException();
		public ProtectedDataGrantValidationOutcome ValidateGrant(string token, int expectedDepartmentId, long currentPolicyEpoch, string requiredScope, out ProtectedDataGrant grant, DateTime? utcNow = null)
		{
			grant = null;
			if (token == "good-grant") { grant = new ProtectedDataGrant { UserId = "officer", DepartmentId = expectedDepartmentId }; return ProtectedDataGrantValidationOutcome.Valid; }
			if (token == "stale-grant") return ProtectedDataGrantValidationOutcome.Expired;
			return ProtectedDataGrantValidationOutcome.Invalid;
		}
	}

}

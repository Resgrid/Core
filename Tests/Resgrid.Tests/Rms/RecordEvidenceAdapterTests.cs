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
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;
using Resgrid.Services.Records.Evidence;

namespace Resgrid.Tests.Rms
{
	[TestFixture]
	public class RecordEvidenceAdapterTests
	{
		private static RecordEvidenceCaptureRequest Request() => new() { DepartmentId = 9, RecordId = "report", CapturedByUserId = "officer", CallId = 501, CaptureReason = "Officer selected supporting evidence", CoverageStart = new DateTime(2026,9,1,0,0,0,DateTimeKind.Utc), CoverageEnd = new DateTime(2026,9,1,1,0,0,DateTimeKind.Utc) };
		[Test]
		public async Task Tracking_requires_unit_tenant_and_location_permission_and_freezes_only_fixes_in_the_window()
		{
			var source = new Mock<IUnitLocationRepository>(); var units = new Mock<IUnitsService>(); var auth = new Mock<IAuthorizationService>();
			units.Setup(u => u.GetUnitByIdAsync(5)).ReturnsAsync(new Unit { UnitId = 5, DepartmentId = 9 });
			var fix = new UnitLocation { UnitLocationId = 10, UnitId = 5, Timestamp = Request().CoverageStart.Value, Latitude = 38, Longitude = -119 };
			source.Setup(s => s.GetLastUnitLocationByUnitIdTimestampAsync(5, It.IsAny<DateTime>())).ReturnsAsync(() => fix);
			var adapter = new TrackingFixEvidenceAdapter(source.Object, units.Object, new Lazy<IAuthorizationService>(() => auth.Object)); var request = Request(); request.UnitIds = new() { 5 };
			Func<Task> denied = () => adapter.CaptureAsync(request); await denied.Should().ThrowAsync<UnauthorizedAccessException>();
			source.Verify(s => s.GetLastUnitLocationByUnitIdTimestampAsync(5, It.IsAny<DateTime>()), Times.Never);
			auth.Setup(a => a.CanUserViewUnitLocationAsync("officer", 5, 9)).ReturnsAsync(true);
			var capture = await adapter.CaptureAsync(request); capture.SourceItemCount.Should().Be(1);
			var frozen = RecordsEvidenceService.Serialize(capture.Manifest); fix.Latitude = 0;
			RecordsEvidenceService.Serialize(capture.Manifest).Should().Be(frozen).And.Contain("38");
			units.Setup(u => u.GetUnitByIdAsync(5)).ReturnsAsync(new Unit { UnitId = 5, DepartmentId = 99 }); await denied.Should().ThrowAsync<UnauthorizedAccessException>();
		}
		[Test]
		public async Task Chat_requires_channel_permission_and_rejects_deleted_moderated_foreign_and_revoked_messages()
		{
			var messages = new Mock<IChatMessageRepository>(); var channels = new Mock<IChatChannelRepository>(); var permission = new Mock<IChatPermissionService>();
			var channel = new ChatChannel { ChatChannelId = "channel", DepartmentId = 9, CallId = 501 };
			channels.Setup(c => c.GetByCallIdAsync(501)).ReturnsAsync(new[] { channel });
			var message = new ChatMessage { ChatMessageId = "message", ChatChannelId = "channel", DepartmentId = 9, Body = "Crew clear", SenderUserId = "member", MessageSeq = 42, SentOn = Request().CoverageStart.Value };
			messages.Setup(m => m.GetByIdAsync("message")).ReturnsAsync(() => message);
			var adapter = new ChatPromotionEvidenceAdapter(messages.Object, channels.Object, new Lazy<IChatPermissionService>(() => permission.Object)); var request = Request(); request.SourceIds = new() { "message" };
			(await adapter.CaptureAsync(request)).Available.Should().BeFalse(); messages.Verify(m => m.GetByIdAsync("message"), Times.Never);
			permission.Setup(p => p.CanAccessChannelAsync(channel, "officer", null)).ReturnsAsync(true);
			var capture = await adapter.CaptureAsync(request); var frozen = RecordsEvidenceService.Serialize(capture.Manifest); capture.Classification.Should().Be(RmsEvidenceClassification.Restricted);
			message.Body = "Changed later"; RecordsEvidenceService.Serialize(capture.Manifest).Should().Be(frozen).And.Contain("Crew clear");
			foreach (var mutate in new Action[] { () => message.DeletedOn = DateTime.UtcNow, () => { message.DeletedOn = null; message.IsModerated = true; }, () => { message.IsModerated = false; message.DepartmentId = 10; } })
			{ mutate(); Func<Task> denied = () => adapter.CaptureAsync(request); await denied.Should().ThrowAsync<UnauthorizedAccessException>(); }
			message.DepartmentId = 9;
			permission.SetupSequence(p => p.CanAccessChannelAsync(channel, "officer", null)).ReturnsAsync(true).ReturnsAsync(false);
			Func<Task> revoked = () => adapter.CaptureAsync(request); await revoked.Should().ThrowAsync<UnauthorizedAccessException>();
		}
		[Test]
		public async Task Certification_snapshot_requires_person_visibility_and_excludes_certificate_numbers_and_files()
		{
			var source = new Mock<ICertificationService>(); var auth = new Mock<IAuthorizationService>();
			var certificate = new PersonnelCertification { PersonnelCertificationId = 13, DepartmentId = 9, UserId = "member", Name = "Firefighter II", Number = "SECRET-NUMBER", Filename = "SECRET-FILE", Data = new byte[] { 1, 2 }, RecievedOn = new DateTime(2025,1,1), ExpiresOn = new DateTime(2027,1,1) };
			source.Setup(s => s.GetCertificationsByUserIdAsync("member")).ReturnsAsync(new List<PersonnelCertification> { certificate });
			var adapter = new CertificationSnapshotEvidenceAdapter(source.Object, Mock.Of<IRmsRecordParticipantsRepository>(), new Lazy<IAuthorizationService>(() => auth.Object)); var request = Request(); request.UserIds = new() { "member" };
			Func<Task> denied = () => adapter.CaptureAsync(request); await denied.Should().ThrowAsync<UnauthorizedAccessException>();
			source.Verify(s => s.GetCertificationsByUserIdAsync("member"), Times.Never);
			auth.Setup(a => a.CanUserViewPersonAsync("officer", "member", 9)).ReturnsAsync(true);
			var capture = await adapter.CaptureAsync(request); var frozen = RecordsEvidenceService.Serialize(capture.Manifest);
			frozen.Should().Contain("Firefighter II").And.Contain("source_id").And.NotContain("SECRET").And.NotContain("AQI=");
			certificate.Name = "Edited later"; RecordsEvidenceService.Serialize(capture.Manifest).Should().Be(frozen);
		}
		[Test]
		public async Task Run_card_capture_rejects_other_department_and_call_rows_and_freezes_the_recorded_decision()
		{
			var source = new Mock<IRunCardActivationsRepository>();
			var decision = new RunCardActivation { RunCardActivationId = 7, DepartmentId = 9, CallId = 501, RunCardId = 3, ResultJson = "{\"selected\":[\"Engine 5\"],\"shortfall\":0}", CreatedOn = Request().CoverageStart.Value };
			source.Setup(s => s.GetActivationsByCallIdAsync(501)).ReturnsAsync(new[] { decision, new RunCardActivation { DepartmentId = 10, CallId = 501, ResultJson = "{\"secret\":true}" }, new RunCardActivation { DepartmentId = 9, CallId = 999, ResultJson = "{\"secret\":true}" } });
			var adapter = new RunCardActivationEvidenceAdapter(source.Object); var capture = await adapter.CaptureAsync(Request()); var frozen = RecordsEvidenceService.Serialize(capture.Manifest);
			capture.SourceItemCount.Should().Be(1); frozen.Should().Contain("Engine 5").And.NotContain("secret");
			decision.ResultJson = "{}"; RecordsEvidenceService.Serialize(capture.Manifest).Should().Be(frozen);
		}

		[Test]
		public async Task Oversized_source_selections_are_rejected_instead_of_silently_producing_partial_evidence()
		{
			var request = Request(); request.UnitIds = Enumerable.Range(1, 21).ToList(); request.SourceIds = Enumerable.Range(1, 501).Select(i => i.ToString()).ToList(); request.UserIds = request.SourceIds;
			var locations = new Mock<IUnitLocationRepository>(); var messages = new Mock<IChatMessageRepository>(); var certifications = new Mock<ICertificationService>();
			var tracking = new TrackingFixEvidenceAdapter(locations.Object, Mock.Of<IUnitsService>(), new Lazy<IAuthorizationService>(() => Mock.Of<IAuthorizationService>()));
			var chat = new ChatPromotionEvidenceAdapter(messages.Object, Mock.Of<IChatChannelRepository>(), new Lazy<IChatPermissionService>(() => Mock.Of<IChatPermissionService>()));
			var certificate = new CertificationSnapshotEvidenceAdapter(certifications.Object, Mock.Of<IRmsRecordParticipantsRepository>(), new Lazy<IAuthorizationService>(() => Mock.Of<IAuthorizationService>()));
			foreach (var adapter in new IRecordEvidenceAdapter[] { tracking, chat, certificate })
			{
				Func<Task> capture = () => adapter.CaptureAsync(request); await capture.Should().ThrowAsync<ArgumentException>();
			}
			locations.VerifyNoOtherCalls(); messages.VerifyNoOtherCalls(); certifications.VerifyNoOtherCalls();
			var activations = new Mock<IRunCardActivationsRepository>();
			activations.Setup(a => a.GetActivationsByCallIdAsync(501)).ReturnsAsync(Enumerable.Range(1, 501).Select(i => new RunCardActivation { DepartmentId = 9, CallId = 501, RunCardActivationId = i }));
			Func<Task> runCards = () => new RunCardActivationEvidenceAdapter(activations.Object).CaptureAsync(Request()); await runCards.Should().ThrowAsync<ArgumentException>();
			var usage = new Mock<IRmsInventoryUsageAdapter>(); var auth = new Mock<IRecordsAuthorizationService>();
			auth.Setup(a => a.CanUseSourceInventoryAsync("officer", 9, null)).ReturnsAsync(true);
			usage.Setup(u => u.GetUsageForRecordAsync(9, "report")).ReturnsAsync(Enumerable.Range(1, 501).Select(i => new RmsInventoryUsage()).ToList());
			Func<Task> inventory = () => new InventoryUsageEvidenceAdapter(usage.Object, auth.Object).CaptureAsync(Request()); await inventory.Should().ThrowAsync<ArgumentException>();
		}

		[TestCase("{broken")]
		[TestCase("{\"decision\":")]
		public async Task Malformed_recorded_run_card_decisions_cannot_be_replaced_with_null_evidence(string json)
		{
			var source = new Mock<IRunCardActivationsRepository>();
			source.Setup(a => a.GetActivationsByCallIdAsync(501)).ReturnsAsync(new[] { new RunCardActivation { DepartmentId = 9, CallId = 501, ResultJson = json } });
			Func<Task> capture = () => new RunCardActivationEvidenceAdapter(source.Object).CaptureAsync(Request());
			await capture.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unreadable*");
		}

		[Test]
		public async Task Certifications_issued_after_the_incident_are_invalid_even_when_not_expired()
		{
			var source = new Mock<ICertificationService>(); var auth = new Mock<IAuthorizationService>();
			auth.Setup(a => a.CanUserViewPersonAsync("officer", "member", 9)).ReturnsAsync(true);
			var request = Request(); request.UserIds = new() { "member" };
			source.Setup(s => s.GetCertificationsByUserIdAsync("member")).ReturnsAsync(new List<PersonnelCertification>
			{
				new() { PersonnelCertificationId = 1, DepartmentId = 9, UserId = "member", RecievedOn = request.CoverageEnd.Value.AddDays(1) },
				new() { PersonnelCertificationId = 2, DepartmentId = 9, UserId = "member", RecievedOn = request.CoverageStart.Value.AddYears(-1), ExpiresOn = request.CoverageEnd.Value.AddDays(-1) },
				new() { PersonnelCertificationId = 3, DepartmentId = 9, UserId = "member", RecievedOn = request.CoverageEnd, ExpiresOn = request.CoverageEnd }
			});
			var capture = await new CertificationSnapshotEvidenceAdapter(source.Object, Mock.Of<IRmsRecordParticipantsRepository>(), new Lazy<IAuthorizationService>(() => auth.Object)).CaptureAsync(request);
			var items = JObject.Parse(RecordsEvidenceService.Serialize(capture.Manifest))["people"][0]["certifications"];
			items.Select(i => (bool)i["valid_at_incident"]).Should().Equal(false, false, true);
		}

		[Test]
		public async Task Chat_channel_moved_to_another_incident_during_capture_is_rejected()
		{
			var messages = new Mock<IChatMessageRepository>(); var channels = new Mock<IChatChannelRepository>(); var permission = new Mock<IChatPermissionService>();
			var channel = new ChatChannel { ChatChannelId = "channel", DepartmentId = 9, CallId = 501 };
			channels.Setup(c => c.GetByCallIdAsync(501)).ReturnsAsync(new[] { channel });
			permission.Setup(p => p.CanAccessChannelAsync(channel, "officer", null)).ReturnsAsync(true);
			messages.Setup(m => m.GetByIdAsync("message")).Callback(() => channel.CallId = 999).ReturnsAsync(new ChatMessage { ChatMessageId = "message", ChatChannelId = "channel", DepartmentId = 9, Body = "Crew clear" });
			var request = Request(); request.SourceIds = new() { "message" };
			Func<Task> capture = () => new ChatPromotionEvidenceAdapter(messages.Object, channels.Object, new Lazy<IChatPermissionService>(() => permission.Object)).CaptureAsync(request);
			await capture.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[TestCase(-1)]
		[TestCase(25)]
		public async Task Tracking_rejects_reversed_or_overlong_windows_before_reading_locations(int hours)
		{
			var locations = new Mock<IUnitLocationRepository>(); var request = Request(); request.UnitIds = new() { 5 };
			request.CoverageEnd = request.CoverageStart.Value.AddHours(hours);
			var adapter = new TrackingFixEvidenceAdapter(locations.Object, Mock.Of<IUnitsService>(), new Lazy<IAuthorizationService>(() => Mock.Of<IAuthorizationService>()));
			Func<Task> capture = () => adapter.CaptureAsync(request);
			await capture.Should().ThrowAsync<ArgumentException>(); locations.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Tracking_windows_and_certification_people_and_times_have_distinct_bounded_identities()
		{
			var request = Request(); request.UnitIds = new() { 5 };
			var units = new Mock<IUnitsService>(); units.Setup(u => u.GetUnitByIdAsync(5)).ReturnsAsync(new Unit { DepartmentId = 9, UnitId = 5 });
			var auth = new Mock<IAuthorizationService>(); auth.Setup(a => a.CanUserViewUnitLocationAsync("officer", 5, 9)).ReturnsAsync(true);
			var locations = new Mock<IUnitLocationRepository>(); locations.Setup(l => l.GetLastUnitLocationByUnitIdTimestampAsync(5, It.IsAny<DateTime>()))
				.ReturnsAsync((int unit, DateTime at) => new UnitLocation { UnitId = unit, UnitLocationId = 1, Timestamp = at });
			var tracking = new TrackingFixEvidenceAdapter(locations.Object, units.Object, new Lazy<IAuthorizationService>(() => auth.Object));
			var first = await tracking.CaptureAsync(request); request.CoverageEnd = request.CoverageEnd.Value.AddHours(1);
			(await tracking.CaptureAsync(request)).SourceEntityId.Should().NotBe(first.SourceEntityId);
			var certificates = new Mock<ICertificationService>();
			certificates.Setup(c => c.GetCertificationsByUserIdAsync(It.IsAny<string>())).ReturnsAsync((string id) => new List<PersonnelCertification> { new() { DepartmentId = 9, UserId = id } });
			auth.Setup(a => a.CanUserViewPersonAsync("officer", It.IsAny<string>(), 9)).ReturnsAsync(true);
			var adapter = new CertificationSnapshotEvidenceAdapter(certificates.Object, Mock.Of<IRmsRecordParticipantsRepository>(), new Lazy<IAuthorizationService>(() => auth.Object));
			request.UserIds = new() { "one" }; var one = await adapter.CaptureAsync(request);
			request.UserIds = new() { "two" }; var two = await adapter.CaptureAsync(request);
			request.UserIds = new() { "one" }; request.CoverageEnd = request.CoverageEnd.Value.AddDays(1); var later = await adapter.CaptureAsync(request);
			new[] { one.SourceEntityId, two.SourceEntityId, later.SourceEntityId }.Distinct().Should().HaveCount(3);
			one.SourceEntityId.Length.Should().BeLessThan(200);
		}
	}
}

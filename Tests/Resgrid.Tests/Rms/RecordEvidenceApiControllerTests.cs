using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services.Records;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Rms
{
	[TestFixture, NonParallelizable]
	public class RecordEvidenceApiControllerTests
	{
		private const int Department = 42;
		private const string Officer = "officer";
		private Mock<IRecordsEvidenceService> _evidence;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<IRecordsApiIdempotencyService> _idempotency;
		private RecordEvidenceController _controller;
		private DefaultHttpContext _http;
		private Activity _activity;

		[SetUp]
		public void Setup()
		{
			_evidence = new Mock<IRecordsEvidenceService>();
			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.CanUserViewRecordAsync(Officer, It.IsAny<string>(), Department)).ReturnsAsync(true);
			_authorization.Setup(a => a.HasPermissionAsync(Officer, Department, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
			_idempotency = new Mock<IRecordsApiIdempotencyService>();
			var cutover = new Mock<IRecordsCutoverService>();
			cutover.Setup(c => c.GetModuleStateAsync(Department, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState
			{ DepartmentId = Department, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active, LegacyWritesBlocked = true });
			_http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.PrimarySid, Officer), new Claim(ClaimTypes.PrimaryGroupSid, Department.ToString()),
				new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View),
				new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Create),
				new Claim(ResgridClaimTypes.Resources.RecordRestricted, ResgridClaimTypes.Actions.View)
			}, "test")) };
			_http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
			_activity = new Activity(nameof(RecordEvidenceApiControllerTests)).Start();
			_controller = new RecordEvidenceController(_evidence.Object, cutover.Object, _authorization.Object,
				Mock.Of<IFeatureToggleService>(), _idempotency.Object) { ControllerContext = new ControllerContext { HttpContext = _http } };
		}

		[TearDown] public void Cleanup() => _activity?.Stop();

		private static CaptureRecordEvidenceInput Input() => new() { RecordId = "report", ExpectedRowVersion = 7,
			Kind = (int)RmsEvidenceKind.ChatPromotion, SourceIds = new() { "message" }, CaptureReason = "Officer selected supporting evidence", OriginClient = (int)RmsOriginClient.Api, IdempotencyKey = "capture-1" };
		private static RmsEvidenceArtifact Artifact(CaptureRecordEvidenceInput input) => new()
		{
			RmsEvidenceArtifactId = "artifact", DepartmentId = Department, RecordId = input.RecordId,
			CapturedByUserId = Officer, Classification = (int)RmsEvidenceClassification.Restricted,
			Title = "Sensitive channel", CaptureReason = "Sensitive reason", SourceEntityId = "sensitive-channel",
			ManifestJson = "{\"body\":\"Sensitive message\"}", Checksum = "sensitive-hash",
			CaptureRequestChecksum = RecordsEvidenceService.ComputeRequestChecksum(RecordEvidenceApiMapper.ToCaptureRequest(input, Department, Officer, RmsOriginClient.Api))
		};
		private void Replay(RmsEvidenceArtifact artifact)
		{
			_idempotency.Setup(i => i.TryGetRecordIdAsync(Department, Officer, "capture-1", "Capture")).ReturnsAsync("artifact");
			_evidence.Setup(e => e.GetAsync(Department, "artifact")).ReturnsAsync(artifact);
		}

		[Test]
		public async Task Capture_requires_a_parent_version_before_reading_or_mutating_evidence()
		{
			var input = Input(); input.ExpectedRowVersion = null;
			var result = await _controller.Capture(input, default);
			result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(428);
			_evidence.VerifyNoOtherCalls(); _idempotency.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Capture_accepts_IfMatch_and_passes_the_server_actor_and_department()
		{
			var input = Input(); var artifact = Artifact(input); input.ExpectedRowVersion = null; _http.Request.Headers.IfMatch = "\"7\"";
			_evidence.Setup(e => e.CaptureAsync(It.IsAny<RecordEvidenceCaptureRequest>(), true, It.IsAny<CancellationToken>())).ReturnsAsync(artifact);
			(await _controller.Capture(input, default)).Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(201);
			_evidence.Verify(e => e.CaptureAsync(It.Is<RecordEvidenceCaptureRequest>(r => r.DepartmentId == Department && r.CapturedByUserId == Officer && r.ExpectedRowVersion == 7), true, It.IsAny<CancellationToken>()), Times.Once);
		}

		[TestCase("record")]
		[TestCase("actor")]
		[TestCase("reason")]
		[TestCase("selection")]
		[TestCase("version")]
		[TestCase("legacy")]
		public async Task Replay_rejects_a_different_capture_or_an_unbound_legacy_artifact(string change)
		{
			var input = Input(); var artifact = Artifact(input); Replay(artifact);
			switch (change)
			{
				case "record": artifact.RecordId = "another-report"; break;
				case "actor": artifact.CapturedByUserId = "another-officer"; break;
				case "reason": input.CaptureReason = "Changed reason"; break;
				case "selection": input.SourceIds.Add("another-message"); break;
				case "version": input.ExpectedRowVersion++; break;
				case "legacy": artifact.CaptureRequestChecksum = null; break;
			}
			(await _controller.Capture(input, default)).Result.Should().BeOfType<ConflictObjectResult>();
			_evidence.Verify(e => e.CaptureAsync(It.IsAny<RecordEvidenceCaptureRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Replay_rechecks_access_to_the_original_artifact_and_never_returns_it_after_revocation()
		{
			var input = Input(); Replay(Artifact(input));
			_authorization.SetupSequence(a => a.CanUserViewRecordAsync(Officer, "report", Department)).ReturnsAsync(true).ReturnsAsync(false);
			(await _controller.Capture(input, default)).Result.Should().BeOfType<ConflictObjectResult>();
		}

		[Test]
		public async Task Exact_replay_returns_the_original_capture_without_a_second_write()
		{
			var input = Input(); Replay(Artifact(input));
			var result = (await _controller.Capture(input, default)).Result.Should().BeOfType<OkObjectResult>().Subject;
			result.Value.Should().BeOfType<RecordEvidenceResult>().Which.Data.ManifestJson.Should().Contain("Sensitive message");
			_evidence.Verify(e => e.CaptureAsync(It.IsAny<RecordEvidenceCaptureRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Stale_restricted_claims_do_not_expose_manifest_or_provenance_through_read_list_replay_or_verify()
		{
			var input = Input(); var artifact = Artifact(input); Replay(artifact);
			_authorization.Setup(a => a.HasPermissionAsync(Officer, Department, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			_evidence.Setup(e => e.GetForRecordAsync(Department, "report", null, false)).ReturnsAsync(new List<RmsEvidenceArtifact> { artifact });
			var read = (await _controller.GetArtifact("artifact")).Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RecordEvidenceResult>().Subject.Data;
			var replay = (await _controller.Capture(input, default)).Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RecordEvidenceResult>().Subject.Data;
			var list = (await _controller.GetEvidence("report", null)).Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<RecordEvidenceListResult>().Subject.Data[0];
			foreach (var data in new[] { read, replay, list })
			{
				data.ManifestWithheld.Should().BeTrue(); data.Title.Should().Be("Restricted evidence");
				data.ManifestJson.Should().BeNull(); data.SourceEntityId.Should().BeNull(); data.CapturedByUserId.Should().BeNull();
				data.Checksum.Should().BeNull(); data.CaptureReason.Should().BeNull();
			}
			(await _controller.Verify("artifact")).Result.Should().BeOfType<ForbidResult>();
		}

		[Test]
		public async Task Verify_and_manifest_reads_recheck_record_access_after_the_last_awaited_permission_or_source_read()
		{
			Replay(Artifact(Input())); var allowed = true;
			_authorization.Setup(a => a.CanUserViewRecordAsync(Officer, "report", Department)).ReturnsAsync(() => allowed);
			_evidence.Setup(e => e.VerifyAsync(Department, "artifact")).Callback(() => allowed = false).ReturnsAsync(true);
			(await _controller.Verify("artifact")).Result.Should().BeOfType<NotFoundResult>();
			allowed = true;
			_authorization.Setup(a => a.HasPermissionAsync(Officer, Department, PermissionTypes.ViewRestrictedRecords)).Callback(() => allowed = false).ReturnsAsync(true);
			(await _controller.GetArtifact("artifact")).Result.Should().BeOfType<NotFoundResult>();
		}
	}
}

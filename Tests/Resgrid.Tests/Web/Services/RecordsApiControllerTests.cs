using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// v4 Records contract (RMS-1B, plan 5.3/5.4/5.9.1): flag gate, capability manifest with the protection block,
	/// ETag on reads, 409 conflict with changed field paths, idempotent create replay, command idempotency, field-client
	/// flag gate, delta cursor with tombstones, restricted-field withholding, visibility denial.
	/// </summary>
	[TestFixture]
	public class RecordsApiControllerTests
	{
		private const int Dept = 42;
		private const string Me = "author";
		private const string RecordId = "11111111-1111-1111-1111-111111111111";

		private Mock<IRecordsService> _records;
		private Mock<IRecordsCutoverService> _cutover;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<IFeatureToggleService> _flags;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IDepartmentDataProtectionService> _adp;
		private Mock<IRecordsSearchService> _search;
		private Mock<IRecordAttachmentUploadService> _uploads;
		private Mock<IRecordsApiIdempotencyService> _idempotency;
		private Mock<IRecordsDashboardService> _dashboard;
		private RecordsModuleState _moduleState;
		private RecordsController _controller;
		private DefaultHttpContext _http;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_records = new Mock<IRecordsService>();
			_cutover = new Mock<IRecordsCutoverService>();
			_moduleState = new RecordsModuleState { DepartmentId = Dept, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active, LegacyWritesBlocked = true };
			_cutover.Setup(c => c.GetModuleStateAsync(Dept, It.IsAny<bool>())).ReturnsAsync(() => _moduleState);

			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.IsActiveMemberAsync(Me, Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.HasPermissionAsync(Me, Dept, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
			_authorization.Setup(a => a.CanUserViewRecordAsync(Me, It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync(Me, Dept)).ReturnsAsync((List<int>)null);
			_authorization.Setup(a => a.GetReadScopeStampAsync(Me, Dept)).ReturnsAsync("membership-1");

			_flags = new Mock<IFeatureToggleService>();
			_flags.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(false);

			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetRecordsGroupVisibilityModeAsync(Dept, It.IsAny<bool>())).ReturnsAsync(RecordsGroupVisibilityMode.DepartmentWide);
			_adp = new Mock<IDepartmentDataProtectionService>();
			_search = new Mock<IRecordsSearchService>();
			_search.SetupGet(s => s.IsAvailable).Returns(false);
			_uploads = new Mock<IRecordAttachmentUploadService>();
			_uploads.SetupGet(u => u.ChunkSize).Returns(512 * 1024);
			_idempotency = new Mock<IRecordsApiIdempotencyService>();
			_idempotency.Setup(i => i.TryReserveCommandAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
			_dashboard = new Mock<IRecordsDashboardService>();

			_http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Me),
					new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()),
					new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View),
					new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Create),
					new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Finalize)
				}, "test"))
			};
			_http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
			_activity = new Activity("RecordsApiControllerTests").Start();

			_controller = new RecordsController(_records.Object, _cutover.Object, _authorization.Object, _flags.Object, _settings.Object, _adp.Object, _search.Object, _uploads.Object, _idempotency.Object, _dashboard.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = _http }
			};
		}

		[TearDown]
		public void TearDown()
		{
			_activity?.Stop();
		}

		private static RecordAggregate Aggregate(long rowVersion = 3, string definitionKey = RmsDefinitionKeys.Training, RmsRecordState state = RmsRecordState.Draft)
		{
			return new RecordAggregate
			{
				Record = new RmsOperationalRecord
				{
					RmsOperationalRecordId = RecordId, DepartmentId = Dept, DefinitionKey = definitionKey, DefinitionVersion = 1,
					RecordType = (int)RmsDefinitionKeys.LockedTypes[definitionKey], LifecyclePreset = (int)RmsLifecyclePreset.QuickEntry, State = (int)state,
					DraftReference = "D-ABCDE", AuthorUserId = Me, OwnerUserId = Me, RowVersion = rowVersion, CreatedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow
				},
				Details = new RmsOperationalRecordDetail { Narrative = "Drill", Course = "CPR", CaseNumber = "C-1", BodyLocation = "Room 2" }
			};
		}

		[Test]
		public async Task Flag_off_hides_every_endpoint()
		{
			_moduleState.FlagEnabled = false;

			(await _controller.Capabilities()).Result.Should().BeOfType<NotFoundResult>();
			(await _controller.GetRecord(RecordId)).Result.Should().BeOfType<NotFoundResult>();
			(await _controller.GetRecords(null, null, null, null, null, null)).Result.Should().BeOfType<NotFoundResult>();
			(await _controller.Changes()).Result.Should().BeOfType<NotFoundResult>();
			(await _controller.CreateDraft(new SaveRecordDraftInput { DefinitionKey = RmsDefinitionKeys.Training }, CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task Capabilities_carry_the_contract_definitions_permissions_and_the_inert_protection_block()
		{
			_flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.RecordsFieldResponder, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);

			var response = await _controller.Capabilities();

			var data = ((RecordsCapabilitiesResult)((OkObjectResult)response.Result).Value).Data;
			data.ContractVersion.Should().Be(RecordsApiContract.Version);
			data.RecordsUsable.Should().BeTrue();
			data.Permissions.CanCreate.Should().BeTrue();
			data.Permissions.CanFinalize.Should().BeTrue();
			data.Permissions.CanVoid.Should().BeFalse();
			data.FieldClients.Responder.Should().BeTrue();
			data.FieldClients.Unit.Should().BeFalse();
			data.Definitions.Select(d => d.Key).Should().Contain(RmsDefinitionKeys.LockedTypes.Keys);
			data.Definitions.Should().OnlyContain(d => d.MinimumClientCapability == RecordsApiContract.LockedDefinitionCapability);
			data.Protection.State.Should().Be("NotInstalled");
			data.Protection.CatalogVersion.Should().Be(0);
			data.Protection.GrantExpiresOn.Should().BeNull();
			data.Protection.StepUpWindowMinutes.Should().BeNull();
			data.Protection.MinimumClientVersion.Should().BeNull();
			data.Search.Available.Should().BeFalse();
			data.MaxAttachmentBytes.Should().Be(Resgrid.Services.Records.RecordAttachmentHygiene.MaxBytes);
			data.GroupVisibilityMode.Should().Be("DepartmentWide");
		}

		[Test]
		public async Task Capabilities_protection_block_reflects_an_enrolled_department()
		{
			_adp.Setup(a => a.GetPolicyByDepartmentIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new DepartmentDataProtectionPolicy { State = (int)DepartmentDataProtectionState.Enabled, CatalogVersion = 3, StepUpWindowMinutes = 15 });

			var data = ((RecordsCapabilitiesResult)((OkObjectResult)(await _controller.Capabilities()).Result).Value).Data;

			data.Protection.State.Should().Be("Enabled");
			data.Protection.CatalogVersion.Should().Be(3);
			data.Protection.StepUpWindowMinutes.Should().Be(15);
		}

		[Test]
		public async Task Get_record_sets_the_ETag_audits_the_read_and_withholds_restricted_fields()
		{
			_records.Setup(r => r.GetAsync(Dept, RecordId, true)).ReturnsAsync(Aggregate(7, RmsDefinitionKeys.Coroner));

			var response = await _controller.GetRecord(RecordId);

			var result = (RecordResult)((OkObjectResult)response.Result).Value;
			result.Data.ETag.Should().Be("W/\"7\"");
			_http.Response.Headers[HeaderNames.ETag].ToString().Should().Be("W/\"7\"");
			result.Data.IsRestricted.Should().BeTrue();
			result.Data.Details.Narrative.Should().Be("Drill", "the narrative is not part of the restricted section");
			result.Data.Details.CaseNumber.Should().BeNull("the caller lacks RecordRestricted_View");
			result.Data.WithheldFields.Should().Contain(new[] { "CaseNumber", "BodyLocation" });
			result.Data.AvailableTransitions.Should().Contain("Finalized");
			_records.Verify(r => r.RecordAccessAsync(Dept, Me, RecordId, null, RmsAccessAuditAction.Read, null, It.IsAny<string>(), RmsOriginClient.Api), Times.Once);
		}

		[Test]
		public async Task Visibility_refusal_is_a_404_and_an_audited_denial()
		{
			_authorization.Setup(a => a.CanUserViewRecordAsync(Me, RecordId, Dept)).ReturnsAsync(false);

			(await _controller.GetRecord(RecordId)).Result.Should().BeOfType<NotFoundResult>();

			_records.Verify(r => r.GetAsync(Dept, RecordId, It.IsAny<bool>()), Times.Never);
			_records.Verify(r => r.RecordAccessAsync(Dept, Me, RecordId, null, RmsAccessAuditAction.Denied, null, It.IsAny<string>(), RmsOriginClient.Api), Times.Once);
		}

		[Test]
		public async Task A_stale_restricted_claim_cannot_disclose_fields_in_reads_or_conflicts()
		{
			((ClaimsIdentity)_http.User.Identity).AddClaim(new Claim(ResgridClaimTypes.Resources.RecordRestricted, ResgridClaimTypes.Actions.View));
			_authorization.Setup(a => a.HasPermissionAsync(Me, Dept, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			_records.Setup(r => r.GetAsync(Dept, RecordId, It.IsAny<bool>())).ReturnsAsync(Aggregate(5, RmsDefinitionKeys.Coroner));
			var read = (RecordResult)((OkObjectResult)(await _controller.GetRecord(RecordId)).Result).Value;
			read.Data.Details.CaseNumber.Should().BeNull();
			_records.Setup(r => r.SaveDraftAsync(Dept, Me, RecordId, 4, It.IsAny<RecordDraftInput>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new RecordConcurrencyException(RecordId, 4, 5));
			var conflict = await _controller.SaveDraft(new SaveRecordDraftInput { RecordId = RecordId, RowVersion = 4, DefinitionKey = RmsDefinitionKeys.Coroner, Details = new RecordDetailsInput { Narrative = "Changed" } }, CancellationToken.None);
			Newtonsoft.Json.JsonConvert.SerializeObject(((ConflictObjectResult)conflict.Result).Value).Should().NotContain("C-1").And.NotContain("Room 2");
		}

		[Test]
		public async Task Changing_visibility_without_modifying_a_record_forces_delta_cache_eviction()
		{
			_records.Setup(r => r.GetChangesSinceAsync(Dept, It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new List<RmsRecordSearchProjection>());
			var initial = (RecordsChangesResult)((OkObjectResult)(await _controller.Changes()).Result).Value;
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync(Me, Dept)).ReturnsAsync(new List<int> { 10 });
			var response = (RecordsChangesResult)((OkObjectResult)(await _controller.Changes(initial.Data.ServerTimestampMs, 200, null, initial.Data.ScopeStamp)).Result).Value;
			response.Data.ResetRequired.Should().BeTrue();
			response.Data.ServerTimestampMs.Should().Be(0);
			response.Data.Records.Should().BeEmpty();
			_records.Verify(r => r.GetChangesSinceAsync(Dept, It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<string>()), Times.Once);
		}

		[Test]
		public async Task Removed_members_cannot_use_the_delta_feed_with_a_still_valid_token()
		{
			_authorization.Setup(a => a.IsActiveMemberAsync(Me, Dept)).ReturnsAsync(false);
			(await _controller.Changes()).Result.Should().BeOfType<ForbidResult>();
			_records.Verify(r => r.GetChangesSinceAsync(Dept, It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Revoked_membership_refuses_feed_and_deep_links_despite_valid_claims_and_ownership()
		{
			_authorization.Setup(a => a.IsActiveMemberAsync(Me, Dept)).ReturnsAsync(false);
			(await _controller.Changes()).Result.Should().BeOfType<ForbidResult>();
			(await _controller.GetRecord(RecordId)).Result.Should().BeOfType<NotFoundResult>();
			_records.Verify(r => r.GetAsync(Dept, It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
			_records.Verify(r => r.GetChangesSinceAsync(Dept, It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[TestCase("roles")]
		[TestCase("protection")]
		public async Task Policy_or_role_change_invalidates_unmodified_cached_rows(string change)
		{
			_records.Setup(r => r.GetChangesSinceAsync(Dept, It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new List<RmsRecordSearchProjection>());
			var initial = ((RecordsChangesResult)((OkObjectResult)(await _controller.Changes()).Result).Value).Data;
			if (change == "roles") _authorization.Setup(a => a.GetReadScopeStampAsync(Me, Dept)).ReturnsAsync("membership-2");
			else _adp.Setup(a => a.GetPolicyByDepartmentIdAsync(Dept, true)).ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = Dept, PolicyEpoch = 2 });
			var next = ((RecordsChangesResult)((OkObjectResult)(await _controller.Changes(initial.ServerTimestampMs, 200, null, initial.ScopeStamp)).Result).Value).Data;
			next.ResetRequired.Should().BeTrue();
			next.Records.Should().BeEmpty();
			next.ServerTimestampMs.Should().Be(0);
			_records.Verify(r => r.GetChangesSinceAsync(Dept, It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<string>()), Times.Once);
		}

		[Test]
		public async Task Role_change_during_page_hydration_discards_all_previously_authorized_content()
		{
			_records.Setup(r => r.GetChangesSinceAsync(Dept, It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<string>()))
				.Callback(() => _authorization.Setup(a => a.GetReadScopeStampAsync(Me, Dept)).ReturnsAsync("membership-2"))
				.ReturnsAsync(new List<RmsRecordSearchProjection> { new RmsRecordSearchProjection { DepartmentId = Dept, RmsRecordSearchProjectionId = RecordId, DisplaySummary = "Secret report", ModifiedOn = DateTime.UtcNow } });
			var response = (RecordsChangesResult)((OkObjectResult)(await _controller.Changes()).Result).Value;
			response.Data.ResetRequired.Should().BeTrue();
			response.Data.Records.Should().BeEmpty();
			Newtonsoft.Json.JsonConvert.SerializeObject(response).Should().NotContain("Secret report");
		}

		[Test]
		public async Task Unavailable_scope_or_protection_policy_fails_closed_before_reading_a_page()
		{
			_adp.Setup(a => a.GetPolicyByDepartmentIdAsync(Dept, true)).ThrowsAsync(new InvalidOperationException("unavailable"));
			(await _controller.Changes()).Result.Should().BeOfType<ForbidResult>();
			_adp.Setup(a => a.GetPolicyByDepartmentIdAsync(Dept, true)).ReturnsAsync((DepartmentDataProtectionPolicy)null);
			_authorization.Setup(a => a.GetReadScopeStampAsync(Me, Dept)).ReturnsAsync((string)null);
			(await _controller.Changes()).Result.Should().BeOfType<ForbidResult>();
			_records.Verify(r => r.GetChangesSinceAsync(Dept, It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[TestCase("restricted")]
		[TestCase("view")]
		[TestCase("removed")]
		[TestCase("checksum")]
		[TestCase("tenant")]
		public async Task Attachment_egress_rechecks_permissions_and_file_after_audit(string attack)
		{
			((ClaimsIdentity)_http.User.Identity).AddClaim(new Claim(ResgridClaimTypes.Resources.RecordRestricted, ResgridClaimTypes.Actions.View));
			_records.Setup(r => r.GetAsync(Dept, RecordId, It.IsAny<bool>())).ReturnsAsync(Aggregate());
			var file = new RmsRecordAttachment { RmsRecordAttachmentId = "file", RecordId = RecordId, DepartmentId = Dept,
				Data = System.Text.Encoding.UTF8.GetBytes("private file"), ScanState = (int)RmsAttachmentScanState.Clean, Classification = 1 };
			file.Checksum = Resgrid.Services.Records.RecordSnapshotSerializer.Checksum(file.Data);
			_records.Setup(r => r.GetAttachmentAsync(Dept, Me, "file")).ReturnsAsync(() => file);
			_records.Setup(r => r.RecordAccessAsync(Dept, Me, RecordId, null, RmsAccessAuditAction.Read, It.IsAny<string>(), It.IsAny<string>(), RmsOriginClient.Api))
				.Callback(() =>
				{
					if (attack == "restricted") _authorization.Setup(a => a.HasPermissionAsync(Me, Dept, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
					if (attack == "view") _authorization.Setup(a => a.IsActiveMemberAsync(Me, Dept)).ReturnsAsync(false);
					if (attack == "removed") file.DeletedOn = DateTime.UtcNow;
					if (attack == "checksum") file.Checksum = "corrupt";
					if (attack == "tenant") file.DepartmentId++;
				}).Returns(Task.CompletedTask);
			var response = await _controller.GetAttachment(RecordId, "file");
			response.Result.Should().Match<ActionResult>(result => result is ForbidResult || result is NotFoundResult);
			_records.Verify(r => r.GetAttachmentAsync(Dept, Me, "file"), Times.Exactly(2));
		}

		[Test]
		public async Task Run_Call_requires_a_version_and_maps_server_identity_and_UTC_time_to_the_same_authoring_service()
		{
			var input = new CreateRunCallInput { RecordId = RecordId, Name = "Historical response", Address = "5 Test Road", Nature = "Run documentation", OccurredOnUtc = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc) };
			(await _controller.CreateRunCall(input, default)).Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(428);
			_records.Verify(r => r.CreateRunCallAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<RecordNewCallInput>(), It.IsAny<CancellationToken>()), Times.Never);
			_records.Setup(r => r.GetAsync(Dept, RecordId, It.IsAny<bool>())).ReturnsAsync(Aggregate(5, RmsDefinitionKeys.Run));
			var saved = Aggregate(6, RmsDefinitionKeys.Run); saved.Record.CallId = 77;
			_records.Setup(r => r.CreateRunCallAsync(Dept, Me, RecordId, 5, It.IsAny<RecordNewCallInput>(), It.IsAny<CancellationToken>())).ReturnsAsync(saved);
			_http.Request.Headers.IfMatch = "\"5\"";
			var result = (await _controller.CreateRunCall(input, default)).Result.Should().BeOfType<OkObjectResult>().Subject;
			result.Value.Should().BeOfType<RecordResult>().Which.Data.CallId.Should().Be(77);
			_http.Response.Headers.ETag.ToString().Should().Be("W/\"6\"");
			_records.Verify(r => r.CreateRunCallAsync(Dept, Me, RecordId, 5, It.Is<RecordNewCallInput>(c => c.Name == input.Name && c.Address == input.Address && c.Nature == input.Nature && c.OccurredOnUtc == input.OccurredOnUtc && c.OccurredOnUtc.Kind == DateTimeKind.Utc), It.IsAny<CancellationToken>()), Times.Once);
		}

		[TestCase(true)]
		[TestCase(false)]
		public async Task Run_Call_denial_or_stale_parent_never_returns_a_successful_call_binding(bool denied)
		{
			_records.Setup(r => r.GetAsync(Dept, RecordId, It.IsAny<bool>())).ReturnsAsync(Aggregate(5, RmsDefinitionKeys.Run));
			_records.Setup(r => r.CreateRunCallAsync(Dept, Me, RecordId, 5, It.IsAny<RecordNewCallInput>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(denied ? new UnauthorizedAccessException() : new RecordConcurrencyException(RecordId, 5, 6));
			var result = (await _controller.CreateRunCall(new CreateRunCallInput { RecordId = RecordId, RowVersion = 5, Name = "Historical response", OccurredOnUtc = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc) }, default)).Result;
			if (denied) result.Should().BeOfType<ForbidResult>();
			else result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(409);
			_idempotency.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Create_draft_returns_201_and_a_replayed_idempotency_key_returns_200_with_the_same_record()
		{
			RecordDraftInput captured = null;
			_records.Setup(r => r.CreateDraftAsync(Dept, Me, It.IsAny<RecordDraftInput>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string u, RecordDraftInput input, CancellationToken c) => { captured = input; return Aggregate(input.IdempotencyKey == "replay" ? 4 : 1); });
			_http.Request.Headers[RecordsApiContract.IdempotencyKeyHeader] = "k-1";

			var created = await _controller.CreateDraft(new SaveRecordDraftInput { DefinitionKey = RmsDefinitionKeys.Training, ClientRecordId = RecordId, Details = new RecordDetailsInput { Narrative = "Drill" }, OriginClient = (int)RmsOriginClient.Web }, CancellationToken.None);

			created.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status201Created);
			captured.IdempotencyKey.Should().Be("k-1", "the Idempotency-Key header is honored");
			captured.ClientRecordId.Should().Be(RecordId);
			captured.OriginClient.Should().Be(RmsOriginClient.Api, "Web is never an API origin");

			var replayed = await _controller.CreateDraft(new SaveRecordDraftInput { DefinitionKey = RmsDefinitionKeys.Training, IdempotencyKey = "replay" }, CancellationToken.None);
			replayed.Result.Should().BeOfType<OkObjectResult>();
			((RecordResult)((OkObjectResult)replayed.Result).Value).Data.RecordId.Should().Be(RecordId);
		}

		[Test]
		public async Task Field_client_below_its_flag_fails_closed_for_authoring()
		{
			var response = await _controller.CreateDraft(new SaveRecordDraftInput { DefinitionKey = RmsDefinitionKeys.Training, OriginClient = (int)RmsOriginClient.Responder }, CancellationToken.None);

			var problem = response.Result.Should().BeOfType<ObjectResult>().Which;
			problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
			((ProblemDetails)problem.Value).Type.Should().Be("field_records_disabled");
			_records.Verify(r => r.CreateDraftAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<RecordDraftInput>(), It.IsAny<CancellationToken>()), Times.Never);

			_flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.RecordsFieldResponder, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			_records.Setup(r => r.CreateDraftAsync(Dept, Me, It.IsAny<RecordDraftInput>(), It.IsAny<CancellationToken>())).ReturnsAsync(Aggregate(1));
			(await _controller.CreateDraft(new SaveRecordDraftInput { DefinitionKey = RmsDefinitionKeys.Training, OriginClient = (int)RmsOriginClient.Responder }, CancellationToken.None))
				.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status201Created);
		}

		[Test]
		public async Task Stale_save_returns_409_with_the_current_record_and_the_changed_field_paths()
		{
			_records.Setup(r => r.GetAsync(Dept, RecordId, It.IsAny<bool>())).ReturnsAsync(Aggregate(5));
			_records.Setup(r => r.SaveDraftAsync(Dept, Me, RecordId, 4, It.IsAny<RecordDraftInput>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new RecordConcurrencyException(RecordId, 4, 5));

			var response = await _controller.SaveDraft(new SaveRecordDraftInput { RecordId = RecordId, RowVersion = 4, Details = new RecordDetailsInput { Narrative = "Mine", Course = "CPR", CaseNumber = "C-1", BodyLocation = "Room 2" } }, CancellationToken.None);

			var conflict = response.Result.Should().BeOfType<ConflictObjectResult>().Which;
			var data = ((RecordConflictResult)conflict.Value).Data;
			data.ExpectedRowVersion.Should().Be(4);
			data.CurrentRowVersion.Should().Be(5);
			data.CurrentStateName.Should().Be("Draft");
			data.ChangedFieldPaths.Should().BeEquivalentTo(new[] { "Details.Narrative" });
			data.Current.Details.Narrative.Should().Be("Drill");
			_http.Response.Headers[HeaderNames.ETag].ToString().Should().Be("W/\"5\"");
		}

		[Test]
		public async Task Save_takes_the_row_version_from_If_Match_when_the_body_has_none()
		{
			_records.Setup(r => r.GetAsync(Dept, RecordId, It.IsAny<bool>())).ReturnsAsync(Aggregate(5));
			_records.Setup(r => r.SaveDraftAsync(Dept, Me, RecordId, 5, It.IsAny<RecordDraftInput>(), It.IsAny<CancellationToken>())).ReturnsAsync(Aggregate(6));
			_http.Request.Headers[HeaderNames.IfMatch] = "W/\"5\"";

			var response = await _controller.SaveDraft(new SaveRecordDraftInput { RecordId = RecordId, Details = new RecordDetailsInput { Narrative = "Drill" } }, CancellationToken.None);

			((RecordResult)((OkObjectResult)response.Result).Value).Data.RowVersion.Should().Be(6);
		}

		[Test]
		public async Task Save_without_any_precondition_is_refused()
		{
			var response = await _controller.SaveDraft(new SaveRecordDraftInput { RecordId = RecordId }, CancellationToken.None);

			response.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status428PreconditionRequired);
			_records.Verify(r => r.SaveDraftAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<RecordDraftInput>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Finalize_requires_attestation_checks_the_row_version_and_remembers_the_idempotency_key()
		{
			_records.Setup(r => r.GetAsync(Dept, RecordId, It.IsAny<bool>())).ReturnsAsync(Aggregate(5));
			_records.Setup(r => r.FinalizeAsync(Dept, Me, RecordId, 5, "1", null, null, It.IsAny<CancellationToken>())).ReturnsAsync(Aggregate(6, RmsDefinitionKeys.Training, RmsRecordState.Finalized));

			var unattested = await _controller.Finalize(new RecordCommandInput { RecordId = RecordId, RowVersion = 5 }, CancellationToken.None);
			((ProblemDetails)((ObjectResult)unattested.Result).Value).Type.Should().Be("attestation_required");
			RecordCommandReceipt receipt = null;
			_idempotency.Setup(i => i.RememberCommandAsync(Dept, Me, "fin-1", "Finalize", RecordId, It.IsAny<string>()))
				.Callback((int d, string u, string k, string c, string id, string checksum) => receipt = new RecordCommandReceipt { RecordId = id, RequestChecksum = checksum }).Returns(Task.CompletedTask);

			var finalized = await _controller.Finalize(new RecordCommandInput { RecordId = RecordId, RowVersion = 5, Attested = true, IdempotencyKey = "fin-1" }, CancellationToken.None);
			((RecordResult)((OkObjectResult)finalized.Result).Value).Data.StateName.Should().Be("Finalized");
			_idempotency.Verify(i => i.RememberCommandAsync(Dept, Me, "fin-1", "Finalize", RecordId, It.IsAny<string>()), Times.Once);

			// Replay: the remembered key short-circuits the transition.
			_idempotency.Setup(i => i.TryGetCommandAsync(Dept, Me, "fin-1", "Finalize")).ReturnsAsync(() => receipt);
			var replayed = await _controller.Finalize(new RecordCommandInput { RecordId = RecordId, RowVersion = 5, Attested = true, IdempotencyKey = "fin-1" }, CancellationToken.None);
			replayed.Result.Should().BeOfType<OkObjectResult>();
			var changed = await _controller.Finalize(new RecordCommandInput { RecordId = RecordId, RowVersion = 99, Attested = true, IdempotencyKey = "fin-1" }, CancellationToken.None);
			((ProblemDetails)((ObjectResult)changed.Result).Value).Type.Should().Be("record_idempotency_conflict");
			_records.Verify(r => r.FinalizeAsync(Dept, Me, RecordId, It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[TestCase("reason")]
		[TestCase("attestation")]
		[TestCase("owner")]
		[TestCase("record")]
		[TestCase("legacy")]
		public async Task Reused_command_keys_reject_changed_payloads_and_unbound_legacy_receipts(string changedField)
		{
			_records.Setup(r => r.GetAsync(Dept, It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync(Aggregate(5));
			_records.Setup(r => r.FinalizeAsync(Dept, Me, RecordId, 5, "1", null, null, It.IsAny<CancellationToken>())).ReturnsAsync(Aggregate(6, RmsDefinitionKeys.Training, RmsRecordState.Finalized));
			RecordCommandReceipt receipt = null;
			_idempotency.Setup(i => i.RememberCommandAsync(Dept, Me, "fin-1", "Finalize", RecordId, It.IsAny<string>()))
				.Callback((int d, string u, string k, string c, string id, string checksum) => receipt = new RecordCommandReceipt { RecordId = id, RequestChecksum = checksum }).Returns(Task.CompletedTask);
			var input = new RecordCommandInput { RecordId = RecordId, RowVersion = 5, Attested = true, IdempotencyKey = "fin-1" };
			await _controller.Finalize(input, default);
			_idempotency.Setup(i => i.TryGetCommandAsync(Dept, Me, "fin-1", "Finalize")).ReturnsAsync(() => receipt);
			if (changedField == "reason") input.ReasonText = "Different reason";
			if (changedField == "attestation") input.AttestationStatementVersion = "another-statement";
			if (changedField == "owner") input.NewOwnerUserId = "someone-else";
			if (changedField == "record") receipt.RecordId = "different-record";
			if (changedField == "legacy") receipt.RequestChecksum = null;
			var result = await _controller.Finalize(input, default);
			((ProblemDetails)((ObjectResult)result.Result).Value).Type.Should().Be("record_idempotency_conflict");
			_records.Verify(r => r.FinalizeAsync(Dept, Me, It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Command_replay_does_not_return_a_record_when_access_is_revoked_during_replay_hydration()
		{
			_records.Setup(r => r.GetAsync(Dept, RecordId, It.IsAny<bool>())).ReturnsAsync(Aggregate(5));
			_records.Setup(r => r.FinalizeAsync(Dept, Me, RecordId, 5, "1", null, null, It.IsAny<CancellationToken>())).ReturnsAsync(Aggregate(6, RmsDefinitionKeys.Training, RmsRecordState.Finalized));
			RecordCommandReceipt receipt = null;
			_idempotency.Setup(i => i.RememberCommandAsync(Dept, Me, "fin-1", "Finalize", RecordId, It.IsAny<string>()))
				.Callback((int d, string u, string k, string c, string id, string checksum) => receipt = new RecordCommandReceipt { RecordId = id, RequestChecksum = checksum }).Returns(Task.CompletedTask);
			var input = new RecordCommandInput { RecordId = RecordId, RowVersion = 5, Attested = true, IdempotencyKey = "fin-1" };
			await _controller.Finalize(input, default);
			_idempotency.Setup(i => i.TryGetCommandAsync(Dept, Me, "fin-1", "Finalize")).ReturnsAsync(() => receipt);
			_records.Setup(r => r.GetAsync(Dept, RecordId, true)).Callback(() => _authorization.Setup(a => a.CanUserViewRecordAsync(Me, RecordId, Dept)).ReturnsAsync(false)).ReturnsAsync(Aggregate(6));
			(await _controller.Finalize(input, default)).Result.Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task Losing_a_concurrent_command_reservation_never_executes_the_transition()
		{
			_records.Setup(r => r.GetAsync(Dept, RecordId, It.IsAny<bool>())).ReturnsAsync(Aggregate(5));
			_idempotency.Setup(i => i.TryReserveCommandAsync(Dept, Me, "same-key", "Finalize", RecordId, It.IsAny<string>())).ReturnsAsync(false);
			var result = await _controller.Finalize(new RecordCommandInput { RecordId = RecordId, RowVersion = 5, Attested = true, IdempotencyKey = "same-key" }, default);
			((ProblemDetails)((ObjectResult)result.Result).Value).Type.Should().Be("record_command_pending");
			_records.Verify(r => r.FinalizeAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_post_commit_receipt_failure_reports_an_unknown_outcome_and_cannot_reexecute_on_retry()
		{
			_records.Setup(r => r.GetAsync(Dept, RecordId, It.IsAny<bool>())).ReturnsAsync(Aggregate(5));
			_records.Setup(r => r.FinalizeAsync(Dept, Me, RecordId, 5, "1", null, null, It.IsAny<CancellationToken>())).ReturnsAsync(Aggregate(6, RmsDefinitionKeys.Training, RmsRecordState.Finalized));
			RecordCommandReceipt pending = null;
			_idempotency.Setup(i => i.TryGetCommandAsync(Dept, Me, "uncertain", "Finalize")).ReturnsAsync(() => pending);
			_idempotency.Setup(i => i.TryReserveCommandAsync(Dept, Me, "uncertain", "Finalize", RecordId, It.IsAny<string>()))
				.Callback((int d, string u, string k, string c, string id, string checksum) => pending = new RecordCommandReceipt { RecordId = id, RequestChecksum = checksum, IsPending = true }).ReturnsAsync(true);
			_idempotency.Setup(i => i.RememberCommandAsync(Dept, Me, "uncertain", "Finalize", RecordId, It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("receipt store unavailable"));
			var input = new RecordCommandInput { RecordId = RecordId, RowVersion = 5, Attested = true, IdempotencyKey = "uncertain" };
			var first = await _controller.Finalize(input, default);
			((ObjectResult)first.Result).StatusCode.Should().Be(503);
			((ProblemDetails)((ObjectResult)first.Result).Value).Type.Should().Be("record_command_outcome_unknown");
			var retry = await _controller.Finalize(input, default);
			((ProblemDetails)((ObjectResult)retry.Result).Value).Type.Should().Be("record_command_pending");
			_records.Verify(r => r.FinalizeAsync(Dept, Me, RecordId, 5, "1", null, null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Illegal_transition_is_a_409_problem()
		{
			_records.Setup(r => r.GetAsync(Dept, RecordId, It.IsAny<bool>())).ReturnsAsync(Aggregate(5, RmsDefinitionKeys.Training, RmsRecordState.Finalized));
			_records.Setup(r => r.CancelAsync(Dept, Me, RecordId, It.IsAny<CancellationToken>())).ThrowsAsync(new RecordTransitionException(RecordId, RmsRecordState.Finalized, RmsRecordState.Cancelled));

			var response = await _controller.Cancel(new RecordCommandInput { RecordId = RecordId }, CancellationToken.None);

			var problem = response.Result.Should().BeOfType<ObjectResult>().Which;
			problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
			((ProblemDetails)problem.Value).Type.Should().Be("record_transition");
		}

		[Test]
		public async Task Changes_returns_a_cursor_and_content_free_evictions_for_rows_outside_the_callers_scope()
		{
			var t0 = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc);
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync(Me, Dept)).ReturnsAsync(new List<int> { 1 });
			_authorization.Setup(a => a.CanUserViewRecordAsync(Me, "hidden", Dept)).ReturnsAsync(false);
			_records.Setup(r => r.GetChangesSinceAsync(Dept, It.IsAny<DateTime?>(), 3, It.IsAny<string>())).ReturnsAsync(new List<RmsRecordSearchProjection>
			{
				new RmsRecordSearchProjection { RmsRecordSearchProjectionId = "live", State = (int)RmsRecordState.Draft, ModifiedOn = t0, RecordCreatedOn = t0 },
				new RmsRecordSearchProjection { RmsRecordSearchProjectionId = "hidden", State = (int)RmsRecordState.Draft, ModifiedOn = t0.AddMinutes(1), RecordCreatedOn = t0 },
				new RmsRecordSearchProjection { RmsRecordSearchProjectionId = "gone", State = (int)RmsRecordState.Voided, ModifiedOn = t0.AddMinutes(2), RecordCreatedOn = t0 }
			});

			var response = await _controller.Changes(0, 2);

			var data = ((RecordsChangesResult)((OkObjectResult)response.Result).Value).Data;
			data.HasMore.Should().BeTrue("three rows came back for a page of two");
			data.Records.Select(r => r.RecordId).Should().BeEquivalentTo(new[] { "live", "hidden" });
			data.Records.Single(r => r.RecordId == "hidden").IsTombstone.Should().BeTrue("a previously cached live record must be evicted after loss of scope");
			data.ServerTimestampMs.Should().Be(new DateTimeOffset(t0.AddMinutes(1)).ToUnixTimeMilliseconds(), "the cursor stops at the last row of the page so nothing is skipped");

			_records.Setup(r => r.GetChangesSinceAsync(Dept, It.IsAny<DateTime?>(), 3, It.IsAny<string>())).ReturnsAsync(new List<RmsRecordSearchProjection>
			{
				new RmsRecordSearchProjection { RmsRecordSearchProjectionId = "gone", State = (int)RmsRecordState.Voided, ModifiedOn = t0.AddMinutes(2), RecordCreatedOn = t0 }
			});
			var next = ((RecordsChangesResult)((OkObjectResult)(await _controller.Changes(data.ServerTimestampMs, 2, data.ServerCursorId, data.ScopeStamp)).Result).Value).Data;
			next.HasMore.Should().BeFalse();
			next.Records.Should().ContainSingle(r => r.RecordId == "gone" && r.IsTombstone, "tombstones ride through regardless of scope");
		}

		[Test]
		public async Task Changes_preserves_sub_millisecond_cursor_precision_and_resets_old_id_only_cursors()
		{
			var exact = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
			var rows = new List<RmsRecordSearchProjection>
			{
				new RmsRecordSearchProjection { RmsRecordSearchProjectionId = "a", DepartmentId = Dept, State = 1, ModifiedOn = exact },
				new RmsRecordSearchProjection { RmsRecordSearchProjectionId = "b", DepartmentId = Dept, State = 1, ModifiedOn = exact }
			};
			_records.Setup(r => r.GetChangesSinceAsync(Dept, It.IsAny<DateTime?>(), 2, It.IsAny<string>()))
				.ReturnsAsync((int d, DateTime? time, int take, string id) => rows.Where(r => !time.HasValue || r.ModifiedOn > time || r.ModifiedOn == time && string.CompareOrdinal(r.RmsRecordSearchProjectionId, id) > 0).Take(take).ToList());
			var first = ((RecordsChangesResult)((OkObjectResult)(await _controller.Changes(0, 1)).Result).Value).Data;
			first.HasMore.Should().BeTrue(); first.ServerCursorId.Should().StartWith("rms1:");
			var next = ((RecordsChangesResult)((OkObjectResult)(await _controller.Changes(first.ServerTimestampMs, 1, first.ServerCursorId, first.ScopeStamp)).Result).Value).Data;
			next.HasMore.Should().BeFalse(); next.Records.Should().ContainSingle().Which.RecordId.Should().Be("b");
			_records.Verify(r => r.GetChangesSinceAsync(Dept, exact, 2, "a"), Times.Once);
			var legacy = ((RecordsChangesResult)((OkObjectResult)(await _controller.Changes(first.ServerTimestampMs, 1, "a", first.ScopeStamp)).Result).Value).Data;
			legacy.ResetRequired.Should().BeTrue(); legacy.Records.Should().BeEmpty(); legacy.ServerTimestampMs.Should().Be(0);
			var mismatched = ((RecordsChangesResult)((OkObjectResult)(await _controller.Changes(first.ServerTimestampMs + 1, 1, first.ServerCursorId, first.ScopeStamp)).Result).Value).Data;
			mismatched.ResetRequired.Should().BeTrue(); mismatched.Records.Should().BeEmpty();
		}

		[Test]
		public async Task List_passes_the_callers_group_scope_to_the_query()
		{
			RmsRecordQuery captured = null;
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync(Me, Dept)).ReturnsAsync(new List<int> { 1, 2 });
			_records.Setup(r => r.QueryAsync(Dept, It.IsAny<RmsRecordQuery>())).ReturnsAsync((int d, RmsRecordQuery q) => { captured = q; return new List<RmsRecordSearchProjection> { new RmsRecordSearchProjection { RmsRecordSearchProjectionId = "a", State = 1 } }; });
			_records.Setup(r => r.CountAsync(Dept, It.IsAny<RmsRecordQuery>())).ReturnsAsync(1);

			var response = await _controller.GetRecords(2026, RmsDefinitionKeys.Run, (int)RmsRecordState.Draft, null, null, null, 0, 1000);

			var result = (RecordsResult)((OkObjectResult)response.Result).Value;
			result.Data.Should().ContainSingle();
			result.Total.Should().Be(1);
			captured.VisibleGroupIds.Should().Equal(1, 2);
			captured.ViewerUserId.Should().Be(Me);
			captured.Take.Should().Be(RecordsController.MaxPageSize, "page size is capped");
		}

		[Test]
		public async Task Search_degrades_to_the_filtered_queue_when_the_host_is_off()
		{
			_records.Setup(r => r.QueryAsync(Dept, It.IsAny<RmsRecordQuery>())).ReturnsAsync(new List<RmsRecordSearchProjection>());
			_records.Setup(r => r.CountAsync(Dept, It.IsAny<RmsRecordQuery>())).ReturnsAsync(0);

			var result = (RecordsResult)((OkObjectResult)(await _controller.Search("smoke", null, null, null)).Result).Value;

			result.SearchDegraded.Should().BeTrue();
			_search.Verify(s => s.SearchAsync(It.IsAny<int>(), It.IsAny<RecordsSearchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Upload_session_errors_map_to_http_codes()
		{
			_records.Setup(r => r.GetAsync(Dept, RecordId, It.IsAny<bool>())).ReturnsAsync(Aggregate(5));
			_uploads.Setup(u => u.BeginAsync(Dept, Me, RecordId, "a.png", "image/png", 10, "x")).ThrowsAsync(new RecordUploadSessionException("checksum_mismatch", "bad hash"));
			_uploads.Setup(u => u.AppendAsync(Dept, Me, "s1", 0, It.IsAny<byte[]>())).ThrowsAsync(new RecordUploadSessionException("bad_offset", "order"));
			_uploads.Setup(u => u.CompleteAsync(Dept, Me, "s2", null, It.IsAny<CancellationToken>(), It.IsAny<int>())).ThrowsAsync(new RecordUploadSessionException("not_found", "gone"));

			((ObjectResult)(await _controller.BeginUpload(new BeginRecordUploadInput { RecordId = RecordId, FileName = "a.png", ContentType = "image/png", ByteSize = 10, Sha256 = "x" })).Result).StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
			((ObjectResult)(await _controller.UploadChunk(new RecordUploadChunkInput { UploadId = "s1", Offset = 0, Data = Convert.ToBase64String(new byte[] { 1 }) })).Result).StatusCode.Should().Be(StatusCodes.Status409Conflict);
			((ObjectResult)(await _controller.CompleteUpload(new CompleteRecordUploadInput { UploadId = "s2" }, CancellationToken.None)).Result).StatusCode.Should().Be(StatusCodes.Status404NotFound);
		}

		[Test]
		public async Task Not_activated_department_cannot_write_but_still_reads_the_manifest()
		{
			_moduleState.Activated = false;
			_moduleState.CutoverState = null;

			var manifest = ((RecordsCapabilitiesResult)((OkObjectResult)(await _controller.Capabilities()).Result).Value).Data;
			manifest.RecordsUsable.Should().BeFalse();

			var create = await _controller.CreateDraft(new SaveRecordDraftInput { DefinitionKey = RmsDefinitionKeys.Training }, CancellationToken.None);
			var problem = create.Result.Should().BeOfType<ObjectResult>().Which;
			problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
			((ProblemDetails)problem.Value).Type.Should().Be("records_not_activated");
		}
	}
}

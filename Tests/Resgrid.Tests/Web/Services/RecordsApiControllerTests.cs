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
			_authorization.Setup(a => a.CanUserViewRecordAsync(Me, It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync(Me, Dept)).ReturnsAsync((List<int>)null);

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

			var finalized = await _controller.Finalize(new RecordCommandInput { RecordId = RecordId, RowVersion = 5, Attested = true, IdempotencyKey = "fin-1" }, CancellationToken.None);
			((RecordResult)((OkObjectResult)finalized.Result).Value).Data.StateName.Should().Be("Finalized");
			_idempotency.Verify(i => i.RememberAsync(Dept, Me, "fin-1", "Finalize", RecordId), Times.Once);

			// Replay: the remembered key short-circuits the transition.
			_idempotency.Setup(i => i.TryGetRecordIdAsync(Dept, Me, "fin-1", "Finalize")).ReturnsAsync(RecordId);
			var replayed = await _controller.Finalize(new RecordCommandInput { RecordId = RecordId, RowVersion = 99, Attested = true, IdempotencyKey = "fin-1" }, CancellationToken.None);
			replayed.Result.Should().BeOfType<OkObjectResult>();
			_records.Verify(r => r.FinalizeAsync(Dept, Me, RecordId, It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
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
		public async Task Changes_returns_a_cursor_tombstones_and_drops_rows_outside_the_callers_scope()
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
			data.Records.Select(r => r.RecordId).Should().BeEquivalentTo(new[] { "live" }, "the hidden row is outside the caller's group scope");
			data.ServerTimestampMs.Should().Be(new DateTimeOffset(t0.AddMinutes(1)).ToUnixTimeMilliseconds(), "the cursor stops at the last row of the page so nothing is skipped");

			_records.Setup(r => r.GetChangesSinceAsync(Dept, It.IsAny<DateTime?>(), 3, It.IsAny<string>())).ReturnsAsync(new List<RmsRecordSearchProjection>
			{
				new RmsRecordSearchProjection { RmsRecordSearchProjectionId = "gone", State = (int)RmsRecordState.Voided, ModifiedOn = t0.AddMinutes(2), RecordCreatedOn = t0 }
			});
			var next = ((RecordsChangesResult)((OkObjectResult)(await _controller.Changes(data.ServerTimestampMs, 2)).Result).Value).Data;
			next.HasMore.Should().BeFalse();
			next.Records.Should().ContainSingle(r => r.RecordId == "gone" && r.IsTombstone, "tombstones ride through regardless of scope");
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
			_uploads.Setup(u => u.CompleteAsync(Dept, Me, "s2", null, It.IsAny<CancellationToken>())).ThrowsAsync(new RecordUploadSessionException("not_found", "gone"));

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

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
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// The v4 Field Records surface (RMS plan RMS-1D): the origin, app version and context the client sends are
	/// normalized and passed to the service, never trusted; a non-field origin is refused; and prefill outside the
	/// caller's own catalog is forbidden rather than answered.
	/// </summary>
	[TestFixture]
	public class FieldRecordsApiControllerTests
	{
		private const int Dept = 42;
		private const string Me = "responder";

		private Mock<IFieldRecordsService> _field;
		private Mock<IRecordWorkAssignmentsService> _assignments;
		private FieldRecordsController _controller;
		private DefaultHttpContext _http;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_field = new Mock<IFieldRecordsService>();
			_assignments = new Mock<IRecordWorkAssignmentsService>();
			_http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Me),
					new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()),
					new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View),
					new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Create)
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
			_activity = new Activity(nameof(FieldRecordsApiControllerTests)).Start();
			_controller = new FieldRecordsController(_field.Object, _assignments.Object) { ControllerContext = new ControllerContext { HttpContext = _http } };
		}

		[TearDown]
		public void Cleanup() => _activity?.Stop();

		[Test]
		public async Task Preflight_normalizes_the_claimed_origin_and_reads_the_app_version_header()
		{
			_http.Request.Headers["X-Resgrid-App-Version"] = "5.4.1";
			_field.Setup(f => f.PreflightAsync(Dept, Me, RmsOriginClient.Responder, "5.4.1", null))
				.ReturnsAsync(new FieldRecordPreflight { Origin = RmsOriginClient.Responder, Ok = true, AppEnabled = true, ModuleEnabled = true, RecordsUsable = true, AppVersion = "5.4.1" });

			var response = await _controller.Preflight((int)RmsOriginClient.Responder);

			var data = ((response.Result as OkObjectResult)?.Value as FieldRecordPreflightResult)?.Data;
			data.Should().NotBeNull();
			data.Ok.Should().BeTrue();
			data.OriginClient.Should().Be(RmsOriginClient.Responder.ToString());
			data.ContractVersion.Should().Be(FieldRecordCatalogV1.ContractVersion);
			_field.Verify(f => f.PreflightAsync(Dept, Me, RmsOriginClient.Responder, "5.4.1", null), Times.Once);
		}

		[Test]
		public async Task A_non_field_origin_is_normalized_to_System_so_every_field_gate_refuses_it()
		{
			_field.Setup(f => f.PreflightAsync(Dept, Me, It.IsAny<RmsOriginClient>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string u, RmsOriginClient o, string v, string c) => new FieldRecordPreflight { Origin = o, Ok = false });

			await _controller.Preflight((int)RmsOriginClient.Web);
			await _controller.Preflight(999);

			_field.Verify(f => f.PreflightAsync(Dept, Me, RmsOriginClient.System, It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2), "a claimed Web or unknown origin never becomes a field origin");
		}

		[Test]
		public async Task Catalog_passes_the_verified_context_through_and_returns_coded_exclusions()
		{
			_field.Setup(f => f.GetCatalogAsync(Dept, Me, It.IsAny<FieldRecordCatalogRequest>()))
				.ReturnsAsync((int d, string u, FieldRecordCatalogRequest r) => new FieldRecordCatalog
				{
					Origin = r.Origin, Ok = true, ContextKind = r.Context.Kind, ContextVerified = true, ScopeStamp = "scope-1",
					Definitions = { new FieldRecordCatalogEntry { DefinitionKey = "shift-log", Version = 3, Name = "Shift log" } },
					Exclusions = { new FieldRecordCatalogExclusion { DefinitionKey = "casualty", Reason = FieldRecordCatalogV1.ExclusionReasons.ProtectedDataUnavailable } }
				});

			var response = await _controller.Catalog(new FieldRecordCatalogInput
			{
				OriginClient = (int)RmsOriginClient.Unit, AppVersion = "5.4.0", ClientCapability = RecordsClientCapabilities.Packs,
				Context = new FieldRecordContextInput { UnitId = 7 }
			});

			var data = ((response.Result as OkObjectResult)?.Value as FieldRecordCatalogResult)?.Data;
			data.ContextKind.Should().Be(FieldRecordCatalogV1.LaunchContexts.Unit);
			data.Definitions.Should().ContainSingle(d => d.DefinitionKey == "shift-log");
			data.Exclusions.Should().ContainSingle(e => e.Reason == FieldRecordCatalogV1.ExclusionReasons.ProtectedDataUnavailable);
			_field.Verify(f => f.GetCatalogAsync(Dept, Me, It.Is<FieldRecordCatalogRequest>(r => r.Origin == RmsOriginClient.Unit && r.Context.UnitId == 7 && r.AppVersion == "5.4.0")), Times.Once);
		}

		[Test]
		public async Task Prefill_outside_the_callers_catalog_is_forbidden_not_answered()
		{
			_field.Setup(f => f.PrefillAsync(Dept, Me, It.IsAny<FieldRecordCatalogRequest>(), "shift-log", 3)).ThrowsAsync(new UnauthorizedAccessException());

			var refused = await _controller.Prefill(new FieldRecordPrefillInput { OriginClient = (int)RmsOriginClient.Responder, DefinitionKey = "shift-log", Version = 3 });
			refused.Result.Should().BeOfType<ForbidResult>();

			_field.Setup(f => f.PrefillAsync(Dept, Me, It.IsAny<FieldRecordCatalogRequest>(), "run-sheet", 2)).ReturnsAsync(new FieldRecordPrefill
			{
				DefinitionKey = "run-sheet", Version = 2, CallId = 501,
				Values = { new RecordValueInput { SectionKey = "main", FieldKey = "related_call", Value = "501", ReferenceType = "call", ReferenceId = "501" } },
				Provenance = { new FieldRecordPrefillProvenance { FieldKey = "related_call", Source = "call", SourceId = "501", CapturedOn = DateTime.UtcNow } }
			});

			var response = await _controller.Prefill(new FieldRecordPrefillInput { OriginClient = (int)RmsOriginClient.Responder, DefinitionKey = "run-sheet", Version = 2, Context = new FieldRecordContextInput { CallId = 501 } });
			var data = ((response.Result as OkObjectResult)?.Value as FieldRecordPrefillResult)?.Data;
			data.Values.Should().ContainSingle(v => v.FieldKey == "related_call" && v.Value == "501");
			data.Provenance.Should().ContainSingle(p => p.Source == "call");
		}

		[Test]
		public async Task Sync_maps_records_tombstones_drafts_and_assignments_and_rejects_a_bad_cursor()
		{
			(await _controller.Sync(new FieldRecordSyncInput { Since = -1 }, CancellationToken.None)).Result.Should().BeOfType<BadRequestResult>();

			_field.Setup(f => f.SyncAsync(Dept, Me, It.IsAny<FieldRecordSyncRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new FieldRecordSyncBundle
			{
				Ok = true, ScopeStamp = "scope-1", ServerTimestampMs = 1234,
				Changes = { new RmsRecordSearchProjection { RmsRecordSearchProjectionId = "r1", DepartmentId = Dept, State = (int)RmsRecordState.Finalized } },
				Tombstones = { "r2" },
				Drafts = { new RmsRecordSearchProjection { RmsRecordSearchProjectionId = "r3", DepartmentId = Dept, State = (int)RmsRecordState.Draft } },
				Assignments = { new RmsRecordWorkAssignment { RmsRecordWorkAssignmentId = "a1", RecordId = "r1", AssigneeKind = (int)RmsWorkAssigneeKind.Person, AssigneeUserId = Me, State = (int)RmsWorkAssignmentState.Open, Purpose = RmsWorkAssignmentPurposes.Complete } }
			});

			var response = await _controller.Sync(new FieldRecordSyncInput { OriginClient = (int)RmsOriginClient.Responder, Since = 0 }, CancellationToken.None);

			var data = ((response.Result as OkObjectResult)?.Value as FieldRecordSyncResult)?.Data;
			data.Records.Select(r => r.RecordId).Should().Equal(new[] { "r1" });
			data.Tombstones.Should().Equal(new[] { "r2" });
			data.Drafts.Should().ContainSingle();
			data.Assignments.Single().State.Should().Be(RmsWorkAssignmentState.Open.ToString());
			data.Assignments.Single().AssigneeKind.Should().Be(RmsWorkAssigneeKind.Person.ToString());
		}

		[Test]
		public async Task Assignment_commands_map_service_failures_onto_the_right_status_codes()
		{
			_assignments.Setup(a => a.AcknowledgeAsync(Dept, Me, "a1", It.IsAny<long?>(), It.IsAny<FieldRecordContext>(), It.IsAny<RmsOriginClient>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new RecordConcurrencyException("r1", 1, 2));
			var conflict = await _controller.AcknowledgeAssignment(new FieldRecordAssignmentCommandInput { AssignmentId = "a1", RowVersion = 1 }, CancellationToken.None);
			(conflict.Result as ObjectResult)?.StatusCode.Should().Be(StatusCodes.Status409Conflict);

			_assignments.Setup(a => a.CompleteAsync(Dept, Me, "a1", It.IsAny<long?>(), It.IsAny<FieldRecordContext>(), It.IsAny<RmsOriginClient>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new UnauthorizedAccessException());
			(await _controller.CompleteAssignment(new FieldRecordAssignmentCommandInput { AssignmentId = "a1" }, CancellationToken.None)).Result.Should().BeOfType<ForbidResult>();

			_assignments.Setup(a => a.AssignAsync(Dept, Me, It.IsAny<RecordWorkAssignmentInput>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("A reviewer is required."));
			var invalid = await _controller.Assign(new FieldRecordAssignInput { RecordId = "r1", AssigneeUserId = "x" }, CancellationToken.None);
			(invalid.Result as ObjectResult)?.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

			(await _controller.Assign(null, CancellationToken.None)).Result.Should().BeOfType<BadRequestResult>();
		}
	}
}

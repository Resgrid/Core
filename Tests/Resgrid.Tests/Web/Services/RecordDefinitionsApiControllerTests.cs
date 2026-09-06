using System;
using System.Collections.Generic;
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
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>v4 RecordDefinitions: flag gating, the aggregate/version envelopes with ETags, service exceptions mapped to HTTP codes.</summary>
	[TestFixture, NonParallelizable]
	public class RecordDefinitionsApiControllerTests
	{
		private const int Dept = 42;
		private const string Me = "admin";
		private Mock<IRecordDefinitionsService> _definitions;
		private Mock<IRecordTemplatePacksService> _templates;
		private Mock<IRecordsCutoverService> _cutover;
		private RecordDefinitionsController _controller;
		private DefaultHttpContext _http;
		private System.Diagnostics.Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_definitions = new Mock<IRecordDefinitionsService>();
			_templates = new Mock<IRecordTemplatePacksService>();
			_cutover = new Mock<IRecordsCutoverService>();
			_cutover.Setup(c => c.GetModuleStateAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState { DepartmentId = Dept, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active, LegacyWritesBlocked = true });
			_http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Me), new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()),
					new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View)
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
			_activity = new System.Diagnostics.Activity(nameof(RecordDefinitionsApiControllerTests)).Start();
			_controller = new RecordDefinitionsController(_definitions.Object, _templates.Object, _cutover.Object, Mock.Of<IRecordsPrintLayoutService>()) { ControllerContext = new ControllerContext { HttpContext = _http } };
		}

		[TearDown] public void Cleanup() => _activity?.Stop();

		private static RecordDefinitionAggregate Aggregate() => new RecordDefinitionAggregate
		{
			Definition = new RmsRecordDefinition { RmsRecordDefinitionId = "def-1", DepartmentId = Dept, DefinitionKey = "shift-log", Name = "Shift log", Owner = (int)RmsDefinitionOwner.Department, CurrentPublishedVersion = 1, LatestVersion = 2, RowVersion = 3 },
			Versions = new List<RmsRecordDefinitionVersion>
			{
				new RmsRecordDefinitionVersion { RmsRecordDefinitionVersionId = "v1", DepartmentId = Dept, DefinitionKey = "shift-log", Version = 1, State = (int)RmsDefinitionVersionState.Published, SchemaJson = RecordDefinitionSchema.Serialize(new RecordDefinitionSchema()), RowVersion = 2, MinimumClientCapability = RecordsClientCapabilities.Configurable },
				new RmsRecordDefinitionVersion { RmsRecordDefinitionVersionId = "v2", DepartmentId = Dept, DefinitionKey = "shift-log", Version = 2, State = (int)RmsDefinitionVersionState.Draft, SchemaJson = RecordDefinitionSchema.Serialize(new RecordDefinitionSchema()), RowVersion = 1 }
			}
		};

		[Test]
		public async Task Reads_are_gated_by_the_records_flag_and_carry_the_definition_etag()
		{
			_definitions.Setup(d => d.GetAsync(Dept, "shift-log")).ReturnsAsync(Aggregate());
			var ok = (await _controller.Get("shift-log")).Result.Should().BeOfType<OkObjectResult>().Subject;
			var result = ok.Value.Should().BeOfType<RecordDefinitionResult>().Subject;
			result.Data.Key.Should().Be("shift-log");
			result.Data.CurrentPublishedVersion.Should().Be(1);
			result.Data.Versions.Should().HaveCount(2);
			result.Data.ETag.Should().Be(RecordsApiContract.ToETag(3));
			_http.Response.Headers[RecordsApiContract.ETagHeader].ToString().Should().Be(RecordsApiContract.ToETag(3));

			(await _controller.Get("missing")).Result.Should().BeOfType<NotFoundResult>();
			_cutover.Setup(c => c.GetModuleStateAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState { DepartmentId = Dept, FlagEnabled = false });
			(await _controller.List()).Result.Should().BeOfType<NotFoundResult>();
			(await _controller.Get("shift-log")).Result.Should().BeOfType<NotFoundResult>("the surface disappears when the flag is off");
		}

		[Test]
		public async Task Create_returns_201_with_the_aggregate_and_maps_service_failures()
		{
			RecordDefinitionCreateInput captured = null;
			_definitions.Setup(d => d.CreateAsync(Dept, Me, It.IsAny<RecordDefinitionCreateInput>(), It.IsAny<CancellationToken>()))
				.Callback<int, string, RecordDefinitionCreateInput, CancellationToken>((d, u, i, c) => captured = i).ReturnsAsync(Aggregate());
			var created = (await _controller.Create(new CreateRecordDefinitionInput { DefinitionKey = "shift-log", Name = "Shift log", TemplateKey = "template.shift-summary", JurisdictionProfileKey = "ca", Locale = "fr-CA" }, CancellationToken.None)).Result
				.Should().BeOfType<ObjectResult>().Subject;
			created.StatusCode.Should().Be(StatusCodes.Status201Created);
			captured.TemplateKey.Should().Be("template.shift-summary"); captured.JurisdictionProfileKey.Should().Be("ca"); captured.Locale.Should().Be("fr-CA");
			created.Value.Should().BeOfType<RecordDefinitionResult>().Which.Data.Key.Should().Be("shift-log");

			(await _controller.Create(null, CancellationToken.None)).Result.Should().BeOfType<BadRequestResult>();
			_definitions.Setup(d => d.CreateAsync(Dept, Me, It.IsAny<RecordDefinitionCreateInput>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("A definition with key 'shift-log' already exists."));
			var conflict = (await _controller.Create(new CreateRecordDefinitionInput { DefinitionKey = "shift-log", Name = "x" }, CancellationToken.None)).Result.Should().BeOfType<ObjectResult>().Subject;
			conflict.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
			_definitions.Setup(d => d.CreateAsync(Dept, Me, It.IsAny<RecordDefinitionCreateInput>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException("no"));
			(await _controller.Create(new CreateRecordDefinitionInput { DefinitionKey = "shift-log", Name = "x" }, CancellationToken.None)).Result.Should().BeOfType<ForbidResult>();
		}

		[Test]
		public async Task Publish_uses_the_if_match_header_and_reports_concurrency_as_409()
		{
			_http.Request.Headers[RecordsApiContract.IfMatchHeader] = RecordsApiContract.ToETag(1);
			_definitions.Setup(d => d.PublishAsync(Dept, Me, "shift-log", 2, 1, It.IsAny<CancellationToken>())).ReturnsAsync(Aggregate().Versions[1]);
			var ok = (await _controller.Publish("shift-log", 2, null, CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>().Subject;
			ok.Value.Should().BeOfType<RecordDefinitionVersionResult>().Which.Data.Version.Should().Be(2);

			_definitions.Setup(d => d.PublishAsync(Dept, Me, "shift-log", 2, 1, It.IsAny<CancellationToken>())).ThrowsAsync(new RecordConcurrencyException("v2", 1, 4));
			var stale = (await _controller.Publish("shift-log", 2, null, CancellationToken.None)).Result.Should().BeOfType<ObjectResult>().Subject;
			stale.StatusCode.Should().Be(StatusCodes.Status409Conflict);
			_definitions.Setup(d => d.PublishAsync(Dept, Me, "shift-log", 2, 1, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Only a draft version can be published."));
			(await _controller.Publish("shift-log", 2, null, CancellationToken.None)).Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
		}

		[Test]
		public async Task Templates_and_profiles_come_from_the_catalog_service()
		{
			_templates.Setup(t => t.GetCatalogAsync()).ReturnsAsync(new List<RecordTemplatePackSummary> { new RecordTemplatePackSummary { PackKey = "pack.sar", Name = "SAR" } });
			var ok = (await _controller.Templates()).Result.Should().BeOfType<OkObjectResult>().Subject;
			ok.Value.Should().BeOfType<RecordTemplatePacksResult>().Which.Data.Should().ContainSingle(p => p.PackKey == "pack.sar");
		}
	}
}

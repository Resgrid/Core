using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.v4;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;

namespace Resgrid.Tests.Services
{
	/// <summary>Actual authenticated HTTP binding and response handling around the Records/Inventory domain boundary.</summary>
	[TestFixture, NonParallelizable]
	public sealed class RecordInventoryM3HttpTests
	{
		private const int Department = 77;
		private const string Route = "/api/v4/RecordInventory/";
		private const string RecordId = "11111111-1111-1111-1111-111111111111";
		private const string ItemId = "22222222-2222-2222-2222-222222222222";
		private const string UsageId = "33333333-3333-3333-3333-333333333333";
		private const string TransactionId = "44444444-4444-4444-4444-444444444444";
		private const string LocationId = "55555555-5555-5555-5555-555555555555";
		private const string Grant = "synthetic-current-inventory-grant";
		private const string Canary = "SYNTHETIC-RECORD-INVENTORY-PHI-CANARY";
		private Mock<IRmsInventoryUsageAdapter> _usage;
		private Mock<IRecordsEvidenceService> _evidence;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<IRecordsCutoverService> _cutover;
		private List<(InventoryActor Actor, string RecordId, RmsRecordKind Kind, long Version, RecordInventoryUsageRequest Request)> _posted;
		private List<(InventoryActor Actor, string RecordId, RmsRecordKind Kind, long Version, RecordInventoryUsageCorrection Correction)> _reversed;

		[SetUp]
		public void SetUp()
		{
			_usage = new(); _evidence = new(); _authorization = new(); _cutover = new(); _posted = new(); _reversed = new();
			_cutover.Setup(x => x.GetModuleStateAsync(Department, false)).ReturnsAsync(new RecordsModuleState { FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active });
			_authorization.Setup(x => x.CanUserViewRecordAsync("officer", RecordId, Department)).ReturnsAsync(true);
			_authorization.Setup(x => x.HasPermissionAsync("officer", Department, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(true);
			_authorization.Setup(x => x.CanUseSourceInventoryAsync("officer", Department, null)).ReturnsAsync(true);
			_usage.Setup(x => x.RecordModernUsageAsync(It.IsAny<InventoryActor>(), It.IsAny<string>(), It.IsAny<RmsRecordKind>(), It.IsAny<long>(), It.IsAny<RecordInventoryUsageRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((InventoryActor actor, string record, RmsRecordKind kind, long version, RecordInventoryUsageRequest request, CancellationToken ct) =>
				{
					_posted.Add((Copy(actor), record, kind, version, Copy(request)));
					return new List<RmsInventoryUsage> { Usage() };
				});
			_usage.Setup(x => x.ReverseModernUsageAsync(It.IsAny<InventoryActor>(), It.IsAny<string>(), It.IsAny<RmsRecordKind>(), It.IsAny<long>(), It.IsAny<RecordInventoryUsageCorrection>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((InventoryActor actor, string record, RmsRecordKind kind, long version, RecordInventoryUsageCorrection correction, CancellationToken ct) =>
				{
					_reversed.Add((Copy(actor), record, kind, version, Copy(correction)));
					return Usage(true);
				});
			_usage.Setup(x => x.GetAuthorizedUsageAsync(It.IsAny<InventoryActor>(), RecordId, It.IsAny<RmsRecordKind>())).ReturnsAsync(new List<RmsInventoryUsage> { Usage(), Usage(true) });
			_evidence.Setup(x => x.CaptureAsync(It.IsAny<RecordEvidenceCaptureRequest>(), true, It.IsAny<CancellationToken>())).ReturnsAsync(new RmsEvidenceArtifact { RmsEvidenceArtifactId = "synthetic-evidence" });
		}

		[Test]
		public async Task Routes_require_authentication_and_the_Record_create_policy_before_domain_calls()
		{
			await WithServer(async client =>
			{
				foreach (var action in new[] { "RecordUsage", "ReverseUsage" })
					(await client.PostAsync(Route + action, Json(Input(action)))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
				SignIn(client, canCreate: false);
				foreach (var action in new[] { "RecordUsage", "ReverseUsage" })
					(await client.PostAsync(Route + action, Json(Input(action)))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
				_usage.Invocations.Should().BeEmpty(); _evidence.Invocations.Should().BeEmpty();
			});
		}

		[Test]
		public async Task Typed_usage_takes_actor_and_grant_from_authenticated_context_and_preserves_client_request_identity()
		{
			await WithServer(async client =>
			{
				SignIn(client); client.DefaultRequestHeaders.Add(DataProtectionController.GrantHeader, Grant);
				var requestId = Guid.NewGuid().ToString("D");
				var response = await client.PostAsync(Route + "RecordUsage?departmentId=88&userId=forged", Json(new
				{
					RecordId, Kind = (int)RmsRecordKind.IncidentReport, ExpectedRowVersion = 9, DepartmentId = 88, UserId = "forged", GrantToken = "forged-grant",
					Actor = new { DepartmentId = 88, UserId = "forged", GrantToken = "forged-grant" },
					Request = new { RequestId = requestId, Lines = new[] { new { ItemId, LocationId, Quantity = 2.125001m, UsageType = (int)InventoryUsageType.LeftAtScene, Note = Canary } } }
				}));
				var json = await Created(response); var posting = _posted.Single();
				posting.Actor.Should().BeEquivalentTo(new InventoryActor { DepartmentId = Department, UserId = "officer", GrantToken = Grant });
				posting.RecordId.Should().Be(RecordId); posting.Kind.Should().Be(RmsRecordKind.IncidentReport); posting.Version.Should().Be(9);
				posting.Request.RequestId.Should().Be(requestId); var line = posting.Request.Lines.Single();
				line.ItemId.Should().Be(ItemId); line.LocationId.Should().Be(LocationId); line.Quantity.Should().Be(2.125001m); line.UsageType.Should().Be(InventoryUsageType.LeftAtScene); line.Note.Should().Be(Canary);
				json["usage"].Single().Value<string>("UsageId").Should().Be(UsageId);
				json.Value<bool>("evidenceCaptureRequired").Should().BeFalse();
				_evidence.Verify(x => x.CaptureAsync(It.Is<RecordEvidenceCaptureRequest>(r => r.DepartmentId == Department && r.RecordId == RecordId && r.RecordKind == RmsRecordKind.IncidentReport && r.CapturedByUserId == "officer" && r.Kind == RmsEvidenceKind.InventoryUsage && r.OriginClient == RmsOriginClient.Api), true, It.IsAny<CancellationToken>()), Times.Once);
				(await response.Content.ReadAsStringAsync()).Should().NotContain(Grant).And.NotContain("forged-grant").And.NotContain(Canary);
			});
		}

		[Test]
		public async Task Correction_carries_witnessed_transaction_and_reason_and_returns_signed_usage()
		{
			await WithServer(async client =>
			{
				SignIn(client); client.DefaultRequestHeaders.Add(DataProtectionController.GrantHeader, Grant);
				var requestId = Guid.NewGuid().ToString("D");
				var response = await client.PostAsync(Route + "ReverseUsage", Json(new { RecordId, Kind = 1, ExpectedRowVersion = 12,
					Correction = new { RequestId = requestId, UsageId, ExistingTransactionId = TransactionId, Reason = Canary, DepartmentId = 88, UserId = "forged" } }));
				var json = await Created(response); var reversal = _reversed.Single();
				reversal.Actor.Should().BeEquivalentTo(new InventoryActor { DepartmentId = Department, UserId = "officer", GrantToken = Grant });
				reversal.RecordId.Should().Be(RecordId); reversal.Kind.Should().Be(RmsRecordKind.Operational); reversal.Version.Should().Be(12);
				reversal.Correction.RequestId.Should().Be(requestId); reversal.Correction.UsageId.Should().Be(UsageId); reversal.Correction.ExistingTransactionId.Should().Be(TransactionId); reversal.Correction.Reason.Should().Be(Canary);
				json["usage"].Value<decimal>("Quantity").Should().Be(-2.125001m); json["usage"].Value<string>("ReversesUsageId").Should().Be(UsageId);
				_posted.Should().BeEmpty(); (await response.Content.ReadAsStringAsync()).Should().NotContain(Canary).And.NotContain(Grant);
			});
		}

		[Test]
		public async Task Existing_consumption_attachment_is_bound_without_inventing_fresh_stock_fields()
		{
			await WithServer(async client =>
			{
				SignIn(client);
				await Created(await client.PostAsync(Route + "RecordUsage", Json(new { RecordId, Kind = 1, ExpectedRowVersion = 3,
					Request = new { RequestId = Guid.NewGuid().ToString("D"), Lines = new[] { new { ExistingTransactionId = TransactionId, UsageType = (int)InventoryUsageType.ConsumedOnPatient } } } })));
				var line = _posted.Single().Request.Lines.Single(); line.ExistingTransactionId.Should().Be(TransactionId); line.UsageType.Should().Be(InventoryUsageType.ConsumedOnPatient);
				line.Quantity.Should().Be(0); line.ItemId.Should().BeNull(); line.LocationId.Should().BeNull(); line.Note.Should().BeNull();
			});
		}

		[TestCase("RecordUsage")]
		[TestCase("ReverseUsage")]
		public async Task Evidence_failure_returns_created_with_refresh_required_and_never_repeats_the_inventory_action(string action)
		{
			_evidence.Setup(x => x.CaptureAsync(It.IsAny<RecordEvidenceCaptureRequest>(), true, It.IsAny<CancellationToken>())).ThrowsAsync(new InventoryException(403, "ProtectedDataRequired"));
			await WithServer(async client =>
			{
				SignIn(client); var response = await client.PostAsync(Route + action, Json(Input(action))); var json = await Created(response);
				json.Value<bool>("evidenceCaptureRequired").Should().BeTrue(); json["evidenceId"].Type.Should().Be(JTokenType.Null);
				(_posted.Count + _reversed.Count).Should().Be(1);
				_usage.Invocations.Count(x => x.Method.Name is nameof(IRmsInventoryUsageAdapter.RecordModernUsageAsync) or nameof(IRmsInventoryUsageAdapter.ReverseModernUsageAsync)).Should().Be(1);
				_usage.Invocations.Should().NotContain(x => x.Method.Name == nameof(IRmsInventoryUsageAdapter.ConsumeAsync) || x.Method.Name == nameof(IRmsInventoryUsageAdapter.ConsumeModernAsync));
				_evidence.Verify(x => x.CaptureAsync(It.IsAny<RecordEvidenceCaptureRequest>(), true, It.IsAny<CancellationToken>()), Times.Once);
			});
		}

		[TestCase("RecordUsage")]
		[TestCase("ReverseUsage")]
		public async Task Domain_concurrency_protection_and_missing_source_failures_keep_their_status_without_leaking_content(string action)
		{
			await WithServer(async client =>
			{
				SignIn(client);
				foreach (var failure in new (Exception Error, HttpStatusCode Status, string Code)[] {
					(new RecordConcurrencyException(RecordId, 3, 4), HttpStatusCode.Conflict, null),
					(new InventoryException(403, "ProtectedDataRequired"), HttpStatusCode.Forbidden, "protected_data_required"),
					(new InventoryException(404, "UsageUnavailable"), HttpStatusCode.NotFound, "UsageUnavailable"),
					(new InventoryException(409, "UsageAlreadyReversed"), HttpStatusCode.Conflict, "UsageAlreadyReversed"),
					(new UnauthorizedAccessException(Canary), HttpStatusCode.Forbidden, null),
					(new InvalidOperationException(Canary), HttpStatusCode.BadRequest, null) })
				{
					if (action == "RecordUsage") _usage.Setup(x => x.RecordModernUsageAsync(It.IsAny<InventoryActor>(), It.IsAny<string>(), It.IsAny<RmsRecordKind>(), It.IsAny<long>(), It.IsAny<RecordInventoryUsageRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure.Error);
					else _usage.Setup(x => x.ReverseModernUsageAsync(It.IsAny<InventoryActor>(), It.IsAny<string>(), It.IsAny<RmsRecordKind>(), It.IsAny<long>(), It.IsAny<RecordInventoryUsageCorrection>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure.Error);
					var response = await client.PostAsync(Route + action, Json(Input(action))); var body = await response.Content.ReadAsStringAsync();
					response.StatusCode.Should().Be(failure.Status, body); response.Headers.CacheControl.NoStore.Should().BeTrue(); body.Should().NotContain(Canary).And.NotContain(Grant);
					if (failure.Code != null) body.Should().Contain(failure.Code);
				}
				_evidence.Invocations.Should().BeEmpty();
			});
		}

		[Test]
		public async Task Missing_request_id_is_not_replaced_with_a_generated_id_at_the_HTTP_boundary()
		{
			RecordInventoryUsageRequest posted = null; RecordInventoryUsageCorrection reversed = null;
			_usage.Setup(x => x.RecordModernUsageAsync(It.IsAny<InventoryActor>(), It.IsAny<string>(), It.IsAny<RmsRecordKind>(), It.IsAny<long>(), It.IsAny<RecordInventoryUsageRequest>(), It.IsAny<CancellationToken>()))
				.Callback<InventoryActor, string, RmsRecordKind, long, RecordInventoryUsageRequest, CancellationToken>((a, r, k, v, request, ct) => posted = request).ThrowsAsync(new ArgumentException(Canary));
			_usage.Setup(x => x.ReverseModernUsageAsync(It.IsAny<InventoryActor>(), It.IsAny<string>(), It.IsAny<RmsRecordKind>(), It.IsAny<long>(), It.IsAny<RecordInventoryUsageCorrection>(), It.IsAny<CancellationToken>()))
				.Callback<InventoryActor, string, RmsRecordKind, long, RecordInventoryUsageCorrection, CancellationToken>((a, r, k, v, correction, ct) => reversed = correction).ThrowsAsync(new ArgumentException(Canary));
			await WithServer(async client =>
			{
				SignIn(client);
				foreach (var requestId in new[] { (string)null, Guid.Empty.ToString("D"), "not-a-guid" })
				{
					var response = await client.PostAsync(Route + "RecordUsage", Json(new { RecordId, Kind = 1, ExpectedRowVersion = 3, Request = new { RequestId = requestId, Lines = new[] { new { ItemId, Quantity = 1 } } } }));
					response.StatusCode.Should().Be(HttpStatusCode.BadRequest); posted.RequestId.Should().Be(requestId); (await response.Content.ReadAsStringAsync()).Should().NotContain(Canary);
					response = await client.PostAsync(Route + "ReverseUsage", Json(new { RecordId, Kind = 1, ExpectedRowVersion = 3, Correction = new { RequestId = requestId, UsageId, Reason = "Correction" } }));
					response.StatusCode.Should().Be(HttpStatusCode.BadRequest); reversed.RequestId.Should().Be(requestId);
				}
				_evidence.Invocations.Should().BeEmpty();
			});
		}

		[Test]
		public async Task Unknown_or_inactive_source_is_hidden_before_any_usage_or_evidence_call()
		{
			_authorization.Setup(x => x.CanUserViewRecordAsync("officer", RecordId, Department)).ReturnsAsync(false);
			await WithServer(async client =>
			{
				SignIn(client);
				foreach (var action in new[] { "RecordUsage", "ReverseUsage" }) (await client.PostAsync(Route + action, Json(Input(action)))).StatusCode.Should().Be(HttpStatusCode.NotFound);
				_authorization.Setup(x => x.CanUserViewRecordAsync("officer", RecordId, Department)).ReturnsAsync(true);
				_cutover.Setup(x => x.GetModuleStateAsync(Department, false)).ReturnsAsync(new RecordsModuleState());
				(await client.GetAsync(Route + "Usage?recordId=" + RecordId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
				_usage.Invocations.Should().BeEmpty(); _evidence.Invocations.Should().BeEmpty();
			});
		}

		[Test]
		public async Task Usage_reads_use_current_context_and_authorization_revocation_after_post_hides_the_receipt()
		{
			await WithServer(async client =>
			{
				SignIn(client); client.DefaultRequestHeaders.Add(DataProtectionController.GrantHeader, Grant);
				var response = await client.GetAsync(Route + "Usage?recordId=" + RecordId + "&kind=2&departmentId=88&grantToken=forged-grant");
				response.StatusCode.Should().Be(HttpStatusCode.OK); response.Headers.CacheControl.NoStore.Should().BeTrue();
				_usage.Verify(x => x.GetAuthorizedUsageAsync(It.Is<InventoryActor>(a => a.DepartmentId == Department && a.UserId == "officer" && a.GrantToken == Grant), RecordId, RmsRecordKind.IncidentReport), Times.Once);
				_authorization.SetupSequence(x => x.CanUserViewRecordAsync("officer", RecordId, Department)).ReturnsAsync(true).ReturnsAsync(false);
				response = await client.PostAsync(Route + "RecordUsage", Json(Input("RecordUsage")));
				response.StatusCode.Should().Be(HttpStatusCode.Forbidden); _posted.Should().HaveCount(1); (await response.Content.ReadAsStringAsync()).Should().NotContain(UsageId);
			});
		}

		private static RmsInventoryUsage Usage(bool correction = false) => new() { ReferenceId = correction ? TransactionId : UsageId, UsageId = correction ? TransactionId : UsageId, RecordId = RecordId,
			TransactionId = TransactionId, ItemId = ItemId, Quantity = correction ? -2.125001m : 2.125001m, UsageType = InventoryUsageType.LeftAtScene, ReversesUsageId = correction ? UsageId : null,
			ItemName = ProtectedDataEnvelope.RedactionValue, Note = ProtectedDataEnvelope.RedactionValue, UnitOfMeasure = ProtectedDataEnvelope.RedactionValue };
		private static object Input(string action) => action == "RecordUsage"
			? new { RecordId, Kind = 1, ExpectedRowVersion = 3, Request = new RecordInventoryUsageRequest { RequestId = Guid.NewGuid().ToString("D"), Lines = new() { new() { ItemId = ItemId, LocationId = LocationId, Quantity = 2.125001m, UsageType = InventoryUsageType.LeftAtScene } } } }
			: new { RecordId, Kind = 1, ExpectedRowVersion = 3, Correction = new RecordInventoryUsageCorrection { RequestId = Guid.NewGuid().ToString("D"), UsageId = UsageId, Reason = "Synthetic correction" } };
		private static StringContent Json(object value) => new(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");
		private static T Copy<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
		private static void SignIn(HttpClient client, bool canCreate = true) { client.DefaultRequestHeaders.Add("Test-Member", "officer"); if (canCreate) client.DefaultRequestHeaders.Add("Test-Record-Create", "true"); }
		private static async Task<JObject> Created(HttpResponseMessage response)
		{
			var body = await response.Content.ReadAsStringAsync(); response.StatusCode.Should().Be(HttpStatusCode.Created, body); response.Headers.CacheControl.NoStore.Should().BeTrue(); return JObject.Parse(body);
		}
		private async Task WithServer(Func<HttpClient, Task> test)
		{
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Services.AddHttpContextAccessor(); builder.Services.AddApiVersioning();
			const string scheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
			builder.Services.AddAuthentication(scheme).AddScheme<AuthenticationSchemeOptions, TestAuthentication>(scheme, _ => { });
			builder.Services.AddAuthorization(options => options.AddPolicy(ResgridResources.Record_Create, policy => policy.RequireClaim("records-create", "true")));
			builder.Services.AddControllers().AddApplicationPart(typeof(RecordInventoryController).Assembly).AddNewtonsoftJson(o => o.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver());
			builder.Services.AddSingleton(_usage.Object); builder.Services.AddSingleton(_evidence.Object); builder.Services.AddSingleton(_authorization.Object); builder.Services.AddSingleton(_cutover.Object);
			await using var app = builder.Build(); var previous = ApiClaims._httpContextAccessor; ApiClaims._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
			try { await app.StartAsync(); using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) }; await test(client); }
			finally { await app.StopAsync(); ApiClaims._httpContextAccessor = previous; }
		}
		private sealed class TestAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
		{
			public TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }
			protected override Task<AuthenticateResult> HandleAuthenticateAsync()
			{
				if (!Request.Headers.TryGetValue("Test-Member", out var member)) return Task.FromResult(AuthenticateResult.NoResult());
				var claims = new List<Claim> { new(ClaimTypes.PrimarySid, member.ToString()), new(ClaimTypes.PrimaryGroupSid, Department.ToString()) };
				if (Request.Headers["Test-Record-Create"] == "true") claims.Add(new Claim("records-create", "true"));
				return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
			}
		}
	}
}

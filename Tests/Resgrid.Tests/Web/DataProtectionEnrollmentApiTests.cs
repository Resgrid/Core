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
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using DataProtectionController = Resgrid.Web.Services.Controllers.v4.DataProtectionController;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// v4 ADP enrollment over HTTP with the real DepartmentDataProtectionService: an acknowledgement record that misses a
	/// current section 12 item, or was made for an older version, is refused with acknowledgements_incomplete and nothing
	/// is written. The record a client builds from Capabilities is accepted.
	/// </summary>
	[TestFixture, NonParallelizable]
	public sealed class DataProtectionEnrollmentApiTests
	{
		private const int Department = 77;
		private const string ManagingMember = "managing-member";
		private const string MfaGrant = "synthetic-mfa-grant";
		private const string Route = "/api/v4/DataProtection/";

		private Mock<IDepartmentDataProtectionPolicyRepository> _policies;
		private Mock<IDepartmentsService> _departments;
		private Mock<IFeatureToggleService> _flags;
		private Mock<IProtectedDataGrantService> _grants;
		private Mock<ICacheProvider> _cache;
		private DepartmentDataProtectionService _service;
		private DepartmentDataProtectionPolicy _inserted;

		[SetUp]
		public void SetUp()
		{
			_inserted = null;
			_policies = new Mock<IDepartmentDataProtectionPolicyRepository>();
			_policies.Setup(x => x.GetByDepartmentIdAsync(Department)).ReturnsAsync(() => _inserted);
			_policies.Setup(x => x.InsertAsync(It.IsAny<DepartmentDataProtectionPolicy>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Returns<DepartmentDataProtectionPolicy, CancellationToken, bool>((policy, _, _) => Task.FromResult(_inserted = policy));

			// Everything else a department needs to enroll: managing member, paid plan, ADP add-on, open gate, a time zone.
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentByIdAsync(Department, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = Department, ManagingUserId = ManagingMember, TimeZone = "UTC" });
			var subscriptions = new Mock<ISubscriptionsService>();
			subscriptions.Setup(x => x.GetCurrentPlanForDepartmentAsync(Department, It.IsAny<bool>())).ReturnsAsync(new Plan { PlanId = 5, Cost = 500 });
			subscriptions.Setup(x => x.GetCurrentPlanAddonsForDepartmentFromStripeAsync(Department))
				.ReturnsAsync(new List<PlanAddon> { new PlanAddon { AddonType = (int)PlanAddonTypes.ADP } });
			_flags = new Mock<IFeatureToggleService>();
			_flags.Setup(x => x.GetFlagByKeyAsync(FeatureFlagKeys.DepartmentProtectedDataEnrollment, It.IsAny<bool>()))
				.ReturnsAsync(new FeatureFlag { FlagKey = FeatureFlagKeys.DepartmentProtectedDataEnrollment, IsEnabledGlobally = true });

			_cache = new Mock<ICacheProvider>();
			_cache.Setup(x => x.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<DepartmentDataProtectionPolicy>>>(), It.IsAny<TimeSpan>()))
				.Returns<string, Func<Task<DepartmentDataProtectionPolicy>>, TimeSpan>((_, fallback, _) => fallback());
			_cache.Setup(x => x.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<DepartmentProtectedDataEgressPolicy>>>(), It.IsAny<TimeSpan>()))
				.Returns<string, Func<Task<DepartmentProtectedDataEgressPolicy>>, TimeSpan>((_, fallback, _) => fallback());
			_cache.Setup(x => x.RemoveAsync(It.IsAny<string>())).ReturnsAsync(true);

			// A valid, recent-MFA grant for the managing member, so the command gets past RequireRecentMfaAsync.
			_grants = new Mock<IProtectedDataGrantService>();
			_grants.SetupGet(x => x.CanValidateGrants).Returns(true);
			var grant = new ProtectedDataGrant { GrantId = "grant-1", UserId = ManagingMember, DepartmentId = Department, MfaAtUtc = DateTime.UtcNow };
			_grants.Setup(x => x.ValidateGrant(MfaGrant, Department, 0, null, out grant, It.IsAny<DateTime?>()))
				.Returns(ProtectedDataGrantValidationOutcome.Valid);

			_service = new DepartmentDataProtectionService(_policies.Object, new Mock<IDepartmentProtectedDataEgressPolicyRepository>().Object,
				_departments.Object, _flags.Object, subscriptions.Object, _cache.Object, new ProtectedFieldCatalog(),
				new Mock<IDepartmentDataProtectionMigrationRepository>().Object, new Mock<IDepartmentLockService>().Object,
				new Mock<IDepartmentKeyService>().Object);
		}

		private static string Record(string version, IEnumerable<string> items, bool lockConsent = true) =>
			JsonConvert.SerializeObject(new { version, acknowledgedItems = items, lockConsent });

		private static StringContent Queue(string acknowledgementsJson) => new StringContent(JsonConvert.SerializeObject(new
		{
			AcknowledgementsJson = acknowledgementsJson, WindowStartLocal = "22:00", WindowEndLocal = "06:00", WindowTimeZone = "UTC"
		}), Encoding.UTF8, "application/json");

		private static async Task<JObject> Body(HttpResponseMessage response) => JObject.Parse(await response.Content.ReadAsStringAsync());

		[Test]
		public async Task Queue_enrollment_without_the_own_ai_provider_acknowledgement_is_refused_with_acknowledgements_incomplete()
		{
			await WithServer(async client =>
			{
				var response = await client.PostAsync(Route + "QueueEnrollment",
					Queue(Record(AdpEnrollmentAcknowledgements.Version, AdpEnrollmentAcknowledgements.Items.Where(i => i != "own_ai_provider"))));

				response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
				var problem = await Body(response);
				problem.Value<string>("type").Should().Be("acknowledgements_incomplete");
				problem.Value<int>("status").Should().Be(400);
				_inserted.Should().BeNull("nothing is written when the record is incomplete");
				_policies.Verify(x => x.InsertAsync(It.IsAny<DepartmentDataProtectionPolicy>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			});
		}

		[TestCase("ADP-ACK-1", true)]
		[TestCase(AdpEnrollmentAcknowledgements.Version, false)]
		public async Task Queue_enrollment_with_an_older_version_or_without_lock_consent_is_refused(string version, bool lockConsent)
		{
			await WithServer(async client =>
			{
				var response = await client.PostAsync(Route + "QueueEnrollment",
					Queue(Record(version, AdpEnrollmentAcknowledgements.Items, lockConsent)));

				response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
				(await Body(response)).Value<string>("type").Should().Be("acknowledgements_incomplete");
				_inserted.Should().BeNull();
			});
		}

		[Test]
		public async Task A_record_built_from_capabilities_queues_the_enrollment()
		{
			await WithServer(async client =>
			{
				var capabilities = await client.GetAsync(Route + "Capabilities");
				capabilities.StatusCode.Should().Be(HttpStatusCode.OK);
				var data = (await Body(capabilities))["Data"];
				var version = data.Value<string>("AcknowledgementVersion");
				var items = data["AcknowledgementItems"].Values<string>().ToList();
				version.Should().Be(AdpEnrollmentAcknowledgements.Version);
				items.Should().Equal(AdpEnrollmentAcknowledgements.Items).And.Contain("own_ai_provider");

				var response = await client.PostAsync(Route + "QueueEnrollment", Queue(Record(version, items)));

				response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
				var queued = await Body(response);
				queued.Value<string>("Outcome").Should().Be(nameof(DepartmentDataProtectionEnrollmentResult.Queued));
				queued.Value<int>("State").Should().Be((int)DepartmentDataProtectionState.EnrollmentQueued);
				_inserted.State.Should().Be((int)DepartmentDataProtectionState.EnrollmentQueued);
				AdpEnrollmentAcknowledgements.IsComplete(_inserted.AcknowledgementsJson).Should().BeTrue();
			});
		}

		private async Task WithServer(Func<HttpClient, Task> test)
		{
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Services.AddHttpContextAccessor(); builder.Services.AddApiVersioning();
			const string scheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
			builder.Services.AddAuthentication(scheme).AddScheme<AuthenticationSchemeOptions, TestAuthentication>(scheme, _ => { });
			builder.Services.AddAuthorization();
			builder.Services.AddControllers().AddApplicationPart(typeof(DataProtectionController).Assembly)
				.AddNewtonsoftJson(o => o.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver());

			builder.Services.AddSingleton<IDepartmentDataProtectionService>(_service);
			builder.Services.AddSingleton(new Mock<IDepartmentLockService>().Object);
			builder.Services.AddSingleton<IProtectedFieldCatalog>(new ProtectedFieldCatalog());
			builder.Services.AddSingleton(_departments.Object);
			builder.Services.AddSingleton(_flags.Object);
			builder.Services.AddSingleton(new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null).Object);
			builder.Services.AddSingleton(_cache.Object);
			builder.Services.AddSingleton(_grants.Object);
			builder.Services.AddSingleton(new Mock<IAdpReleaseService>().Object);
			builder.Services.AddSingleton(new Mock<IAdpAuditRepository>().Object);

			await using var app = builder.Build(); var previous = ApiClaims._httpContextAccessor; ApiClaims._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
			try
			{
				await app.StartAsync();
				using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
				client.DefaultRequestHeaders.Add("Test-Member", ManagingMember);
				client.DefaultRequestHeaders.Add(DataProtectionController.GrantHeader, MfaGrant);
				await test(client);
			}
			finally { await app.StopAsync(); ApiClaims._httpContextAccessor = previous; }
		}

		private sealed class TestAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
		{
			public TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

			protected override Task<AuthenticateResult> HandleAuthenticateAsync()
			{
				if (!Request.Headers.TryGetValue("Test-Member", out var member))
					return Task.FromResult(AuthenticateResult.NoResult());
				var claims = new List<Claim> { new(ClaimTypes.PrimarySid, member.ToString()), new(ClaimTypes.PrimaryGroupSid, Department.ToString()) };
				return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
			}
		}
	}
}

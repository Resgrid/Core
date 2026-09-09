using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistWorkflowTests
	{
		[Test, NonParallelizable]
		public async Task P1M4_HTTP_reports_require_authentication_UTC_ranges_and_current_ADP_without_caching_or_raw_errors()
		{
			await SeedReportMonth(); PacketService(null);
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization();
			const string scheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
			builder.Services.AddAuthentication(scheme).AddScheme<AuthenticationSchemeOptions, ChecklistTestAuthentication>(scheme, _ => { }); builder.Services.AddAuthorization(); builder.Services.AddApiVersioning();
			builder.Services.AddControllers().AddApplicationPart(typeof(ChecklistRunsController).Assembly).AddNewtonsoftJson(o => o.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver());
			builder.Services.AddSingleton<IChecklistsService>(_service); builder.Services.AddSingleton(_access.Object); builder.Services.AddSingleton(Mock.Of<IDepartmentDataProtectionService>());
			await using var app = builder.Build(); var previous = ApiClaims._httpContextAccessor; ApiClaims._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("fr").AddSupportedCultures("fr").AddSupportedUICultures("fr"));
			app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
			try
			{
				await app.StartAsync(); using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
				const string route = "/api/v4/ChecklistRuns/";
				const string query = "GetComplianceSummary?fromUtc=2026-08-01T00:00:00Z&untilUtc=2026-08-31T00:00:00Z";
				(await client.GetAsync(route + query)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
				client.DefaultRequestHeaders.Add("Test-Member", "author"); var response = await client.GetAsync(route + query);
				response.StatusCode.Should().Be(HttpStatusCode.OK); response.Headers.CacheControl.NoStore.Should().BeTrue();
				var json = JObject.Parse(await response.Content.ReadAsStringAsync()); json["Data"]["Groups"].Count().Should().Be(3); json.ToString().Should().NotContain("CANARY");
				response = await client.GetAsync(route + "GetComplianceSummary?fromUtc=2026-08-31&untilUtc=2026-08-01"); response.StatusCode.Should().Be(HttpStatusCode.BadRequest); (await response.Content.ReadAsStringAsync()).Should().NotContain("Select a chronological");
				response = await client.GetAsync(route + "GetReadinessPacket?callId=101"); response.StatusCode.Should().Be(HttpStatusCode.OK); (await response.Content.ReadAsStringAsync()).Should().Contain("ReadinessEvidenceManifestV1").And.NotContain("CANARY");
				response = await client.GetAsync(route + "GetEntityChecklistHistory?entityType=1&entityId=1&fromUtc=2026-08-01&untilUtc=2026-08-31"); response.StatusCode.Should().Be(HttpStatusCode.OK);
				_read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult { RedactedFields = { "checklistdefinitionversions.content" } }));
				response = await client.GetAsync(route + query); response.StatusCode.Should().Be(HttpStatusCode.Forbidden); (await response.Content.ReadAsStringAsync()).Should().Contain("protected_data_required").And.NotContain("CANARY");
			}
			finally { ApiClaims._httpContextAccessor = previous; await app.StopAsync(); }
		}
	}
}

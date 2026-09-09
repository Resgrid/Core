using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Checklists;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistWorkflowTests
	{
		[Test, NonParallelizable]
		public async Task V4_HTTP_contract_binds_requests_enforces_authorization_and_replays_one_completion_with_safe_ADP_errors()
		{
			var setup = await Start(); var version = (await _service.GetRunAsync(_actor, setup.Run)).Completion.VersionId;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization();
			const string scheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
			builder.Services.AddAuthentication(scheme).AddScheme<AuthenticationSchemeOptions, ChecklistTestAuthentication>(scheme, _ => { });
			builder.Services.AddAuthorization(o => o.AddPolicy(ResgridResources.Checklist_Update, p => p.RequireRole("manager")));
			builder.Services.AddApiVersioning();
			builder.Services.AddControllers().AddApplicationPart(typeof(ChecklistRunsController).Assembly).AddNewtonsoftJson(o => o.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver());
			builder.Services.AddSingleton<IChecklistsService>(_service); builder.Services.AddSingleton(_access.Object);
			builder.Services.AddSingleton(Mock.Of<IChecklistTemplateService>()); builder.Services.AddSingleton(Mock.Of<IDepartmentDataProtectionService>());
			await using var app = builder.Build(); var previous = ApiClaims._httpContextAccessor;
			ApiClaims._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("fr").AddSupportedCultures("fr").AddSupportedUICultures("fr"));
			app.UseDeveloperExceptionPage();
			app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
			try
			{
				await app.StartAsync(); using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
				const string runs = "/api/v4/ChecklistRuns/";
				(await client.GetAsync(runs + "GetChecklistAccess")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
				client.DefaultRequestHeaders.Add("Test-Member", "crew");
				(await client.PostAsync("/api/v4/Checklists/NewChecklist", Json(new ChecklistDefinitionInput { Form = Form() }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
				client.DefaultRequestHeaders.Remove("Test-Member"); client.DefaultRequestHeaders.Add("Test-Member", "author");
				client.DefaultRequestHeaders.Add(DataProtectionController.GrantHeader, "synthetic-current-grant");
				var id = Guid.NewGuid().ToString(); var start = new ChecklistStartInput { DefinitionId = setup.Definition, VersionId = version, TargetId = "77", CompletionId = id };
				var response = await client.PostAsync(runs + "StartChecklistRun", Json(start));
				response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync()); response.Headers.CacheControl.NoStore.Should().BeTrue();
				var run = JObject.Parse(await response.Content.ReadAsStringAsync())["Data"]; run["Id"].Value<string>().Should().Be(id); run["Revision"].Value<int>().Should().Be(1);
				_authorization.Verify(a => a.RequireMemberAsync(It.Is<ChecklistActor>(x => x.DepartmentId == 77 && x.UserId == "author" && x.GrantToken == "synthetic-current-grant")), Times.AtLeastOnce);
				(await client.PostAsync(runs + "StartChecklistRun", Json(start))).StatusCode.Should().Be(HttpStatusCode.OK);
				(await client.PostAsync(runs + "SaveChecklistRunProgress", Json(new { Id = id, Input = (object)null }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
				response = await client.PostAsync(runs + "SaveChecklistRunProgress", Json(new { Id = id, Input = new { Revision = "SYNTHETIC PRIVATE VALUE" } }));
				response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
				(await response.Content.ReadAsStringAsync()).Should().NotContain("SYNTHETIC PRIVATE VALUE").And.Contain("checklist_validation");
				var input = Answers(setup.Form); var command = new ChecklistProgressInput { Id = id, Input = input };
				(await client.PostAsync(runs + "SaveChecklistRunProgress", Json(command))).StatusCode.Should().Be(HttpStatusCode.OK);
				(await client.PostAsync(runs + "SaveChecklistRunProgress", Json(command))).StatusCode.Should().Be(HttpStatusCode.OK);
				input.Note = "changed"; response = await client.PostAsync(runs + "SaveChecklistRunProgress", Json(command)); response.StatusCode.Should().Be(HttpStatusCode.Conflict);
				JObject.Parse(await response.Content.ReadAsStringAsync())["type"].Value<string>().Should().Be("checklist_conflict");
				input.Revision = 2;
				(await client.PostAsync(runs + "CompleteChecklistRun", Json(command))).StatusCode.Should().Be(HttpStatusCode.OK);
				(await client.PostAsync(runs + "CompleteChecklistRun", Json(command))).StatusCode.Should().Be(HttpStatusCode.OK);
				_events.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.ChecklistCompleted);
				_read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult { RedactedFields = { "checklistcompletions.content" } }));
				response = await client.GetAsync(runs + "GetChecklistRun?id=" + id); response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
				var problem = await response.Content.ReadAsStringAsync(); problem.Should().Contain("protected_data_required").And.NotContain("changed").And.NotContain("Unlock protected").And.NotContain("rgdp:");
				(await _store.ListAsync<ChecklistCompletion>(77)).Should().HaveCount(2);
			}
			finally { ApiClaims._httpContextAccessor = previous; await app.StopAsync(); }
		}
		private static StringContent Json(object data) => new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
	}
}

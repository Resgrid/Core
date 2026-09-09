using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistWorkflowTests
	{
		// Exercise production routes, authorization, antiforgery, model binding and compiled Razor
		// page bodies. The unrelated global navigation shell is excluded from this isolated host.
		private sealed class PageBodyFilter : IResultFilter
		{
			public void OnResultExecuting(ResultExecutingContext context)
			{
				if (context.Result is ViewResult view)
					context.Result = new PartialViewResult { ViewName = "/Areas/User/Views/Checklists/" + (view.ViewName ?? context.RouteData.Values["action"]) + ".cshtml", ViewData = view.ViewData, TempData = view.TempData, StatusCode = view.StatusCode };
			}
			public void OnResultExecuted(ResultExecutedContext context) { }
		}
		private sealed class ChecklistTestAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
		{
			public ChecklistTestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }
			protected override Task<AuthenticateResult> HandleAuthenticateAsync()
			{
				if (!Request.Headers.TryGetValue("Test-Member", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
				var claims = new[] { new Claim(ClaimTypes.PrimarySid, user.ToString()), new Claim(ClaimTypes.PrimaryGroupSid, "77"), new Claim(ClaimTypes.Role, user == "author" ? "manager" : "member") };
				return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "checklist-test")), "checklist-test")));
			}
		}
		[Test, NonParallelizable]
		public async Task Authenticated_pages_enforce_permission_antiforgery_localization_binding_and_revision_conflicts()
		{
			var setup = await Scheduled();
			_authorization.Setup(a => a.TargetsAsync(It.IsAny<ChecklistActor>(), ChecklistTargetType.Department)).ReturnsAsync(new List<ChecklistTarget> { new ChecklistTarget { Type = ChecklistTargetType.Department, Id = "77", Name = "Test department" } });
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Resgrid.sln"))) directory = directory.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(directory.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization();
			builder.Services.AddAuthentication("checklist-test").AddScheme<AuthenticationSchemeOptions, ChecklistTestAuthentication>("checklist-test", _ => { });
			builder.Services.AddAuthorization(o => o.AddPolicy(ResgridResources.Checklist_Update, p => p.RequireRole("manager")));
			builder.Services.AddControllersWithViews(o => o.Filters.Add(new PageBodyFilter())).AddApplicationPart(typeof(ChecklistsController).Assembly);
			builder.Services.AddSingleton(_service as IChecklistsService); builder.Services.AddSingleton(_access.Object);
			builder.Services.AddSingleton(Mock.Of<IChecklistTemplateService>()); builder.Services.AddSingleton(Mock.Of<IProtectedGrantContext>()); builder.Services.AddSingleton(Mock.Of<IDepartmentDataProtectionService>());
			await using var app = builder.Build(); var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.Use(async (context, next) => { try { await next(); } catch (Exception ex) { context.Response.StatusCode = 500; await context.Response.WriteAsync(ex.ToString()); } });
			app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("fr").AddSupportedCultures("fr").AddSupportedUICultures("fr"));
			app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}");
			try
			{
				await app.StartAsync(); using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }; using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };
				var path = "/User/Checklists/EditSchedule?id=" + setup.Input.Id;
				(await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
				client.DefaultRequestHeaders.Add("Test-Member", "crew"); (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
				client.DefaultRequestHeaders.Remove("Test-Member"); client.DefaultRequestHeaders.Add("Test-Member", "author");
				var response = await client.GetAsync(path); var html = await response.Content.ReadAsStringAsync(); response.StatusCode.Should().Be(HttpStatusCode.OK, html);
				response.Headers.CacheControl.NoStore.Should().BeTrue(); WebUtility.HtmlDecode(html).Should().Contain("Attribution automatique").And.NotContain("Automatic routing");
				var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value); token.Should().NotBeNullOrEmpty();
				var fields = new Dictionary<string, string> { ["Input.Id"] = setup.Input.Id, ["Input.DefinitionId"] = setup.Input.DefinitionId, ["Input.Revision"] = "1", ["Input.Name"] = "HTTP schedule edit", ["Input.TargetId"] = "77", ["Input.Frequency"] = "2", ["Input.TimeZoneId"] = "UTC", ["Input.StartDate"] = "2026-09-08", ["Input.TimesOfDay"] = "10:00", ["Input.WindowMinutes"] = "60", ["Input.DayOfMonth"] = "1", ["Input.MonthOfYear"] = "1", ["Input.IsActive"] = "true", ["assignment"] = "0:" };
				(await client.PostAsync("/User/Checklists/SaveSchedule", new FormUrlEncodedContent(fields))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
				fields["__RequestVerificationToken"] = token;
				response = await client.PostAsync("/User/Checklists/SaveSchedule", new FormUrlEncodedContent(fields)); response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
				(await _service.GetScheduleAsync(_actor, setup.Input.Id)).Content.Name.Should().Be("HTTP schedule edit");
				(await client.PostAsync("/User/Checklists/SaveSchedule", new FormUrlEncodedContent(fields))).StatusCode.Should().Be(HttpStatusCode.Conflict);
				await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime); var occurrence = (await _store.ListAsync<ChecklistOccurrence>(77)).First();
				foreach (var page in new[] { "/User/Checklists/Due", "/User/Checklists/Reminders", "/User/Checklists/Occurrence?id=" + occurrence.Id })
				{ response = await client.GetAsync(page); response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync()); }
				_read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult { RedactedFields = { "checklistschedules.content" } }));
				response = await client.GetAsync(path); html = await response.Content.ReadAsStringAsync(); response.StatusCode.Should().Be(HttpStatusCode.OK, html); html.Should().NotContain("HTTP schedule edit");
				response = await client.GetAsync("/User/Checklists/Occurrence?id=" + occurrence.Id); html = await response.Content.ReadAsStringAsync(); html.Should().Contain("value=\"Occurrence\"").And.NotContain("HTTP schedule edit");
				_read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult()));
				response = await client.PostAsync("/User/Checklists/Reopen", new FormUrlEncodedContent(new Dictionary<string, string> { ["page"] = "Occurrence", ["id"] = occurrence.Id, ["__RequestVerificationToken"] = token }));
				response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
			}
			finally { await app.StopAsync(); ClaimsAuthorizationHelper._httpContextAccessor = previous; }
		}
	}
}

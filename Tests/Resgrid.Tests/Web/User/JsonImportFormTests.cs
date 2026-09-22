using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
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
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services.Invoicing;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	[TestFixture, NonParallelizable]
	public class JsonImportFormTests
	{
		private sealed class PageBodyFilter : IResultFilter
		{
			public void OnResultExecuting(ResultExecutingContext context)
			{
				if (context.Result is ViewResult view)
					context.Result = new PartialViewResult { ViewName = "/Areas/User/Views/RateSchedules/" + (view.ViewName ?? context.RouteData.Values["action"]) + ".cshtml", ViewData = view.ViewData, TempData = view.TempData };
			}
			public void OnResultExecuted(ResultExecutedContext context) { }
		}

		private sealed class TestAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
		{
			public TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }
			protected override Task<AuthenticateResult> HandleAuthenticateAsync()
			{
				var claims = new[] { new Claim(ClaimTypes.PrimarySid, "manager"), new Claim(ClaimTypes.PrimaryGroupSid, "77"), new Claim(ResgridClaimTypes.Resources.Invoicing, ResgridClaimTypes.Actions.Update) };
				return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "json-test")), "json-test")));
			}
		}

		[Test]
		public async Task Import_form_documents_and_retains_pasted_and_uploaded_json_on_failure()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !System.IO.File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders();
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddLocalization();
			builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			builder.Services.AddAuthentication("json-test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("json-test", _ => { });
			builder.Services.AddAuthorization();
			builder.Services.AddControllersWithViews(o => o.Filters.Add(new PageBodyFilter())).AddApplicationPart(typeof(RateSchedulesController).Assembly);
			var rates = new Mock<IRateScheduleService>();
			var saves = 0;
			rates.Setup(x => x.GetSchedulesForDepartmentAsync(77, false)).ReturnsAsync(new List<RateSchedule>());
			rates.Setup(x => x.ImportScheduleJsonAsync(77, It.IsAny<string>(), "manager", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int departmentId, string json, string user, string ip, string agent, CancellationToken token) =>
				{
					var parsed = RateScheduleJsonImport.Read(json);
					saves++;
					return new RateSchedule { RateScheduleId = "imported", Name = parsed.Name };
				});
			builder.Services.AddSingleton(rates.Object);
			builder.Services.AddSingleton(Mock.Of<IBusinessOperationsAccessService>(a => a.CanUseContractorBillingAsync(77) == Task.FromResult(true)));
			builder.Services.AddSingleton(Mock.Of<IUnitsService>());
			builder.Services.AddSingleton(Mock.Of<ICertificationService>());
			builder.Services.AddSingleton(Mock.Of<IDepartmentsService>(d => d.GetDepartmentByIdAsync(77, false) == Task.FromResult(new Department { DepartmentId = 77, TimeZone = "Pacific Standard Time" })));
			await using var app = builder.Build();
			var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.Use(async (context, next) => { try { await next(); } catch (Exception ex) { context.Response.StatusCode = 500; await context.Response.WriteAsync(ex.ToString()); } });
			app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("en").AddSupportedCultures("en").AddSupportedUICultures("en"));
			app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
			app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}");
			try
			{
				await app.StartAsync();
				using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
				using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };
				var response = await client.GetAsync("/User/RateSchedules/Index");
				var html = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.OK, html);
				WebUtility.HtmlDecode(html).Should().Contain("JSON example").And.Contain("JSON schema (field names and types)").And.Contain("\"Rate\": 25.50");
				var example = WebUtility.HtmlDecode(Regex.Match(html, "<code>(.*?)</code>", RegexOptions.Singleline).Groups[1].Value);
				RateScheduleJsonImport.Read(example).Entries.Should().ContainSingle();
				var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
				token.Should().NotBeNullOrEmpty();
				const string invalid = "{\"Name\":\"Keep <script> text\",\"Entries\":[{\"Name\":\"test\",\"IsActive\":\"yes\"}]}";
				response = await client.PostAsync("/User/RateSchedules/Import", new FormUrlEncodedContent(new Dictionary<string, string> { ["json"] = invalid }));
				response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
				saves.Should().Be(0);
				response = await client.PostAsync("/User/RateSchedules/Import", new FormUrlEncodedContent(new Dictionary<string, string> { ["json"] = invalid, ["__RequestVerificationToken"] = token }));
				html = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.BadRequest, html);
				html.Should().Contain("$.Entries[0].IsActive").And.Contain("expected true or false").And.NotContain("Keep <script> text");
				WebUtility.HtmlDecode(Regex.Match(html, "<textarea[^>]*id=\"importJson\"[^>]*>(.*?)</textarea>", RegexOptions.Singleline).Groups[1].Value).Should().Be(invalid);
				saves.Should().Be(0);
				using var upload = new MultipartFormDataContent();
				upload.Add(new StringContent(token), "__RequestVerificationToken");
				upload.Add(new StringContent("{\n  \"Name\":\n}"), "file", "rates.json");
				response = await client.PostAsync("/User/RateSchedules/Import", upload);
				html = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.BadRequest, html);
				html.Should().Contain("invalid JSON at line 3");
				saves.Should().Be(0);
				response = await client.PostAsync("/User/RateSchedules/Import", new FormUrlEncodedContent(new Dictionary<string, string> { ["json"] = example, ["__RequestVerificationToken"] = token }));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
				response.Headers.Location.ToString().Should().Contain("imported");
				saves.Should().Be(1);
			}
			finally { await app.StopAsync(); ClaimsAuthorizationHelper._httpContextAccessor = previous; }
		}
	}
}

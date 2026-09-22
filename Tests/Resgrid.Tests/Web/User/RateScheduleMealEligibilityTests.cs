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
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
    [TestFixture, NonParallelizable]
    public class RateScheduleMealEligibilityTests
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
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "meal-test")), "meal-test")));
            }
        }

        [Test]
        public async Task Meal_rules_bind_validate_serialize_and_reload_through_the_real_form()
        {
            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root != null && !System.IO.File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddLocalization();
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddAuthentication("meal-test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("meal-test", _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddControllersWithViews(o => o.Filters.Add(new PageBodyFilter())).AddApplicationPart(typeof(RateSchedulesController).Assembly);
            var rates = new Mock<IRateScheduleService>();
            RateSchedule saved = null;
            var saveCount = 0;
            rates.Setup(x => x.SaveScheduleAsync(It.IsAny<RateSchedule>(), "manager", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RateSchedule schedule, string user, string ip, string agent, CancellationToken token) =>
                {
                    saved = schedule;
                    saved.RateScheduleId = "schedule-1";
                    saveCount++;
                    return saved;
                });
            rates.Setup(x => x.GetScheduleByIdAsync("schedule-1", 77, true)).ReturnsAsync(() => saved);
            rates.Setup(x => x.ExportScheduleJsonAsync("schedule-1", 77)).ReturnsAsync("{}");
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
                var response = await client.GetAsync("/User/RateSchedules/New");
                var html = await response.Content.ReadAsStringAsync();
                response.StatusCode.Should().Be(HttpStatusCode.OK, html);
                html.Should().Contain("Add meal rule").And.Contain("type=\"time\"").And.NotContain("MealEligibilityJson");
                var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
                token.Should().NotBeNullOrEmpty();
                var fields = new Dictionary<string, string>
                {
                    ["Name"] = "Fire crew rates", ["Description"] = "Keep this description", ["Currency"] = "USD", ["IsActive"] = "false",
                    ["CancellationVehiclesFullDay"] = "false", ["NoClear8CarryOver"] = "false",
                    ["MealEligibility[0].MealCode"] = " B ", ["MealEligibility[0].StartsBefore"] = "07:00", ["MealEligibility[0].EndsAfter"] = "",
                    ["MealEligibility[1].MealCode"] = "Dinner", ["MealEligibility[1].StartsBefore"] = "", ["MealEligibility[1].EndsAfter"] = "23:59",
                    ["MealEligibility[2].MealCode"] = "Night", ["MealEligibility[2].StartsBefore"] = "23:00", ["MealEligibility[2].EndsAfter"] = "01:00",
                    ["MealEligibility[3].MealCode"] = "Any", ["MealEligibility[3].StartsBefore"] = "", ["MealEligibility[3].EndsAfter"] = "",
                    ["MealEligibility[4].MealCode"] = "", ["MealEligibility[4].StartsBefore"] = "", ["MealEligibility[4].EndsAfter"] = ""
                };
                (await client.PostAsync("/User/RateSchedules/Save", new FormUrlEncodedContent(fields))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
                saveCount.Should().Be(0);
                fields["__RequestVerificationToken"] = token;
                response = await client.PostAsync("/User/RateSchedules/Save", new FormUrlEncodedContent(fields));
                response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
                saved.DepartmentId.Should().Be(77);
                saved.IsActive.Should().BeFalse();
                saved.Policy.CancellationVehiclesFullDay.Should().BeFalse();
                saved.Policy.NoClear8CarryOver.Should().BeFalse();
                saved.Policy.MealEligibility.Should().BeEquivalentTo(new[]
                {
                    new MealEligibilityWindow { MealCode = "B", StartsBeforeMinutes = 420 },
                    new MealEligibilityWindow { MealCode = "Dinner", EndsAfterMinutes = 1439 },
                    new MealEligibilityWindow { MealCode = "Night", StartsBeforeMinutes = 1380, EndsAfterMinutes = 60 },
                    new MealEligibilityWindow { MealCode = "Any" }
                });
                response = await client.GetAsync("/User/RateSchedules/Edit?id=schedule-1");
                html = await response.Content.ReadAsStringAsync();
                response.StatusCode.Should().Be(HttpStatusCode.OK, html);
                html.Should().Contain("value=\"07:00\"").And.Contain("value=\"23:59\"").And.Contain("value=\"01:00\"");

                foreach (var invalidTime in new[] { "24:00", "-01:00", "07:60", "07:00:30", "420", "not-a-time" })
                {
                    fields["MealEligibility[0].StartsBefore"] = invalidTime;
                    response = await client.PostAsync("/User/RateSchedules/Save", new FormUrlEncodedContent(fields));
                    html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
                    response.StatusCode.Should().Be(HttpStatusCode.OK, html);
                    html.Should().Contain("Enter a valid time from 00:00 to 23:59.").And.Contain("Fire crew rates").And.Contain("Keep this description");
                    saveCount.Should().Be(1);
                }
                fields["MealEligibility[0].StartsBefore"] = "00:00";
                foreach (var invalidCode in new[] { "", "           ", "12345678901" })
                {
                    fields["MealEligibility[0].MealCode"] = invalidCode;
                    html = WebUtility.HtmlDecode(await (await client.PostAsync("/User/RateSchedules/Save", new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync());
                    html.Should().Contain("Enter a meal code of 1 to 10 characters.");
                    saveCount.Should().Be(1);
                }
                fields["MealEligibility[0].MealCode"] = " dinner ";
                fields["RateScheduleId"] = "schedule-1";
                html = WebUtility.HtmlDecode(await (await client.PostAsync("/User/RateSchedules/Save", new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync());
                html.Should().Contain("Each meal code can have only one eligibility rule.").And.Contain("value=\"00:00\"").And.Contain("value=\"schedule-1\"");
                saveCount.Should().Be(1);
                fields["MealEligibility[0].MealCode"] = "B";
                fields["RoundingMinutes"] = "invalid";
                response = await client.PostAsync("/User/RateSchedules/Save", new FormUrlEncodedContent(fields));
                (await response.Content.ReadAsStringAsync()).Should().Contain("value=\"invalid\"");
                saveCount.Should().Be(1, "binding errors must prevent saving even outside the meal fields");
                fields.Remove("RoundingMinutes");
                foreach (var key in fields.Keys.Where(k => k.StartsWith("MealEligibility[", StringComparison.Ordinal)).ToList()) fields.Remove(key);
                response = await client.PostAsync("/User/RateSchedules/Save", new FormUrlEncodedContent(fields));
                response.StatusCode.Should().Be(HttpStatusCode.Redirect);
                saveCount.Should().Be(2);
                saved.Policy.MealEligibility.Should().BeEmpty("removing every rule must clear the saved windows");
            }
            finally { await app.StopAsync(); ClaimsAuthorizationHelper._httpContextAccessor = previous; }
        }
    }
}

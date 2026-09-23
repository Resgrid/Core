using System;
using System.Collections.Generic;
using System.IO;
using File = System.IO.File;
using Match = System.Text.RegularExpressions.Match;
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
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// PR #522 review: on a deployment-wide time report a crew member sees another crew's rows read-only (disabled, so never
	/// posted). Every row used to be named by its loop index, so a read-only row ahead of the member's own left a gap in the
	/// posted entries[n]; the binder stops at the first missing index, the bound list came back empty, and the restricted save
	/// then deleted the member's own entries. Rendered through the real view and posted the way a browser posts the form.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class TimeReportEntryIndexingTests
	{
		private static readonly DateTime Day = new DateTime(2026, 9, 20);

		[Test]
		public async Task A_read_only_row_ahead_of_the_members_own_rows_does_not_drop_them_from_the_save()
		{
			var deployment = new Deployment
			{
				DeploymentId = "dep", DepartmentId = 77, Name = "Synthetic deployment", Status = (int)DeploymentStatuses.Active,
				Personnel =
				{
					new DeploymentPersonnel { DeploymentPersonnelId = "dp-other", DeploymentId = "dep", DepartmentId = 77, UserId = "other", DisplayName = "Other Crew" },
					new DeploymentPersonnel { DeploymentPersonnelId = "dp-member", DeploymentId = "dep", DepartmentId = 77, UserId = "member", DisplayName = "Test Member" }
				}
			};
			var report = new DeploymentTimeReport
			{
				DeploymentTimeReportId = "dtr", DeploymentId = "dep", DepartmentId = 77, ReportNumber = 7, ReportDate = Day, Status = (int)DeploymentTimeReportStatuses.Draft,
				Entries =
				{
					new DeploymentTimeEntry { DeploymentTimeEntryId = "e-other", DeploymentPersonnelId = "dp-other", SortOrder = 0, StartTime = Day.AddHours(14), EndTime = Day.AddHours(22) },
					new DeploymentTimeEntry { DeploymentTimeEntryId = "e-own-1", DeploymentPersonnelId = "dp-member", SortOrder = 1, StartTime = Day.AddHours(15), EndTime = Day.AddHours(19) },
					new DeploymentTimeEntry { DeploymentTimeEntryId = "e-own-2", DeploymentPersonnelId = "dp-member", SortOrder = 2, StartTime = Day.AddHours(20), EndTime = Day.AddHours(23) }
				}
			};
			var access = new DeploymentTimeAccess { IsRostered = true, PersonnelId = "dp-member", WritableSubjectIds = { "dp-member" } };

			var operations = new Mock<IDeploymentService>();
			operations.Setup(x => x.GetDeploymentByIdAsync("dep", 77)).ReturnsAsync(deployment);
			operations.Setup(x => x.GetTimeAccessAsync(It.IsAny<Deployment>(), "member", false)).ReturnsAsync(access);
			var time = new Mock<ITimeTrackingService>();
			time.Setup(x => x.GetTimeReportByIdAsync("dtr", 77)).ReturnsAsync(report);
			time.Setup(x => x.GetExpensesAsync("dep", 77)).ReturnsAsync(new List<DeploymentExpense>());
			List<DeploymentTimeEntry> saved = null;
			time.Setup(x => x.SaveTimeEntriesAsync("dtr", 77, It.IsAny<List<DeploymentTimeEntry>>(), access, "member", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string _, int __, List<DeploymentTimeEntry> entries, DeploymentTimeAccess ___, string ____, string _____, string ______, CancellationToken _______) =>
				{
					saved = entries;
					return new TimeReportSaveResult { Report = report };
				});

			await WithServer(operations, time, async client =>
			{
				var response = await client.GetAsync("/User/Deployments/TimeReport?id=dtr");
				var html = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.OK, html);

				// The other crew's row takes no index and is disabled; the member's rows are entries[0] and entries[1].
				Regex.Match(html, "name=\"entries\\[-1\\]\\.Id\"[^>]*value=\"e-other\"[^>]*disabled").Success.Should().BeTrue(html);
				Regex.Match(html, "name=\"entries\\[0\\]\\.Id\"[^>]*value=\"e-own-1\"").Success.Should().BeTrue(html);
				Regex.Match(html, "name=\"entries\\[1\\]\\.Id\"[^>]*value=\"e-own-2\"").Success.Should().BeTrue(html);

				var form = BrowserForm(html);
				form.Keys.Should().NotContain(k => k.StartsWith("entries[-1]", StringComparison.Ordinal), "a disabled control is never posted");
				response = await client.PostAsync("/User/Deployments/SaveTimeReport?id=dtr", new FormUrlEncodedContent(form));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
			});

			saved.Should().NotBeNull();
			saved.Select(e => e.DeploymentTimeEntryId).Should().Equal(new[] { "e-own-1", "e-own-2" }, "both of the member's rows reach the restricted save; the other crew's row stays as stored");
			saved.Should().OnlyContain(e => e.DeploymentPersonnelId == "dp-member");
		}

		/// <summary>What a browser submits for #dtrForm: every enabled named input (hidden ones included) and each enabled select's selected option.</summary>
		private static Dictionary<string, string> BrowserForm(string html)
		{
			var start = html.IndexOf("id=\"dtrForm\"", StringComparison.Ordinal);
			start.Should().BeGreaterThan(0, "the time report form should render");
			var form = html.Substring(start, html.IndexOf("</form>", start, StringComparison.Ordinal) - start);
			var fields = new Dictionary<string, string>();
			foreach (Match input in Regex.Matches(form, "<input\\b[^>]*>"))
			{
				var tag = input.Value;
				var name = Attribute(tag, "name");
				if (name == null || Regex.IsMatch(tag, "\\sdisabled\\b")) continue;
				var type = Attribute(tag, "type") ?? "text";
				if ((type == "checkbox" || type == "radio") && !Regex.IsMatch(tag, "\\schecked\\b")) continue;
				fields[name] = WebUtility.HtmlDecode(Attribute(tag, "value") ?? string.Empty);
			}
			foreach (Match select in Regex.Matches(form, "<select\\b([^>]*)>(.*?)</select>", RegexOptions.Singleline))
			{
				var tag = "<select" + select.Groups[1].Value + ">";
				var name = Attribute(tag, "name");
				if (name == null || Regex.IsMatch(tag, "\\sdisabled\\b")) continue;
				var options = Regex.Matches(select.Groups[2].Value, "<option\\b[^>]*>").Select(o => o.Value).ToList();
				var chosen = options.FirstOrDefault(o => Regex.IsMatch(o, "\\sselected\\b")) ?? options.FirstOrDefault();
				fields[name] = WebUtility.HtmlDecode(Attribute(chosen ?? string.Empty, "value") ?? string.Empty);
			}
			return fields;
		}

		private static string Attribute(string tag, string name)
		{
			var match = Regex.Match(tag, "\\s" + name + "=\"([^\"]*)\"");
			return match.Success ? match.Groups[1].Value : null;
		}

		private static async Task WithServer(Mock<IDeploymentService> operations, Mock<ITimeTrackingService> time, Func<HttpClient, Task> test)
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization(); builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, MemberAuthentication>("test", _ => { });
			builder.Services.AddAuthorization();
			builder.Services.AddControllersWithViews(o => o.Filters.Add(new BodyOnly())).AddApplicationPart(typeof(DeploymentsController).Assembly);
			builder.Services.AddSingleton(operations.Object); builder.Services.AddSingleton(time.Object);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(77, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = 77, Name = "Synthetic department", TimeZone = "UTC" });
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.Deployments, 77, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			builder.Services.AddSingleton(departments.Object); builder.Services.AddSingleton(flags.Object);
			foreach (var parameter in typeof(DeploymentsController).GetConstructors().Single().GetParameters())
			{
				var type = parameter.ParameterType;
				if (parameter.HasDefaultValue || builder.Services.Any(s => s.ServiceType == type) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Localization.IStringLocalizer<>))) continue;
				builder.Services.AddSingleton(type, ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))).Object);
			}
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
				await test(client);
			}
			finally { await app.StopAsync(); ClaimsAuthorizationHelper._httpContextAccessor = previous; }
		}

		private sealed class BodyOnly : IResultFilter
		{
			public void OnResultExecuting(ResultExecutingContext context)
			{
				if (context.Result is not ViewResult view) return;
				var action = (ControllerActionDescriptor)context.ActionDescriptor;
				context.Result = new PartialViewResult { ViewName = $"/Areas/User/Views/{action.ControllerName}/{view.ViewName ?? action.ActionName}.cshtml", ViewData = view.ViewData, TempData = view.TempData };
			}
			public void OnResultExecuted(ResultExecutedContext context) { }
		}

		/// <summary>A rostered member with no deployment claims: not a manager, so the time access decides every row.</summary>
		private sealed class MemberAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
		{
			public MemberAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }
			protected override Task<AuthenticateResult> HandleAuthenticateAsync()
			{
				var claims = new[] { new Claim(ClaimTypes.PrimarySid, "member"), new Claim(ClaimTypes.NameIdentifier, "member"), new Claim(ClaimTypes.PrimaryGroupSid, "77") };
				return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
			}
		}
	}
}

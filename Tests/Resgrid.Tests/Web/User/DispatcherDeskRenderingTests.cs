using System;
using System.Collections.Generic;
using System.IO;
using File = System.IO.File;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Areas.User.Models.DispatcherDesk;
using Resgrid.Web.Areas.User.Models.Shifts;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>Renders the dispatcher desk pages and the shift partials through the production views (no layout).</summary>
	[Area("User")]
	public class DispatcherDeskRenderingController : Controller
	{
		private static readonly Department Department = new Department { DepartmentId = 12, TimeZone = "Pacific Standard Time", Use24HourTime = true };

		public IActionResult Show(string page)
		{
			RouteData.Values["controller"] = page.StartsWith("Shifts", StringComparison.Ordinal) ? "Shifts" : "DispatcherDesk";

			switch (page)
			{
				case "Index":
					return PartialView("/Areas/User/Views/DispatcherDesk/Index.cshtml", new DispatcherDeskIndexView { DeskEnabled = false, CanSupervise = true, IsAdmin = true, PollSeconds = 15 });
				case "Breaks":
					return PartialView("/Areas/User/Views/DispatcherDesk/Breaks.cshtml", new DispatcherDeskBreaksView { Date = "2026-11-20", ShiftDayId = 70, ShiftName = "Days <A>", CanSupervise = true });
				case "Settings":
					var settings = DispatcherDeskSettingsView.From(new DispatcherDeskConfig
					{
						Enabled = true,
						EmailCallMode = (int)DispatcherEmailCallModes.Pending,
						DefaultBreakPlan = new List<DispatcherDeskBreakPlanEntry> { new DispatcherDeskBreakPlanEntry { BreakType = 2, DurationMinutes = 30, TargetOffsetMinutes = 240 } }
					});
					settings.Message = "Saved";
					return PartialView("/Areas/User/Views/DispatcherDesk/Settings.cshtml", settings);
				case "Activity":
					return PartialView("/Areas/User/Views/DispatcherDesk/Activity.cshtml", new DispatcherDeskActivityView
					{
						From = "2026-11-01",
						To = "2026-11-20",
						Created = 3,
						AverageSecondsToAnswer = 75,
						CreatedByKind = new List<KeyValuePair<int, int>> { new KeyValuePair<int, int>(3, 2), new KeyValuePair<int, int>(99, 1) },
						Dispatchers = new List<DispatcherDeskStats> { new DispatcherDeskStats { UserId = "u1", Name = "Pat <Dispatcher>", BreaksOverdue = 1, OverdueMinutes = 7, AverageSecondsToAccept = 4000 } }
					});
				case "ShiftsSection":
					return PartialView("/Areas/User/Views/Shifts/_ShiftDispatchSection.cshtml", new ShiftDispatchSectionView
					{
						IsEnabled = true,
						RequiredDispatchers = 2,
						DepartmentMinimum = 1,
						ShowStandingRoster = true,
						BreakPlan = new List<DispatcherBreakPlanRow> { new DispatcherBreakPlanRow { BreakType = 2, DurationMinutes = 30, TargetOffsetMinutes = null } },
						Candidates = new List<ShiftDispatcherCandidate>
						{
							new ShiftDispatcherCandidate { UserId = "u1", Name = "Pat", OnRoster = true, IsSupervisor = true },
							new ShiftDispatcherCandidate { UserId = "u2", Name = "Sam", IsEligible = false, OnRoster = true }
						}
					});
				case "ShiftsDay":
					return PartialView("/Areas/User/Views/Shifts/_ShiftDayDispatchers.cshtml", new ShiftDayView
					{
						ShiftDayId = 70,
						Department = Department,
						Dispatch = new ShiftDayDispatchView
						{
							ShiftDayId = 70,
							RequiredDispatchers = 3,
							IsSignupShift = true,
							RequireApproval = true,
							CanManage = true,
							CanSignup = true,
							Rows = new List<ShiftDayDispatcherRow>
							{
								new ShiftDayDispatcherRow { UserId = "u1", Name = "Pat", ShiftDispatcherId = "sd1", IsSupervisor = true, CanRemove = true, Start = new DateTime(2026, 11, 20, 7, 0, 0), End = new DateTime(2026, 11, 20, 19, 0, 0) },
								new ShiftDayDispatcherRow { UserId = "u2", Name = "Sam", ShiftDispatcherId = "sd2", IsPending = true, Source = 2, CanReview = true, IsYou = true, CanWithdraw = true }
							},
							AssignOptions = new List<ShiftDayOption> { new ShiftDayOption { Value = "u3", Text = "Lee" } }
						}
					});
				case "ShiftsOnDuty":
					return PartialView("/Areas/User/Views/Shifts/_OnDutyDispatchers.cshtml", new OnDutyDispatchersView
					{
						DeskEnabled = true,
						BelowMinimum = true,
						MinimumAvailable = 2,
						AvailableCount = 1,
						QueueCount = 2,
						Dispatchers = new List<OnDutyDispatcherView>
						{
							new OnDutyDispatcherView { UserId = "u1", Name = "Pat", State = DispatcherDisplayStates.OnBreak, BreakType = 2, BreakMinutesLeft = 4, IsOnDuty = true },
							new OnDutyDispatcherView { UserId = "u2", Name = "Sam", State = DispatcherDisplayStates.Away, AwayReason = "Radio check", OverdueMinutes = 0, HeldItems = 1, OfferedItems = 1 }
						}
					});
				default:
					return NotFound();
			}
		}
	}

	[TestFixture, NonParallelizable]
	public class DispatcherDeskRenderingTests
	{
		[Test]
		public async Task Dispatcher_desk_pages_and_shift_partials_render()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders();
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddLocalization();
			builder.Services.AddAdminAssistFieldHelpStubs();
			builder.Services.AddWebOptimizer();
			builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			builder.Services.AddControllersWithViews()
				.AddApplicationPart(typeof(Resgrid.Web.Areas.User.Controllers.DispatcherDeskController).Assembly)
				.AddApplicationPart(typeof(DispatcherDeskRenderingController).Assembly);
			await using var app = builder.Build();

			var previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.Use(async (context, next) =>
			{
				context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, "u2"), new Claim(ClaimTypes.PrimaryGroupSid, "12") }, "Test"));
				try { await next(); } catch (Exception ex) { context.Response.StatusCode = 500; await context.Response.WriteAsync(ex.ToString()); }
			});
			app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("en").AddSupportedCultures("en").AddSupportedUICultures("en"));
			app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}");

			try
			{
				await app.StartAsync();
				using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

				async Task<string> Render(string page)
				{
					var response = await client.GetAsync("/User/DispatcherDeskRendering/Show?page=" + page);
					var html = await response.Content.ReadAsStringAsync();
					response.StatusCode.Should().Be(HttpStatusCode.OK, html);
					return html;
				}

				var index = await Render("Index");
				// Without a layout the Scripts section is not written; the page body is what is checked here.
				index.Should().Contain("id=\"deskMembers\"").And.Contain("Routing work to dispatchers is turned off").And.Contain("id=\"deskTransferModal\"");
				// Breaks, Activity and Desk Settings are not in the sidebar; the board's top buttons are the way in, and they
				// carry their name only (no icon), like On Duty Now.
				foreach (var (action, text) in new[] { ("Breaks", "Dispatcher Breaks"), ("Activity", "Dispatcher Activity"), ("Settings", "Dispatcher Desk Settings") })
					index.Should().MatchRegex($"<a\\b(?=[^>]*\\btop-button\\b)(?=[^>]*href=\"/User/DispatcherDesk/{action}\")[^>]*>{text}</a>");

				var breaks = await Render("Breaks");
				breaks.Should().Contain("id=\"breaksTimeline\"").And.Contain("id=\"breaksPlan\"").And.Contain("Days &lt;A&gt;").And.NotContain("Days <A>");
				const string boardButton = "<a\\b(?=[^>]*\\btop-button\\b)(?=[^>]*href=\"/User/DispatcherDesk\")[^>]*>Dispatcher Desk</a>";
				breaks.Should().MatchRegex(boardButton, "the way back to the board is a text-only top button");

				var settings = await Render("Settings");
				settings.Should().Contain("name=\"EmailCallMode\"").And.Contain("name=\"breakPlanMinutes_0\" value=\"30\"").And.Contain("name=\"breakPlanOffset_0\" value=\"240\"");
				settings.Should().Contain("Enhanced AI is not available", "the department in this harness has no add-on");
				settings.Should().MatchRegex(boardButton, "the way back to the board is a text-only top button");

				var activity = await Render("Activity");
				activity.Should().Contain("Pat &lt;Dispatcher&gt;").And.Contain("1:06:40").And.Contain("1:15");

				var section = await Render("ShiftsSection");
				section.Should().Contain("name=\"DispatchSectionPosted\" value=\"true\"").And.Contain("name=\"dispatchRoster\" value=\"u1\"").And.Contain("No Dispatch access");
				section.Should().NotContain("value=\"u2\"", "a roster member without Dispatch access is not posted back");

				var day = await Render("ShiftsDay");
				day.Should().Contain("DispatcherReview").And.Contain("DispatcherRemove").And.Contain("DispatcherSignup").And.Contain("DispatcherAssign").And.Contain("shiftDayId=70");

				var onDuty = await Render("ShiftsOnDuty");
				onDuty.Should().Contain("Back in 4 min").And.Contain("Radio check").And.Contain("Only 1 available (minimum 2)");
			}
			finally
			{
				ClaimsAuthorizationHelper._httpContextAccessor = previousAccessor;
				await app.StopAsync();
			}
		}
	}
}

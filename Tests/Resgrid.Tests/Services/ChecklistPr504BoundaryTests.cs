using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Repositories.DataRepository.Transactions;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistDatabaseTests
	{
		[Test, Order(180)]
		public async Task Member_export_queries_page_only_owned_targeted_and_witnessed_records_with_tenant_scoped_relations()
		{
			var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow);
			var user = "member-" + Guid.NewGuid().ToString("N") + "' OR 1=1 --";
			var expectedCompletions = new List<string>(); var expectedSchedules = new List<string>(); var expectedOccurrences = new List<string>();
			await uow.CreateOrGetConnectionAsync();
			foreach (var department in new[] { 77, 88 })
			{
				var definition = Row<ChecklistDefinition>(); definition.DepartmentId = department; await store.WriteAsync(definition, true);
				var version = Row<ChecklistDefinitionVersion>(definition.Id); version.DepartmentId = department; version.Version = 1; await store.WriteAsync(version, true);
				for (var i = 0; i < 6; i++)
				{
					var schedule = Row<ChecklistSchedule>(definition.Id); schedule.DepartmentId = department; schedule.VersionId = version.Id;
					schedule.CreatedBy = i == 3 ? user : "another-member"; schedule.TargetType = (int)ChecklistTargetType.Personnel; schedule.TargetId = i == 1 ? user : "another-target";
					schedule.TimeZoneId = "UTC"; schedule.ClockMinutes = "480"; schedule.StartDate = new DateTime(2026, 9, 8);
					schedule.ActiveFromUtc = schedule.GeneratedThroughUtc = schedule.LastSweepUtc = schedule.StartDate; await store.WriteAsync(schedule, true);
					var occurrence = Row<ChecklistOccurrence>(definition.Id); occurrence.DepartmentId = department; occurrence.VersionId = version.Id;
					occurrence.ScheduleId = schedule.Id; occurrence.ScheduleRevision = 1; occurrence.PeriodStartUtc = schedule.StartDate; occurrence.CompletionId = Guid.NewGuid().ToString();
					occurrence.TargetType = (int)ChecklistTargetType.Personnel; occurrence.TargetId = i == 5 ? user : "another-target"; await store.WriteAsync(occurrence, true);
					var completion = Row<ChecklistCompletion>(definition.Id); completion.DepartmentId = department; completion.Id = occurrence.CompletionId; completion.VersionId = version.Id; completion.OccurrenceId = occurrence.Id;
					completion.CreatedBy = i == 0 ? user : "another-member"; completion.TargetType = (int)ChecklistTargetType.Personnel; completion.TargetId = i == 1 ? user : "another-target";
					completion.WitnessUserId = i == 2 ? user : null; await store.WriteAsync(completion, true);
					if (department != 77) continue;
					if (i <= 2) expectedCompletions.Add(completion.Id);
					if (i == 1 || i == 3) expectedSchedules.Add(schedule.Id);
					if (i == 0 || i == 1 || i == 3 || i == 5) expectedOccurrences.Add(occurrence.Id);
				}
			}
			uow.CommitChanges();
			async Task<List<string>> Pages<T>() where T : ChecklistRow
			{
				var ids = new List<string>();
				for (var skip = 0; skip < 20; skip++)
				{
					var page = await store.ListForMemberAsync<T>(77, user, skip, 1);
					if (page.Count == 0) return ids;
					page.Should().ContainSingle().Which.DepartmentId.Should().Be(77); ids.Add(page[0].Id);
				}
				throw new InvalidOperationException("Member query did not finish paging.");
			}
			(await Pages<ChecklistCompletion>()).Should().BeEquivalentTo(expectedCompletions);
			(await Pages<ChecklistSchedule>()).Should().BeEquivalentTo(expectedSchedules);
			(await Pages<ChecklistOccurrence>()).Should().BeEquivalentTo(expectedOccurrences);
			(await store.ListForMemberAsync<ChecklistCompletion>(99, user)).Should().BeEmpty();
			(await store.ListForMemberAsync<ChecklistOccurrence>(77, "missing-member")).Should().BeEmpty();
		}
	}

	public partial class ChecklistWorkflowTests
	{
		[TestCase(false), TestCase(true), NonParallelizable]
		public async Task Schedule_page_encodes_untrusted_names_timezones_and_all_route_links(bool canEdit)
		{
			const string attack = "\"><script>alert('schedule-canary')</script><a onmouseover=\"attack";
			var checklists = new Mock<IChecklistsService>();
			checklists.Setup(s => s.SchedulesAsync(It.IsAny<ChecklistActor>(), attack, 1, false)).ReturnsAsync(new List<ChecklistScheduleView>
			{
				new ChecklistScheduleView { Schedule = new ChecklistSchedule { Id = attack, TimeZoneId = attack, Frequency = 2 }, Content = new ChecklistScheduleContent { Name = attack } }
			});
			_access.Setup(a => a.CanUseChecklistsAsync(77)).ReturnsAsync(canEdit);
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln"))) directory = directory.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(directory.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization();
			builder.Services.AddAuthentication("checklist-test").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ChecklistTestAuthentication>("checklist-test", _ => { });
			builder.Services.AddAuthorization(o => o.AddPolicy(ResgridResources.Checklist_Update, p => p.RequireRole("manager")));
			builder.Services.AddControllersWithViews(o => o.Filters.Add(new PageBodyFilter())).AddApplicationPart(typeof(ChecklistsController).Assembly);
			builder.Services.AddSingleton(checklists.Object); builder.Services.AddSingleton(_access.Object);
			builder.Services.AddSingleton(Mock.Of<IChecklistTemplateService>()); builder.Services.AddSingleton(Mock.Of<IProtectedGrantContext>()); builder.Services.AddSingleton(Mock.Of<IDepartmentDataProtectionService>());
			await using var app = builder.Build(); var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}");
			try
			{
				await app.StartAsync(); using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) }; client.DefaultRequestHeaders.Add("Test-Member", "author");
				var response = await client.GetAsync("/User/Checklists/Schedules?page=1&id=" + Uri.EscapeDataString(attack));
				var html = await response.Content.ReadAsStringAsync(); response.StatusCode.Should().Be(HttpStatusCode.OK, html);
				html.Should().NotContain("<script>").And.NotContain("<a onmouseover=").And.Contain("&lt;script&gt;");
				var links = Regex.Matches(html, "href=\"([^\"]+)\"").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
				// No href anywhere on the page may break out of its attribute, including the
				// module chrome (breadcrumb and tab strip) that carries no untrusted input.
				links.Should().OnlyContain(link => !link.Contains("<") && !link.Contains("\""));
				var untrusted = links.Where(link => Uri.UnescapeDataString(link).Contains(attack)).ToList();
				untrusted.Should().HaveCount(canEdit ? 5 : 3, "each edit/navigation link must remain one attribute");
			}
			finally { await app.StopAsync(); ClaimsAuthorizationHelper._httpContextAccessor = previous; }
		}
	}
}

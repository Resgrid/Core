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
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Contacts;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	[Area("User")]
	public class ContactOccupanciesRenderingController : Controller
	{
		public IActionResult Links() => PartialView("/Areas/User/Views/Contacts/_ContactOccupancies.cshtml", new ViewContactView {
			Occupancies = new List<OccupancyLocationSummary> {
				new OccupancyLocationSummary { OccupancyId = "site-1", Name = "<script>site</script>", AddressText = "110 S Main St",
					City = "rgdp:protected-city", OccupancyNumber = "O-1", Role = (int)RmsOccupancyContactRole.Site, IsPrimary = true },
				new OccupancyLocationSummary { OccupancyId = "site-2", Name = "rgdp:protected-name", AddressText = "900 Industrial Pkwy",
					City = "Springfield", StateProvince = "IL", Role = (int)RmsOccupancyContactRole.Owner }
			} });
	}

	[TestFixture, NonParallelizable]
	public class ContactOccupanciesRenderingTests
	{
		[TestCase(true)]
		[TestCase(false)]
		public async Task Linked_occupancies_render_safe_details_links_only_with_records_permission(bool allowed)
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization(); builder.Services.AddAdminAssistFieldHelpStubs();
			builder.Services.AddWebOptimizer(); builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			builder.Services.AddControllersWithViews().AddApplicationPart(typeof(Resgrid.Web.Areas.User.Controllers.RecordOccupanciesController).Assembly)
				.AddApplicationPart(typeof(ContactOccupanciesRenderingController).Assembly);
			await using var app = builder.Build();
			var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.Use(async (context, next) => {
				var claims = new List<Claim> { new Claim(ClaimTypes.PrimarySid, "user"), new Claim(ClaimTypes.PrimaryGroupSid, "12") };
				if (allowed) claims.Add(new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View));
				context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
				try { await next(); } catch (Exception ex) { context.Response.StatusCode = 500; await context.Response.WriteAsync(ex.ToString()); }
			});
			app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("en").AddSupportedCultures("en").AddSupportedUICultures("en"));
			app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}");
			try
			{
				await app.StartAsync();
				using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
				var response = await client.GetAsync("/User/ContactOccupanciesRendering/Links");
				var html = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.OK, html);
				if (allowed)
				{
					html.Should().Contain("/User/RecordOccupancies/Details/site-1").And.Contain("/User/RecordOccupancies/Details/site-2")
						.And.Contain("&lt;script&gt;site&lt;/script&gt;").And.Contain("110 S Main St").And.Contain("900 Industrial Pkwy")
						.And.Contain("Primary").And.Contain("Owner").And.Contain(ProtectedDataEnvelope.RedactionValue)
						.And.NotContain("<script>").And.NotContain("rgdp:");
				}
				else html.Should().BeNullOrWhiteSpace("occupancy details must not leak to a contact reader without Records view permission");
			}
			finally { ClaimsAuthorizationHelper._httpContextAccessor = previous; await app.StopAsync(); }
		}
	}
}

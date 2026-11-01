using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>The dashboard's MVC surface (home-dashboard-redesign-plan.md §4.4, §9): department policy, anti-forgery on every write.</summary>
	[TestFixture]
	public class DashboardControllerSurfaceTests
	{
		[Test]
		public void The_controller_requires_the_department_view_policy()
		{
			typeof(DashboardController).GetCustomAttributes<AuthorizeAttribute>()
				.Should().Contain(a => a.Policy == ResgridResources.Department_View);
		}

		[Test]
		public void Every_post_validates_the_anti_forgery_token()
		{
			var posts = typeof(DashboardController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
				.Where(m => m.GetCustomAttribute<HttpPostAttribute>() != null)
				.ToList();

			posts.Should().NotBeEmpty();
			posts.Where(m => m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() == null).Select(m => m.Name)
				.Should().BeEmpty("every dashboard write is a cross-site request target");
		}

		[Test]
		public void Widget_reads_are_never_cached()
		{
			foreach (var name in new[] { nameof(DashboardController.Bootstrap), nameof(DashboardController.WidgetData), nameof(DashboardController.WidgetPreview), nameof(DashboardController.Pulse) })
			{
				var cache = typeof(DashboardController).GetMethod(name)!.GetCustomAttribute<ResponseCacheAttribute>();
				cache.Should().NotBeNull(name);
				cache!.NoStore.Should().BeTrue($"{name} can carry revealed protected data or per-viewer state");
			}
		}

		[Test]
		public void Widget_data_takes_no_settings_from_the_request()
		{
			var parameters = typeof(DashboardController).GetMethod(nameof(DashboardController.WidgetData))!.GetParameters().Select(p => p.Name);

			parameters.Should().NotContain("settings", "stored instances use their stored, validated settings (plan §4.4)");
		}
	}
}

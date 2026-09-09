using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture, NonParallelizable]
	public class ReadinessBillingReviewTests
	{
		[Test]
		public async Task Both_addon_helpers_bound_unresponsive_billing_requests()
		{
			var previousUrl = SystemBehaviorConfig.BillingApiBaseUrl;
			var previousKey = ApiConfig.BackendInternalApikey;
			var previousCache = SystemBehaviorConfig.CacheEnabled;
			var builder = WebApplication.CreateBuilder();
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
			await using var app = builder.Build();
			app.MapMethods("/api/Billing/{action}", new[] { "GET", "POST" }, async context =>
				await Task.Delay(TimeSpan.FromSeconds(30), context.RequestAborted));
			try
			{
				await app.StartAsync();
				SystemBehaviorConfig.BillingApiBaseUrl = app.Urls.Single(); ApiConfig.BackendInternalApikey = "test-only"; SystemBehaviorConfig.CacheEnabled = false;
				var service = new SubscriptionsService(null, null, null, null, null, null, null, null);
				var plans = service.GetAllAddonPlansByTypeAsync(PlanAddonTypes.ReadinessPro);
				var payments = service.GetCurrentPaymentAddonsForDepartmentAsync(77, new List<string> { "readiness" });
				await Task.WhenAll(plans, payments).WaitAsync(TimeSpan.FromSeconds(12));
				(await plans).Should().BeNull(); (await payments).Should().BeNull();
			}
			finally
			{
				SystemBehaviorConfig.BillingApiBaseUrl = previousUrl; ApiConfig.BackendInternalApikey = previousKey; SystemBehaviorConfig.CacheEnabled = previousCache;
				await app.StopAsync();
			}
		}

		[Test]
		public async Task Readiness_plan_metadata_uses_the_shared_cache_with_a_short_expiration()
		{
			var previousUrl = SystemBehaviorConfig.BillingApiBaseUrl;
			var previousKey = ApiConfig.BackendInternalApikey;
			var previousCache = SystemBehaviorConfig.CacheEnabled;
			try
			{
				SystemBehaviorConfig.BillingApiBaseUrl = "http://127.0.0.1:1"; ApiConfig.BackendInternalApikey = "test-only"; SystemBehaviorConfig.CacheEnabled = true;
				var plans = new List<PlanAddon> { new PlanAddon { PlanAddonId = "readiness", AddonType = (int)PlanAddonTypes.ReadinessPro } };
				var cache = new Mock<ICacheProvider>(MockBehavior.Strict);
				cache.Setup(c => c.RetrieveAsync($"AddonPlansByType_{(int)PlanAddonTypes.ReadinessPro}", It.IsAny<Func<Task<List<PlanAddon>>>>(), TimeSpan.FromMinutes(5))).ReturnsAsync(plans);
				var service = new SubscriptionsService(null, null, cache.Object, null, null, null, null, null);
				(await service.GetAllAddonPlansByTypeAsync(PlanAddonTypes.ReadinessPro)).Should().BeSameAs(plans);
				cache.VerifyAll(); cache.VerifyNoOtherCalls();
			}
			finally { SystemBehaviorConfig.BillingApiBaseUrl = previousUrl; ApiConfig.BackendInternalApikey = previousKey; SystemBehaviorConfig.CacheEnabled = previousCache; }
		}
	}
}

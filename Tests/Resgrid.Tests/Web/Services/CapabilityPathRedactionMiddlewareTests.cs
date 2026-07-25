using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Resgrid.Web.Services.Middleware;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class CapabilityPathRedactionMiddlewareTests
	{
		[Test]
		public async Task InvokeAsync_CapabilityPath_HidesTokenFromDownstreamPipeline()
		{
			const string token = "rgtrk_prefix12_super-secret-capability";
			var context = new DefaultHttpContext();
			context.Request.Path = $"/api/v4/unit-trackers/c/{token}";
			string downstreamPath = null;
			var middleware = new CapabilityPathRedactionMiddleware(nextContext =>
			{
				downstreamPath = nextContext.Request.Path.Value;
				return Task.CompletedTask;
			});

			await middleware.InvokeAsync(context);

			downstreamPath.Should().Be(CapabilityPathRedactionMiddleware.RedactedCapabilityPath);
			downstreamPath.Should().NotContain(token);
			context.Items[CapabilityPathRedactionMiddleware.CapabilityTokenItemKey]
				.Should().Be(token);
		}

		[Test]
		public void RedactCapabilityUrl_FullUrl_PreservesQueryWithoutSecret()
		{
			const string token = "rgtrk_prefix12_super-secret-capability";
			var redacted = CapabilityPathRedactionMiddleware.RedactCapabilityUrl(
				$"https://api.example/api/v4/unit-trackers/c/{token}?source=test");

			redacted.Should().Be(
				"https://api.example/api/v4/unit-trackers/c/[REDACTED]?source=test");
			redacted.Should().NotContain(token);
		}

		[Test]
		public async Task InvokeAsync_InvalidCapabilitySubpath_StillHidesToken()
		{
			const string token = "rgtrk_prefix12_super-secret-capability";
			var context = new DefaultHttpContext();
			context.Request.Path = $"/api/v4/unit-trackers/c/{token}/unexpected";
			string downstreamPath = null;
			var middleware = new CapabilityPathRedactionMiddleware(nextContext =>
			{
				downstreamPath = nextContext.Request.Path.Value;
				return Task.CompletedTask;
			});

			await middleware.InvokeAsync(context);

			downstreamPath.Should().Be(
				CapabilityPathRedactionMiddleware.RedactedCapabilityPath + "/unexpected");
			downstreamPath.Should().NotContain(token);
		}
	}
}

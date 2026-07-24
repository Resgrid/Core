using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Web.Services.ApplicationCore.UnitTracking;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	[NonParallelizable]
	public class UnitTrackingRateLimiterTests
	{
		private int _originalRequestLimit;
		private int _originalRecordLimit;

		[SetUp]
		public void SetUp()
		{
			_originalRequestLimit = UnitTrackingConfig.PerDeviceRequestsPerMinute;
			_originalRecordLimit = UnitTrackingConfig.PerDeviceRecordsPerMinute;
			UnitTrackingConfig.PerDeviceRequestsPerMinute = 1;
			UnitTrackingConfig.PerDeviceRecordsPerMinute = 2;
		}

		[TearDown]
		public void TearDown()
		{
			UnitTrackingConfig.PerDeviceRequestsPerMinute = _originalRequestLimit;
			UnitTrackingConfig.PerDeviceRecordsPerMinute = _originalRecordLimit;
		}

		[Test]
		public void CheckRequest_SecondRequestWithinWindow_IsRateLimited()
		{
			using var cache = new MemoryCache(new MemoryCacheOptions());
			var limiter = new UnitTrackingRateLimiter(cache);

			limiter.CheckRequest("device-1", "credential-1").Allowed.Should().BeTrue();
			var second = limiter.CheckRequest("device-1", "credential-1");

			second.Allowed.Should().BeFalse();
			second.RetryAfterSeconds.Should().BePositive();
		}

		[Test]
		public void CheckRecords_BatchOverLimit_IsRateLimitedWithoutAllocationByRecord()
		{
			using var cache = new MemoryCache(new MemoryCacheOptions());
			var limiter = new UnitTrackingRateLimiter(cache);

			var result = limiter.CheckRecords("device-1", "credential-1", 3);

			result.Allowed.Should().BeFalse();
		}
	}
}

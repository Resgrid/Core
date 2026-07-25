using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Resgrid.Config;

namespace Resgrid.Web.Services.ApplicationCore.UnitTracking
{
	public sealed class UnitTrackingRateLimitResult
	{
		public bool Allowed { get; set; }
		public int RetryAfterSeconds { get; set; }
	}

	public class UnitTrackingRateLimiter
	{
		private readonly IMemoryCache _cache;
		private readonly object _counterCreationSync = new();

		public UnitTrackingRateLimiter(IMemoryCache cache)
		{
			_cache = cache;
		}

		public UnitTrackingRateLimitResult CheckUnknownEndpoint(string sourceIp)
		{
			return Check(
				"unknown",
				sourceIp ?? "unknown",
				1,
				Math.Max(1, UnitTrackingConfig.UnknownEndpointRequestsPerMinute));
		}

		public UnitTrackingRateLimitResult CheckRequest(string deviceId, string credentialId)
		{
			var maximum = Math.Max(1, UnitTrackingConfig.PerDeviceRequestsPerMinute);
			var binding = Check("request-binding", deviceId, 1, maximum);
			if (!binding.Allowed)
				return binding;

			return Check("request-credential", credentialId, 1, maximum);
		}

		public UnitTrackingRateLimitResult CheckRecords(
			string deviceId,
			string credentialId,
			int recordCount)
		{
			var maximum = Math.Max(1, UnitTrackingConfig.PerDeviceRecordsPerMinute);
			var binding = Check("record-binding", deviceId, recordCount, maximum);
			if (!binding.Allowed)
				return binding;

			return Check("record-credential", credentialId, recordCount, maximum);
		}

		private UnitTrackingRateLimitResult Check(
			string scope,
			string identity,
			int amount,
			int maximum)
		{
			if (string.IsNullOrWhiteSpace(identity))
				identity = "unknown";

			var now = DateTime.UtcNow;
			var key = $"UnitTrackingRate:{Digest($"{scope}|{identity}")}";
			WindowCounter counter;
			lock (_counterCreationSync)
			{
				counter = _cache.GetOrCreate(
					key,
					entry =>
					{
						entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
						return new WindowCounter(now);
					});
			}

			lock (counter.Sync)
			{
				if (now >= counter.WindowStartedOn.AddMinutes(1))
				{
					counter.WindowStartedOn = now;
					counter.Count = 0;
				}

				var retryAfter = Math.Max(
					1,
					(int)Math.Ceiling(
						(counter.WindowStartedOn.AddMinutes(1) - now).TotalSeconds));
				if (amount <= 0 || amount > maximum || counter.Count > maximum - amount)
				{
					return new UnitTrackingRateLimitResult
					{
						Allowed = false,
						RetryAfterSeconds = retryAfter
					};
				}

				counter.Count += amount;
				return new UnitTrackingRateLimitResult { Allowed = true };
			}
		}

		private static string Digest(string value) =>
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
				.ToLowerInvariant();

		private sealed class WindowCounter
		{
			public WindowCounter(DateTime windowStartedOn)
			{
				WindowStartedOn = windowStartedOn;
			}

			public object Sync { get; } = new();
			public DateTime WindowStartedOn { get; set; }
			public int Count { get; set; }
		}
	}
}

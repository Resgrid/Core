using System;
using System.Security.Cryptography;
using System.Text;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class UnitTrackingEventIdService : IUnitTrackingEventIdService
	{
		private const int MaximumCallerEventIdLength = 256;

		public string CreateForHttps(string unitTrackingDeviceId, string callerEventId)
		{
			if (string.IsNullOrWhiteSpace(unitTrackingDeviceId))
				throw new ArgumentNullException(nameof(unitTrackingDeviceId));
			if (string.IsNullOrWhiteSpace(callerEventId))
				throw new ArgumentNullException(nameof(callerEventId));

			var normalizedCallerEventId = callerEventId.Trim();
			if (normalizedCallerEventId.Length > MaximumCallerEventIdLength)
			{
				throw new ArgumentOutOfRangeException(
					nameof(callerEventId),
					$"Tracking event identifiers cannot exceed {MaximumCallerEventIdLength} characters.");
			}

			var input = $"https|{unitTrackingDeviceId.Trim()}|{normalizedCallerEventId}";
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))
				.ToLowerInvariant();
		}
	}
}

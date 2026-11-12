using System.Threading;
using System.Threading.Tasks;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Helpers
{
	/// <summary>
	/// Dispatch ranking asks <see cref="IGeoService.GetTravelEstimateAsync"/> for travel times (M0314). Tests written against
	/// the older drive-time calls set up <see cref="IGeoService.GetEtaInSecondsAsync(string, string)"/> (and its vehicle
	/// overload); this routes the new call through them, so those setups and verifications keep describing what was asked.
	/// Air travel answers itself as the real service does (straight line at the unit's speed).
	/// </summary>
	public static class GeoServiceMocks
	{
		public static Mock<IGeoService> BridgeTravelEstimates(this Mock<IGeoService> geo)
		{
			geo.Setup(x => x.GetTravelEstimateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TravelLookup>(), It.IsAny<CancellationToken>()))
				.Returns((string start, string destination, TravelLookup lookup, CancellationToken token) => BridgeAsync(geo, start, destination, lookup));

			return geo;
		}

		private static async Task<TravelEstimate> BridgeAsync(Mock<IGeoService> geo, string start, string destination, TravelLookup lookup)
		{
			var vehicle = lookup?.Travel == null || lookup.Travel.Mode == UnitTravelModes.Driving ? lookup?.Vehicle : null;

			var seconds = vehicle == null
				? await geo.Object.GetEtaInSecondsAsync(start, destination)
				: await geo.Object.GetEtaInSecondsAsync(start, destination, vehicle);

			return new TravelEstimate { Seconds = seconds, Source = seconds >= 0 ? EtaSources.Road : EtaSources.None };
		}
	}
}

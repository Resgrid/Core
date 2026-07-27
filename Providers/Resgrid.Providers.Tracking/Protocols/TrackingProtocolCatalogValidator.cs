using System;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Tracking;

namespace Resgrid.Providers.Tracking.Protocols
{
	public static class TrackingProtocolCatalogValidator
	{
		public static void Validate(
			ITrackingProtocolModuleRegistry moduleRegistry)
		{
			if (moduleRegistry == null)
				throw new ArgumentNullException(
					nameof(moduleRegistry));

			var requirements = UnitTrackingCatalog.Profiles
				.Where(profile =>
					profile.TransportType ==
					UnitTrackingTransportType.NativeTcpUdp)
				.SelectMany(profile =>
					profile.SupportedTransports.Select(
						transport => new
						{
							profile.ProtocolKey,
							Transport = ParseTransport(
								profile.Key,
								transport)
						}))
				.Distinct()
				.ToList();

			foreach (var requirement in requirements)
			{
				if (!moduleRegistry.TryResolve(
					    requirement.ProtocolKey,
					    requirement.Transport,
					    out _))
				{
					throw new InvalidOperationException(
						$"The unit tracking catalog references unregistered protocol '{requirement.ProtocolKey}' over {requirement.Transport}.");
				}
			}
		}

		private static TrackingSocketTransport ParseTransport(
			string profileKey,
			string transport)
		{
			if (string.Equals(
				    transport,
				    "Tcp",
				    StringComparison.Ordinal))
				return TrackingSocketTransport.Tcp;
			if (string.Equals(
				    transport,
				    "Udp",
				    StringComparison.Ordinal))
				return TrackingSocketTransport.Udp;

			throw new InvalidOperationException(
				$"Native unit tracking profile '{profileKey}' has an invalid socket transport.");
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Providers.Tracking.Protocols
{
	public class TrackingProtocolModuleRegistry : ITrackingProtocolModuleRegistry
	{
		private readonly Dictionary<string, ITrackingProtocolModule> _modulesByKey;

		public TrackingProtocolModuleRegistry(IEnumerable<ITrackingProtocolModule> modules)
		{
			var registeredModules = (modules ?? Enumerable.Empty<ITrackingProtocolModule>())
				.Where(module => module != null)
				.ToList();
			_modulesByKey = new Dictionary<string, ITrackingProtocolModule>(
				StringComparer.OrdinalIgnoreCase);

			foreach (var module in registeredModules)
			{
				if (string.IsNullOrWhiteSpace(module.ProtocolKey))
					throw new InvalidOperationException("Tracking protocol modules must declare a protocol key.");
				if (module.SupportedTransports == null || module.SupportedTransports.Count == 0)
				{
					throw new InvalidOperationException(
						$"Tracking protocol module '{module.ProtocolKey}' must declare at least one transport.");
				}
				if (module.SupportedTransports.Any(
					    transport => transport != TrackingSocketTransport.Tcp &&
					                 transport != TrackingSocketTransport.Udp))
				{
					throw new InvalidOperationException(
						$"Tracking protocol module '{module.ProtocolKey}' declares an unsupported transport.");
				}

				var protocolKey = module.ProtocolKey.Trim();
				if (!_modulesByKey.TryAdd(protocolKey, module))
				{
					throw new InvalidOperationException(
						$"Tracking protocol module '{protocolKey}' is registered more than once.");
				}
			}

			Modules = registeredModules.AsReadOnly();
		}

		public IReadOnlyCollection<ITrackingProtocolModule> Modules { get; }

		public bool TryResolve(
			string protocolKey,
			TrackingSocketTransport transport,
			out ITrackingProtocolModule module)
		{
			module = null;
			if (string.IsNullOrWhiteSpace(protocolKey) ||
			    transport == TrackingSocketTransport.Unknown ||
			    !_modulesByKey.TryGetValue(protocolKey.Trim(), out var registeredModule) ||
			    !registeredModule.SupportedTransports.Contains(transport))
				return false;

			module = registeredModule;
			return true;
		}

		public ITrackingProtocolModule Resolve(
			string protocolKey,
			TrackingSocketTransport transport)
		{
			if (TryResolve(protocolKey, transport, out var module))
				return module;

			throw new KeyNotFoundException(
				$"No tracking protocol module is registered for '{protocolKey}' over {transport}.");
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.TrackerGateway.Listeners;

namespace Resgrid.TrackerGateway.Hosting
{
	public sealed class TrackingListenerPlanBuilder
	{
		private const int MaximumFrameBytes = 1024 * 1024;

		public TrackingListenerPlan Build(
			TrackingGatewaySettings settings,
			ITrackingProtocolModuleRegistry moduleRegistry,
			ITrackingListenerFactory listenerFactory)
		{
			if (settings == null)
				throw new ArgumentNullException(nameof(settings));
			if (moduleRegistry == null)
				throw new ArgumentNullException(nameof(moduleRegistry));
			if (listenerFactory == null)
				throw new ArgumentNullException(nameof(listenerFactory));

			var errors = new List<string>();
			var definitions = new List<TrackingListenerDefinition>();

			ValidatePort(settings.InternalHealthPort, "InternalHealthPort", errors);

			if (!settings.NativeGatewayEnabled)
			{
				ThrowIfInvalid(errors);
				return new TrackingListenerPlan(definitions);
			}

			if (!settings.TrackingEnabled)
			{
				errors.Add(
					"UnitTrackingConfig.Enabled must be true when NativeGatewayEnabled is true.");
			}

			if (string.IsNullOrWhiteSpace(settings.CredentialPepper))
			{
				errors.Add(
					"UnitTrackingConfig.CredentialPepper must be supplied through secret management.");
			}

			ValidatePositive(
				settings.TcpIdleTimeoutSeconds,
				"TcpIdleTimeoutSeconds",
				errors);
			ValidateRange(
				settings.MaxFrameBytes,
				1,
				MaximumFrameBytes,
				"MaxFrameBytes",
				errors);
			ValidatePositive(settings.MaxConnections, "MaxConnections", errors);
			ValidatePositive(
				settings.MaxConnectionsPerIp,
				"MaxConnectionsPerIp",
				errors);
			ValidatePositive(
				settings.GracefulShutdownSeconds,
				"GracefulShutdownSeconds",
				errors);

			if (settings.MaxConnectionsPerIp > settings.MaxConnections)
			{
				errors.Add(
					"UnitTrackingConfig.MaxConnectionsPerIp cannot exceed MaxConnections.");
			}

			var enabledProtocols = settings.Protocols
				.Where(protocol => protocol != null && protocol.Enabled)
				.ToList();
			if (enabledProtocols.Count == 0)
			{
				errors.Add(
					"At least one native tracking protocol must be enabled.");
			}

			foreach (var protocol in enabledProtocols)
			{
				var enabledTransports = new[]
				{
					TrackingSocketTransport.Tcp,
					TrackingSocketTransport.Udp
				}.Where(protocol.IsEnabled).ToList();
				if (enabledTransports.Count == 0)
				{
					errors.Add(
						$"Tracking protocol '{protocol.ProtocolKey}' must enable at least one transport.");
					continue;
				}

				var module = moduleRegistry.Modules.SingleOrDefault(
					candidate => string.Equals(
						candidate.ProtocolKey.Trim(),
						protocol.ProtocolKey,
						StringComparison.OrdinalIgnoreCase));
				if (module == null)
				{
					errors.Add(
						$"No tracking protocol module is registered for '{protocol.ProtocolKey}'.");
					continue;
				}

				foreach (var transport in enabledTransports)
				{
					if (!module.SupportedTransports.Contains(transport))
					{
						errors.Add(
							$"Tracking protocol module '{protocol.ProtocolKey}' does not support enabled transport '{transport}'.");
						continue;
					}

					var port = protocol.GetPort(transport);
					var portName = $"{protocol.ProtocolKey} {transport} port";
					if (!ValidatePort(port, portName, errors))
						continue;

					var definition = new TrackingListenerDefinition(
						protocol.ProtocolKey,
						transport,
						port);
					if (!listenerFactory.Supports(definition))
					{
						errors.Add(
							$"No socket listener implementation is registered for {definition}.");
						continue;
					}

					definitions.Add(definition);
				}
			}

			foreach (var duplicate in definitions
				         .GroupBy(definition => new
				         {
					         definition.Transport,
					         definition.Port
				         })
				         .Where(group => group.Count() > 1))
			{
				errors.Add(
					$"Port {duplicate.Key.Port} is assigned to more than one {duplicate.Key.Transport} listener.");
			}

			if (definitions.Any(
				    definition => definition.Transport == TrackingSocketTransport.Tcp &&
				                  definition.Port == settings.InternalHealthPort))
			{
				errors.Add(
					"InternalHealthPort cannot share a port with a TCP tracking listener.");
			}

			ThrowIfInvalid(errors);
			return new TrackingListenerPlan(definitions);
		}

		private static void ValidatePositive(
			int value,
			string fieldName,
			ICollection<string> errors)
		{
			if (value <= 0)
				errors.Add($"UnitTrackingConfig.{fieldName} must be greater than zero.");
		}

		private static void ValidateRange(
			int value,
			int minimum,
			int maximum,
			string fieldName,
			ICollection<string> errors)
		{
			if (value < minimum || value > maximum)
			{
				errors.Add(
					$"UnitTrackingConfig.{fieldName} must be between {minimum} and {maximum}.");
			}
		}

		private static bool ValidatePort(
			int port,
			string fieldName,
			ICollection<string> errors)
		{
			if (port >= 1 && port <= 65535)
				return true;

			errors.Add($"{fieldName} must be between 1 and 65535.");
			return false;
		}

		private static void ThrowIfInvalid(IReadOnlyCollection<string> errors)
		{
			if (errors.Count > 0)
				throw new TrackingGatewayConfigurationException(errors);
		}
	}
}

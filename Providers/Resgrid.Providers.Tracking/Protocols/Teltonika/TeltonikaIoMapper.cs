using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Tracking;

namespace Resgrid.Providers.Tracking.Protocols.Teltonika
{
	internal static class TeltonikaIoMapper
	{
		private const string ProtocolKey =
			"teltonika-codec8";

		private static readonly Lazy<
			HashSet<(int AvlId, int ValueBytes)>>
			Allowlist = new(CreateAllowlist);

		public static bool IsAllowlisted(
			int avlId,
			int valueBytes)
		{
			return Allowlist.Value.Contains(
				(avlId, valueBytes));
		}

		public static void EnrichPositions(
			ProtocolMessage message,
			UnitTrackingDevice device)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));
			if (device == null)
				throw new ArgumentNullException(nameof(device));
			if (message.MessageType !=
				    ProtocolMessageType.Positions ||
			    message.ProtocolData is not
				    TeltonikaProtocolData protocolData)
				return;

			var profile = UnitTrackingCatalog.GetProfile(
				device.ModelKey);
			if (profile == null ||
			    !string.Equals(
				    profile.ProtocolKey,
				    ProtocolKey,
				    StringComparison.OrdinalIgnoreCase) ||
			    !string.Equals(
				    device.ProtocolKey,
				    ProtocolKey,
				    StringComparison.OrdinalIgnoreCase))
				return;

			var ioMap = UnitTrackingCatalog.GetIoMap(
				profile.IoMapKey);
			if (ioMap == null ||
			    !string.Equals(
				    ioMap.ProtocolKey,
				    ProtocolKey,
				    StringComparison.OrdinalIgnoreCase))
				return;

			var positions =
				message.Positions?.ToList() ??
				new List<CanonicalTrackingPosition>();
			if (positions.Count != protocolData.Records.Count)
			{
				throw new InvalidOperationException(
					"Teltonika I/O metadata does not match the decoded position count.");
			}

			for (var index = 0;
			     index < positions.Count;
			     index++)
			{
				foreach (var mapping in ioMap.Mappings)
				{
					if (protocolData.Records[index]
					    .TryGetValue(
						    mapping.AvlId,
						    out var rawValue))
					{
						Apply(
							positions[index],
							mapping,
							rawValue);
					}
				}
			}
		}

		private static HashSet<(int AvlId, int ValueBytes)>
			CreateAllowlist()
		{
			return UnitTrackingCatalog.IoMaps
				.Where(ioMap => string.Equals(
					ioMap.ProtocolKey,
					ProtocolKey,
					StringComparison.OrdinalIgnoreCase))
				.SelectMany(ioMap => ioMap.Mappings)
				.Select(mapping =>
					(mapping.AvlId, mapping.ValueBytes))
				.ToHashSet();
		}

		private static void Apply(
			CanonicalTrackingPosition position,
			UnitTrackingIoMapping mapping,
			ulong rawValue)
		{
			if (rawValue < mapping.MinimumRawValue.Value ||
			    rawValue > mapping.MaximumRawValue.Value)
				return;

			var value = rawValue * mapping.Multiplier;
			switch (mapping.Target)
			{
				case UnitTrackingIoTarget.Hdop:
					position.Hdop = value;
					break;
				case UnitTrackingIoTarget.BatteryPercent:
					if (value >= 0m && value <= 100m)
						position.BatteryPercent = value;
					break;
				case UnitTrackingIoTarget.ExternalPowerVolts:
					if (value >= 0m)
						position.ExternalPowerVolts = value;
					break;
				case UnitTrackingIoTarget.SignalPercent:
					if (value >= 0m &&
					    value <= 100m &&
					    decimal.Truncate(value) == value)
						position.SignalPercent = (int)value;
					break;
				case UnitTrackingIoTarget.Ignition:
					if (rawValue <= 1)
						position.Ignition =
							rawValue == 1;
					break;
				case UnitTrackingIoTarget.IsMoving:
					if (rawValue <= 1)
						position.IsMoving =
							rawValue == 1;
					break;
			}
		}
	}

	internal sealed class TeltonikaProtocolData
	{
		public TeltonikaProtocolData(
			IReadOnlyList<IReadOnlyDictionary<int, ulong>>
				records)
		{
			Records = records ??
				throw new ArgumentNullException(nameof(records));
		}

		public IReadOnlyList<IReadOnlyDictionary<int, ulong>>
			Records { get; }
	}
}

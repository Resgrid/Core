using System;
using System.Collections.Generic;

namespace Resgrid.Model.Tracking
{
	public enum UnitTrackingIoTarget
	{
		Unknown = 0,
		Hdop = 1,
		BatteryPercent = 2,
		ExternalPowerVolts = 3,
		SignalPercent = 4,
		Ignition = 5,
		IsMoving = 6
	}

	public sealed class UnitTrackingIoMap
	{
		public string Key { get; set; }
		public string ProtocolKey { get; set; }
		public string ProtocolDocumentVersion { get; set; }
		public IReadOnlyCollection<UnitTrackingIoMapping> Mappings { get; set; } =
			Array.Empty<UnitTrackingIoMapping>();
	}

	public sealed class UnitTrackingIoMapping
	{
		public int AvlId { get; set; }
		public int ValueBytes { get; set; }
		public UnitTrackingIoTarget Target { get; set; }
		public decimal Multiplier { get; set; } = 1m;
		public ulong? MinimumRawValue { get; set; }
		public ulong? MaximumRawValue { get; set; }
	}
}

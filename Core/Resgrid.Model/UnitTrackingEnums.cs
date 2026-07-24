namespace Resgrid.Model
{
	public enum UnitLocationSourceType
	{
		UnknownLegacy = 0,
		UnitApp = 1,
		HardwareTracker = 2
	}

	public enum UnitTrackingTransportType
	{
		Unknown = 0,
		NativeHttps = 1,
		ManagedHttpsJson = 2,
		NativeTcpUdp = 3,
		ProtocolGateway = 4
	}

	public enum UnitTrackingAuthMode
	{
		Unknown = 0,
		Bearer = 1,
		Basic = 2,
		CustomHeader = 3,
		CapabilityPath = 4
	}

	public enum UnitTrackingDeviceStatus
	{
		NeverSeen = 0,
		Online = 1,
		Stale = 2,
		Error = 3,
		Disabled = 4
	}

	public enum TrackingTimestampSource
	{
		Unknown = 0,
		Device = 1,
		Server = 2
	}

	public enum UnitTrackingCertificationStatus
	{
		Unknown = 0,
		Candidate = 1,
		FixtureVerified = 2,
		HardwareVerified = 3,
		Certified = 4,
		Deprecated = 5
	}
}

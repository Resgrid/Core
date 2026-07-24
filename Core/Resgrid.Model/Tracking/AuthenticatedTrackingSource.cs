namespace Resgrid.Model.Tracking
{
	public sealed class AuthenticatedTrackingSource
	{
		public UnitTrackingDevice Device { get; set; }
		public UnitTrackingCredential Credential { get; set; }
		public string ReportedDeviceIdentifier { get; set; }
	}
}

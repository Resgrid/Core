namespace Resgrid.Model
{
	public sealed class UnitTrackingGeneratedCredential
	{
		public string Token { get; set; }
		public string KeyPrefix { get; set; }
		public string SecretHash { get; set; }
	}

	public sealed class UnitTrackingCredentialProvisionResult
	{
		public UnitTrackingCredential Credential { get; set; }
		public string Token { get; set; }
		public string EndpointUrl { get; set; }
		public string HeaderName { get; set; }
		public string HeaderValue { get; set; }
		public string BasicUsername { get; set; }
	}

	public sealed class UnitTrackingAuthenticationResult
	{
		public UnitTrackingDevice Device { get; set; }
		public UnitTrackingCredential Credential { get; set; }
	}

	public sealed class ResolvedUnitLocation
	{
		public UnitsLocation Location { get; set; }
		public bool IsStale { get; set; }
	}
}

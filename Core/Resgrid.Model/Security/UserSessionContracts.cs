using System;
using System.Collections.Generic;

namespace Resgrid.Model.Security
{
	public class SessionIssueContext
	{
		public string UserId { get; set; }
		public int? DepartmentId { get; set; }
		public long AuthenticationGeneration { get; set; }
		public UserSessionClientApplication ClientApplication { get; set; }
		public string ClientInstanceIdHash { get; set; }
		public string DeviceName { get; set; }
		public string DeviceType { get; set; }
		public string OperatingSystem { get; set; }
		public string Browser { get; set; }
		public string ApplicationVersion { get; set; }
		public UserSessionAuthenticationMethod AuthenticationMethod { get; set; }
		public string DepartmentSsoConfigId { get; set; }
		public string OpenIddictAuthorizationId { get; set; }
		public string WebCookieTicketKey { get; set; }
		public DateTime ExpiresOn { get; set; }
		public string IpAddress { get; set; }
		public string Country { get; set; }
		public string Region { get; set; }
		public string City { get; set; }
		public string UserAgent { get; set; }
		public bool IsLegacyAdopted { get; set; }
	}

	public class SessionPrincipalContext
	{
		public string UserId { get; set; }
		public string SessionId { get; set; }
		public long? AuthenticationGeneration { get; set; }
		public int? DepartmentId { get; set; }
		public DateTime? CredentialIssuedOn { get; set; }
	}

	public class LegacySessionContext : SessionIssueContext
	{
		public string StableCredentialIdentifier { get; set; }
	}

	public class RequestActivity
	{
		public DateTime OccurredOn { get; set; }
		public string IpAddress { get; set; }
		public string Country { get; set; }
		public string Region { get; set; }
		public string City { get; set; }
		public string UserAgent { get; set; }
	}

	public class ClientSessionMetadata
	{
		public string DeviceName { get; set; }
		public string DeviceType { get; set; }
		public string OperatingSystem { get; set; }
		public string Browser { get; set; }
		public string ApplicationVersion { get; set; }
	}

	public class IpLocationResult
	{
		public string Country { get; set; }
		public string Region { get; set; }
		public string City { get; set; }
		public bool IsKnown => !string.IsNullOrWhiteSpace(Country) || !string.IsNullOrWhiteSpace(Region) ||
			!string.IsNullOrWhiteSpace(City);
	}

	public class SessionValidationResult
	{
		public bool IsValid { get; set; }
		public bool CanAdoptLegacy { get; set; }
		public string FailureCode { get; set; }
		public UserSession Session { get; set; }

		public static SessionValidationResult Valid(UserSession session = null, bool canAdoptLegacy = false) =>
			new SessionValidationResult { IsValid = true, CanAdoptLegacy = canAdoptLegacy, Session = session };

		public static SessionValidationResult Invalid(string code) =>
			new SessionValidationResult { IsValid = false, FailureCode = code };
	}

	public class UserSessionSummary
	{
		public string UserSessionId { get; set; }
		public int? DepartmentId { get; set; }
		public UserSessionState State { get; set; }
		public UserSessionClientApplication ClientApplication { get; set; }
		public string DeviceName { get; set; }
		public string DeviceType { get; set; }
		public string OperatingSystem { get; set; }
		public string Browser { get; set; }
		public string ApplicationVersion { get; set; }
		public UserSessionAuthenticationMethod AuthenticationMethod { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime LastActiveOn { get; set; }
		public DateTime ExpiresOn { get; set; }
		public string LastIpAddress { get; set; }
		public string LastCountry { get; set; }
		public string LastRegion { get; set; }
		public string LastCity { get; set; }
		public string UserAgent { get; set; }
		public bool IsLegacyAdopted { get; set; }
		public bool IsCurrent { get; set; }
	}

	public class RevocationResult
	{
		public int RevokedSessionCount { get; set; }
		public DateTime RevokedOn { get; set; }
	}

	public class SsoManagementState
	{
		public bool IsSsoManaged { get; set; }
		public bool IsScimManaged { get; set; }
		public bool IsEmailExternallyManaged { get; set; }
		public IReadOnlyList<string> ProviderNames { get; set; } = Array.Empty<string>();
	}
}

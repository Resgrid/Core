using System;
using Newtonsoft.Json.Linq;

namespace Resgrid.Web.Services.Models.v4.Sso
{
	/// <summary>
	/// The department's provider step-up mapping (passkey plan section 7.8) on its active SSO configuration, and whether it
	/// is effective: a mapping counts only after a successful test at its current version.
	/// </summary>
	public class FederatedMfaMappingResult : StandardApiResponseV4Base
	{
		public FederatedMfaMappingResultData Data { get; set; }
	}

	public class FederatedMfaMappingResultData
	{
		public string DepartmentSsoConfigId { get; set; }

		/// <summary>oidc or saml2: which mapping fields apply.</summary>
		public string ProviderType { get; set; }

		/// <summary>
		/// The mapping: <c>requestAcrValues</c>, <c>requestClaims</c> (OIDC), <c>requestAuthnContextClassRefs</c> (SAML), and
		/// what counts as MFA: <c>acceptAmr</c>, <c>acceptAcr</c>, <c>acceptAcrs</c> (OIDC) or <c>acceptAuthnContextClassRefs</c>
		/// (SAML). Null when none is set.
		/// </summary>
		public JToken Mapping { get; set; }

		public long MappingVersion { get; set; }

		public long? TestedVersion { get; set; }

		public DateTime? TestedOn { get; set; }

		public string TestedByUserId { get; set; }

		/// <summary>True when the mapping passed its test at the current version and so can be used.</summary>
		public bool Effective { get; set; }
	}

	public class SaveFederatedMfaMappingInput
	{
		/// <summary>The mapping object (see <see cref="FederatedMfaMappingResultData.Mapping"/>), or null to remove it.</summary>
		public JToken Mapping { get; set; }
	}

	/// <summary>Starts the managing member's test step-up with the saved mapping, through brokered SSO.</summary>
	public class FederatedMfaTestBeginInput
	{
		public string ReturnTarget { get; set; }
		public string State { get; set; }
		public string CodeChallenge { get; set; }
		public string CodeChallengeMethod { get; set; }
		public string Platform { get; set; }
	}

	public class FederatedMfaTestCompleteInput
	{
		public string SsoTransactionId { get; set; }
		public string SsoCode { get; set; }
		public string CodeVerifier { get; set; }
	}

	public class FederatedMfaTestResult : StandardApiResponseV4Base
	{
		public FederatedMfaTestResultData Data { get; set; }
	}

	public class FederatedMfaTestResultData
	{
		public bool Tested { get; set; }

		/// <summary>The returned value the mapping counted as MFA, as <c>kind:value</c>.</summary>
		public string MatchedValue { get; set; }

		public long MappingVersion { get; set; }
	}
}

using System;
using Microsoft.AspNetCore.Http;
using Resgrid.Model;
using Resgrid.Web.Attributes;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// The step-up a guarded Web action may offer a signature: what <see cref="RequiresRecentTwoFactorAttribute"/> put on the request after
	/// reading this session's server-side evidence (a second factor, or the password / SSO entered again where the department's rule
	/// allows it). Never from the form or a query string. The signing service decides the level again under the same rule.
	/// </summary>
	public static class WebStepUpProof
	{
		public static SignatureProof Apply(SignatureProof proof, HttpContext httpContext)
		{
			if (proof == null || httpContext?.Items == null)
				return proof;

			proof.StepUpVerifiedOnUtc = httpContext.Items[RequiresRecentTwoFactorAttribute.MfaVerifiedAtHttpContextItemKey] as DateTime?;
			proof.StepUpMethod = proof.StepUpVerifiedOnUtc.HasValue
				? httpContext.Items[RequiresRecentTwoFactorAttribute.MfaMethodHttpContextItemKey] as int?
				: null;
			proof.ReauthenticatedOnUtc = httpContext.Items[RequiresRecentTwoFactorAttribute.ReauthenticatedAtHttpContextItemKey] as DateTime?;
			proof.ReauthenticationMethod = proof.ReauthenticatedOnUtc.HasValue
				? httpContext.Items[RequiresRecentTwoFactorAttribute.ReauthenticationMethodHttpContextItemKey] as int?
				: null;
			return proof;
		}
	}
}

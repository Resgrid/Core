using System;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Model;
using Resgrid.Web.Services.Middleware;

namespace Resgrid.Web.Services.Attributes
{
	/// <summary>
	/// Opens a v4 action to department API keys holding <see cref="Scope"/>. It adds the key scheme to the action's
	/// schemes (no other action runs it, so a key is refused everywhere else) and the scope policy, which a key principal
	/// passes only with the scope and every other principal passes unchanged. The action's own policy still applies.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
	public sealed class DepartmentApiKeyScopeAttribute : AuthorizeAttribute
	{
		public DepartmentApiKeyScopeAttribute(string scope)
		{
			if (!DepartmentApiKeyScopes.IsKnown(scope))
				throw new ArgumentException($"Unknown department API key scope '{scope}'.", nameof(scope));

			Scope = scope;
			AuthenticationSchemes = DepartmentApiKeyAuthHandler.SchemeName;
			Policy = DepartmentApiKeyScopes.PolicyName(scope);
		}

		public string Scope { get; }
	}
}

namespace Resgrid.Model
{
	/// <summary>
	/// Scope strings carried in a Protected Data Grant (plan section 3.2 "permissions/scope").
	/// Version 1 grants the two coarse operation scopes below; per-family and per-permission
	/// narrowing (Calls vs Personnel vs Contacts, export, egress override, break-glass) joins when
	/// the protected read-path DTOs ship and enforcement points can request a narrower scope.
	/// Enforcement of the specific ADP PermissionTypes (31-39) remains a server-side authorization
	/// check at every operation — a scope in a grant never substitutes for it.
	/// </summary>
	public static class ProtectedDataGrantScopes
	{
		/// <summary>Decrypt/read protected field values.</summary>
		public const string Read = "protected:read";

		/// <summary>Encrypt/write protected field values.</summary>
		public const string Write = "protected:write";
	}
}

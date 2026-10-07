using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// What a department API key may do. Each v4 endpoint a key can call is marked with exactly one of these scopes;
	/// an endpoint without a scope cannot be called with a key at all, whatever scopes the key holds.
	/// The values are stored on <see cref="DepartmentApiKey.Scopes"/> and are part of the public API: never rename one.
	/// </summary>
	public static class DepartmentApiKeyScopes
	{
		/// <summary>Read calls: active, pending, scheduled, a single call, calls in a date range, the new-call field policy.</summary>
		public const string CallsRead = "calls.read";

		/// <summary>Create calls, including pending (to be dispatched) and scheduled calls.</summary>
		public const string CallsCreate = "calls.create";

		/// <summary>Edit calls, dispatch a pending or scheduled call now, change a scheduled dispatch time.</summary>
		public const string CallsUpdate = "calls.update";

		/// <summary>Close calls.</summary>
		public const string CallsClose = "calls.close";

		/// <summary>Read the department's call types, priorities, templates, groups and roles (to build a call and its dispatch list).</summary>
		public const string ReferenceRead = "reference.read";

		/// <summary>Read the department's units and their current statuses.</summary>
		public const string UnitsRead = "units.read";

		public static readonly IReadOnlyList<string> All = new[]
		{
			CallsRead, CallsCreate, CallsUpdate, CallsClose, ReferenceRead, UnitsRead
		};

		/// <summary>Claim carried by a key principal, one per granted scope.</summary>
		public const string ScopeClaimType = "resgrid:api_key_scope";

		/// <summary>Claim carried by a key principal: the key's id.</summary>
		public const string KeyIdClaimType = "resgrid:api_key_id";

		/// <summary>Authorization policy name that requires <paramref name="scope"/> of a key principal (other principals pass).</summary>
		public static string PolicyName(string scope) => "DepartmentApiKeyScope:" + scope;

		public static bool IsKnown(string scope) => scope != null && All.Contains(scope, StringComparer.Ordinal);

		/// <summary>Known scopes in <paramref name="value"/> (space, comma or newline separated), de-duplicated, in catalog order.</summary>
		public static List<string> Parse(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return new List<string>();

			var parts = value.Split(new[] { ' ', ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(x => x.Trim().ToLowerInvariant())
				.ToHashSet(StringComparer.Ordinal);

			return All.Where(parts.Contains).ToList();
		}

		public static string Serialize(IEnumerable<string> scopes) =>
			string.Join(" ", Parse(string.Join(" ", scopes ?? Enumerable.Empty<string>())));
	}
}

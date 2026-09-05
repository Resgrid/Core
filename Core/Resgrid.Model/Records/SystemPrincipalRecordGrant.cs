using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// One explicitly configured Record grant for a system principal (Identifier Allocation Registry
	/// section 4.4). System principals — the SMTP relay key and the client_credentials service accounts —
	/// are not people: they have no <c>Permission</c> rows, no roles and no group membership, so the
	/// grant-everything fall-through that departments rely on must never reach them. A grant names a
	/// department, a purpose that is written to the access audit on every read, and either department-wide
	/// scope or the exact group ids the purpose covers, which is how "subject to the same group scope as a
	/// user principal" is expressed for a principal that has no groups of its own.
	/// <para>
	/// A grant conveys <c>Record_View</c> and nothing else. There is no configuration that produces a
	/// mutating or restricted Record policy for a system principal; that is enforced at the two claim
	/// issuance sites and asserted by <c>SystemApiKeyRecordPolicyTests</c>.
	/// </para>
	/// </summary>
	public sealed class SystemPrincipalRecordGrant
	{
		public const string DepartmentWideToken = "DepartmentWide";
		public const string GroupsToken = "Groups";

		public SystemPrincipalRecordGrant(int departmentId, string purpose, bool departmentWide, IEnumerable<int> groupIds)
		{
			DepartmentId = departmentId;
			Purpose = purpose;
			DepartmentWide = departmentWide;
			GroupIds = (groupIds ?? Enumerable.Empty<int>()).Distinct().OrderBy(i => i).ToList();
		}

		public int DepartmentId { get; }

		/// <summary>Why this principal may read Records; written to <c>RmsRecordAccessAudit.Purpose</c>.</summary>
		public string Purpose { get; }

		/// <summary>When true the principal sees the whole department; when false it sees <see cref="GroupIds"/> only.</summary>
		public bool DepartmentWide { get; }

		/// <summary>The groups the purpose covers. Empty under a non-department-wide grant means the principal sees nothing.</summary>
		public IReadOnlyList<int> GroupIds { get; }

		/// <summary>The visible-group filter to apply, or null for "the whole department" — the same shape
		/// <c>IRecordsAuthorizationService.GetVisibleGroupIdsAsync</c> returns for a user.</summary>
		public List<int> VisibleGroupIds() => DepartmentWide ? null : GroupIds.ToList();

		private static string _parsedFrom;
		private static IReadOnlyList<SystemPrincipalRecordGrant> _parsed = Array.Empty<SystemPrincipalRecordGrant>();
		private static readonly object _parseLock = new object();

		/// <summary>Every configured grant. Parsed once per distinct configuration string.</summary>
		public static IReadOnlyList<SystemPrincipalRecordGrant> All()
		{
			var raw = Config.RecordsSystemAccessConfig.Grants ?? string.Empty;

			lock (_parseLock)
			{
				if (!string.Equals(raw, _parsedFrom, StringComparison.Ordinal))
				{
					_parsed = Parse(raw);
					_parsedFrom = raw;
				}

				return _parsed;
			}
		}

		/// <summary>Whether any system principal has been granted Record access at all.</summary>
		public static bool AnyConfigured() => All().Count > 0;

		/// <summary>The grant covering a department, or null when the principal has none there.</summary>
		public static SystemPrincipalRecordGrant For(int departmentId)
		{
			if (departmentId <= 0)
				return null;

			return All().FirstOrDefault(g => g.DepartmentId == departmentId);
		}

		/// <summary>
		/// Parses the configured grant string. A malformed entry is skipped rather than widened: a grant that
		/// cannot be read is not a grant, so the principal is denied.
		/// </summary>
		public static IReadOnlyList<SystemPrincipalRecordGrant> Parse(string raw)
		{
			var grants = new List<SystemPrincipalRecordGrant>();

			if (string.IsNullOrWhiteSpace(raw))
				return grants;

			foreach (var entry in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var parts = entry.Split('|');
				if (parts.Length != 3)
					continue;

				if (!int.TryParse(parts[0].Trim(), out var departmentId) || departmentId <= 0)
					continue;

				var purpose = parts[1].Trim();
				if (string.IsNullOrWhiteSpace(purpose))
					continue;

				var scope = parts[2].Trim();

				if (string.Equals(scope, DepartmentWideToken, StringComparison.OrdinalIgnoreCase))
				{
					grants.Add(new SystemPrincipalRecordGrant(departmentId, purpose, true, null));
					continue;
				}

				if (!scope.StartsWith(GroupsToken + ":", StringComparison.OrdinalIgnoreCase))
					continue;

				var groupIds = scope.Substring(GroupsToken.Length + 1)
					.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
					.Select(v => int.TryParse(v.Trim(), out var id) ? id : 0)
					.Where(id => id > 0)
					.ToList();

				grants.Add(new SystemPrincipalRecordGrant(departmentId, purpose, false, groupIds));
			}

			// A department named twice is ambiguous; the first entry wins so the configuration is deterministic.
			return grants.GroupBy(g => g.DepartmentId).Select(g => g.First()).ToList();
		}
	}
}

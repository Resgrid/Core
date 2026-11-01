using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Resgrid.Model.Dashboard
{
	/// <summary>
	/// Facts about the signed-in viewer that only the web tier can establish (claims-based policies) plus the department
	/// facts resolved once per request by IDashboardEntitlementService (plan §4.2 "entitlement snapshot").
	/// </summary>
	public sealed class DashboardViewer
	{
		/// <summary>The policy that makes a member a Dispatcher for the built-in defaults (plan §8); the web tier always evaluates it.</summary>
		public const string DispatcherPolicy = "Call_Create";

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		public bool IsDepartmentAdmin { get; set; }

		/// <summary>Department admin or the admin of at least one group.</summary>
		public bool IsGroupAdmin { get; set; }

		/// <summary>ResgridResources policies the principal satisfies (only those the catalog references are evaluated).</summary>
		public ISet<string> GrantedPolicies { get; set; } = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>Feature flags evaluated on for the department (only those the catalog references).</summary>
		public ISet<string> EnabledFlags { get; set; } = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>Results of the access checks the catalog references.</summary>
		public IDictionary<DashboardAccessChecks, bool> Access { get; set; } = new Dictionary<DashboardAccessChecks, bool>();

		/// <summary>Module PermissionTypes the viewer holds (only those the catalog references).</summary>
		public ISet<PermissionTypes> Permissions { get; set; } = new HashSet<PermissionTypes>();

		/// <summary>Add-ons the department holds (for locked gallery cards only).</summary>
		public ISet<PlanAddonTypes> HeldAddons { get; set; } = new HashSet<PlanAddonTypes>();

		/// <summary>Add-ons offered to the department (flag on) but not held — candidates for admin locked cards.</summary>
		public ISet<PlanAddonTypes> OfferedAddons { get; set; } = new HashSet<PlanAddonTypes>();

		/// <summary>The department enforces Advanced Data Protection (plan §9A.1).</summary>
		public bool AdpEnforced { get; set; }

		/// <summary>ADP view permissions the viewer holds, resolved through AdpPermissionDefaults for missing rows.</summary>
		public ISet<PermissionTypes> AdpViewPermissions { get; set; } = new HashSet<PermissionTypes>();

		public IReadOnlyList<int> PersonnelRoleIds { get; set; } = Array.Empty<int>();

		public IReadOnlyList<int> GroupIds { get; set; } = Array.Empty<int>();

		/// <summary>
		/// Picks the built-in default (plan §8). Derived from the identity facts the web tier sets, so a board resolved before
		/// the department facts are completed (WidgetData, SaveLayout) gets the same default the bootstrap showed.
		/// </summary>
		public DashboardAudiences Audience => IsDepartmentAdmin ? DashboardAudiences.Administrator
			: HasPolicy(DispatcherPolicy) || IsGroupAdmin ? DashboardAudiences.Dispatcher
			: DashboardAudiences.Member;

		public string TimeZoneId { get; set; }

		public bool HasPolicy(string policy) => policy != null && GrantedPolicies.Contains(policy);

		public bool HasFlag(string flag) => flag != null && EnabledFlags.Contains(flag);

		public bool Passes(DashboardAccessChecks check) => Access.TryGetValue(check, out var ok) && ok;

		public bool CanRevealAdp(PermissionTypes viewPermission) => AdpEnforced && AdpViewPermissions.Contains(viewPermission);
	}

	/// <summary>Everything a widget data provider gets for one call (plan §4.2).</summary>
	public sealed class DashboardWidgetContext
	{
		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		public DashboardViewer Viewer { get; set; }

		public DashboardWidgetDefinition Definition { get; set; }

		/// <summary>The instance's settings after validation (types and bounds already enforced).</summary>
		public IReadOnlyDictionary<string, object> Settings { get; set; } = new Dictionary<string, object>();

		public string Size { get; set; }

		public DateTime NowUtc { get; set; } = DateTime.UtcNow;

		/// <summary>
		/// The caller's Protected Data Grant from the X-Resgrid-Protected-Grant header, or null. Set only by the web controller;
		/// pass it to IProtectedReadService and nowhere else, never log or cache it (plan §9A.4).
		/// </summary>
		public string ProtectedGrantToken { get; set; }

		public int GetInt(string name, int fallback)
		{
			if (!Settings.TryGetValue(name, out var value) || value == null)
				return fallback;

			return value switch
			{
				int i => i,
				long l => (int)l,
				double d => (int)d,
				string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
				_ => fallback
			};
		}

		public bool GetBool(string name, bool fallback)
		{
			if (!Settings.TryGetValue(name, out var value) || value == null)
				return fallback;

			return value switch
			{
				bool b => b,
				string s when bool.TryParse(s, out var parsed) => parsed,
				_ => fallback
			};
		}

		public string GetString(string name, string fallback = null)
		{
			return Settings.TryGetValue(name, out var value) && value is string s && !string.IsNullOrWhiteSpace(s) ? s : fallback;
		}

		public IReadOnlyList<string> GetStrings(string name)
		{
			if (!Settings.TryGetValue(name, out var value) || value == null)
				return Array.Empty<string>();

			return value switch
			{
				IEnumerable<string> list => list.ToList(),
				string s => new[] { s },
				_ => Array.Empty<string>()
			};
		}
	}

	/// <summary>
	/// A protected (ADP) value as a widget DTO carries it (plan §9A.4): the text only when revealed, otherwise a flag the
	/// client renders as a shield. CanReveal is false when the viewer's ADP permission would refuse a reveal.
	/// </summary>
	public sealed class DashboardProtectedText
	{
		public string Value { get; set; }

		public bool IsProtected { get; set; }

		public bool CanReveal { get; set; }

		public static DashboardProtectedText Plain(string value) => new DashboardProtectedText { Value = value };

		public static DashboardProtectedText Hidden(bool canReveal) => new DashboardProtectedText { IsProtected = true, CanReveal = canReveal };
	}

	/// <summary>The data endpoint's envelope (plan §4.4). Widget-level failures come back as Error with HTTP 200.</summary>
	public sealed class DashboardWidgetDataResult
	{
		public object Data { get; set; }

		public DateTime GeneratedUtc { get; set; }

		public DashboardWidgetError Error { get; set; }
	}

	public sealed class DashboardWidgetError
	{
		/// <summary>"failed", "forbidden", "protected" (an ADP sentinel — show "Verify to view"), "notFound".</summary>
		public string Code { get; set; }

		/// <summary>HomeDashboard resx key for the message.</summary>
		public string MessageKey { get; set; }
	}

	public static class DashboardWidgetErrorCodes
	{
		public const string Failed = "failed";
		public const string Forbidden = "forbidden";
		public const string Protected = "protected";
		public const string NotFound = "notFound";
	}

	/// <summary>
	/// Thrown by a provider for a condition the widget should show as a state, not a crash (e.g. the ADP "Unlock protected
	/// data" sentinel from a mixed-content module). Stays out of Sentry (IUserFacingException).
	/// </summary>
	public sealed class DashboardWidgetStateException : Exception, Resgrid.Framework.IUserFacingException
	{
		public DashboardWidgetStateException(string code, string messageKey) : base(code)
		{
			Code = code;
			MessageKey = messageKey;
		}

		public string Code { get; }

		public string MessageKey { get; }
	}
}

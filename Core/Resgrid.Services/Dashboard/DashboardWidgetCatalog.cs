using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Dashboard;

namespace Resgrid.Services.Dashboard
{
	/// <summary>
	/// The authoritative list of home dashboard widgets (home-dashboard-redesign-plan.md §4.2, §7). One partial file per
	/// category (DashboardWidgetCatalog.&lt;Category&gt;.cs) so additions never collide. Pure data: no dependencies.
	/// A key is permanent once shipped; a removed widget's key moves to <see cref="RetiredKeys"/>.
	/// </summary>
	public static partial class DashboardWidgetCatalog
	{
		private static readonly Lazy<IReadOnlyList<DashboardWidgetDefinition>> _all = new Lazy<IReadOnlyList<DashboardWidgetDefinition>>(Build);
		private static readonly Lazy<IReadOnlyDictionary<string, DashboardWidgetDefinition>> _byKey =
			new Lazy<IReadOnlyDictionary<string, DashboardWidgetDefinition>>(() => _all.Value.ToDictionary(d => d.Key, StringComparer.Ordinal));

		/// <summary>Keys of widgets that no longer exist; stored instances are preserved but never rendered (plan §11).</summary>
		public static readonly IReadOnlyCollection<string> RetiredKeys = new HashSet<string>(StringComparer.Ordinal);

		public static IReadOnlyList<DashboardWidgetDefinition> All => _all.Value;

		public static DashboardWidgetDefinition Find(string key) =>
			key != null && _byKey.Value.TryGetValue(key, out var definition) ? definition : null;

		/// <summary>Every policy any widget references (the web tier evaluates exactly these).</summary>
		public static IReadOnlyCollection<string> ReferencedPolicies =>
			All.SelectMany(d => d.Gates.Policies).Distinct(StringComparer.Ordinal).ToList();

		public static IReadOnlyCollection<string> ReferencedFlags =>
			All.SelectMany(d => d.Gates.Flags.Concat(d.Gates.AnyFlags)).Distinct(StringComparer.Ordinal).ToList();

		public static IReadOnlyCollection<DashboardAccessChecks> ReferencedAccess =>
			All.SelectMany(d => d.Gates.Access).Distinct().ToList();

		public static IReadOnlyCollection<PermissionTypes> ReferencedPermissions =>
			All.SelectMany(d => d.Gates.Permissions).Distinct().ToList();

		private static IReadOnlyList<DashboardWidgetDefinition> Build()
		{
			var list = new List<DashboardWidgetDefinition>();
			list.AddRange(OperationsWidgets());
			list.AddRange(PersonnelWidgets());
			list.AddRange(AwarenessWidgets());
			list.AddRange(CommunicationsWidgets());
			list.AddRange(PlanningWidgets());
			list.AddRange(ReadinessWidgets());
			list.AddRange(RecordsWidgets());
			list.AddRange(BusinessWidgets());
			list.AddRange(IncidentBackOfficeWidgets());
			list.AddRange(IntelligenceWidgets());
			list.AddRange(AdministrationWidgets());
			return list;
		}

		private static partial IEnumerable<DashboardWidgetDefinition> OperationsWidgets();
		private static partial IEnumerable<DashboardWidgetDefinition> PersonnelWidgets();
		private static partial IEnumerable<DashboardWidgetDefinition> AwarenessWidgets();
		private static partial IEnumerable<DashboardWidgetDefinition> CommunicationsWidgets();
		private static partial IEnumerable<DashboardWidgetDefinition> PlanningWidgets();
		private static partial IEnumerable<DashboardWidgetDefinition> ReadinessWidgets();
		private static partial IEnumerable<DashboardWidgetDefinition> RecordsWidgets();
		private static partial IEnumerable<DashboardWidgetDefinition> BusinessWidgets();
		private static partial IEnumerable<DashboardWidgetDefinition> IncidentBackOfficeWidgets();
		private static partial IEnumerable<DashboardWidgetDefinition> IntelligenceWidgets();
		private static partial IEnumerable<DashboardWidgetDefinition> AdministrationWidgets();

		/// <summary>Shorthand for a setting with fixed options.</summary>
		internal static DashboardWidgetSetting Select(string name, string defaultValue, params string[] options) =>
			new DashboardWidgetSetting { Name = name, Type = DashboardSettingTypes.Select, Default = defaultValue, Options = options };

		internal static DashboardWidgetSetting SelectFrom(string name, string source) =>
			new DashboardWidgetSetting { Name = name, Type = DashboardSettingTypes.Select, OptionsSource = source };

		internal static DashboardWidgetSetting MultiFrom(string name, string source) =>
			new DashboardWidgetSetting { Name = name, Type = DashboardSettingTypes.MultiSelect, OptionsSource = source, Default = Array.Empty<string>() };

		internal static DashboardWidgetSetting Number(string name, int defaultValue, int min, int max) =>
			new DashboardWidgetSetting { Name = name, Type = DashboardSettingTypes.Number, Default = defaultValue, Min = min, Max = max };

		internal static DashboardWidgetSetting Toggle(string name, bool defaultValue = false) =>
			new DashboardWidgetSetting { Name = name, Type = DashboardSettingTypes.Toggle, Default = defaultValue };

		internal static DashboardWidgetSetting Text(string name, int maxLength) =>
			new DashboardWidgetSetting { Name = name, Type = DashboardSettingTypes.Text, Default = string.Empty, Max = maxLength };

		internal static string[] Sizes(params string[] sizes) => sizes;

		internal static string[] Topics(params string[] topics) => topics;
	}
}

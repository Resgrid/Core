using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Dashboard
{
	/// <summary>Widget gallery categories (plan §6.2 accents, §7 sections).</summary>
	public enum DashboardWidgetCategories
	{
		Operations = 0,
		Personnel = 1,
		Awareness = 2,
		Communications = 3,
		Planning = 4,
		Readiness = 5,
		Records = 6,
		Business = 7,
		IncidentBackOffice = 8,
		Intelligence = 9,
		Administration = 10
	}

	/// <summary>Who a built-in default layout is for (plan §8).</summary>
	public enum DashboardAudiences
	{
		Member = 0,
		Dispatcher = 1,
		Administrator = 2
	}

	/// <summary>Size preset tokens (plan §5.1). Stored in layouts verbatim, so never rename one.</summary>
	public static class DashboardWidgetSizes
	{
		public const string Xs = "xs";
		public const string S = "s";
		public const string M = "m";
		public const string L = "l";
		public const string Xl = "xl";
		public const string Full = "full";

		public static readonly IReadOnlyList<string> All = new[] { Xs, S, M, L, Xl, Full };

		public static bool IsKnown(string size) => size != null && All.Contains(size);
	}

	/// <summary>Realtime topics a widget can subscribe to (plan §10). The client maps eventing hub methods to these.</summary>
	public static class DashboardTopics
	{
		public const string Calls = "calls";
		public const string UnitStatus = "unitStatus";
		public const string PersonnelStatus = "personnelStatus";
		public const string PersonnelStaffing = "personnelStaffing";
		public const string UnitRoles = "unitRoles";
		public const string UnitAlerts = "unitAlerts";
		public const string Checklists = "checklists";
		public const string Requirements = "requirements";
		public const string IncidentCommand = "incidentCommand";
		public const string Chat = "chat";

		public static readonly IReadOnlyList<string> All = new[]
		{
			Calls, UnitStatus, PersonnelStatus, PersonnelStaffing, UnitRoles, UnitAlerts, Checklists, Requirements, IncidentCommand, Chat
		};
	}

	public enum DashboardSettingTypes
	{
		Number = 0,
		Toggle = 1,
		Select = 2,
		MultiSelect = 3,
		Text = 4
	}

	/// <summary>
	/// Checks that are more than a policy or a flag: module switches and the modules' own access services (which combine
	/// flag, module switch and add-on). Evaluated once per request by IDashboardEntitlementService, only when referenced.
	/// </summary>
	public enum DashboardAccessChecks
	{
		MessagingEnabled = 0,
		MappingEnabled = 1,
		ShiftsEnabled = 2,
		LogsEnabled = 3,
		ReportsEnabled = 4,
		DocumentsEnabled = 5,
		CalendarEnabled = 6,
		NotesEnabled = 7,
		TrainingEnabled = 8,
		InventoryEnabled = 9,
		/// <summary>IReadinessAccessService.CanUseChecklistsAsync (flag + module; free).</summary>
		ChecklistsUsable = 10,
		/// <summary>IReadinessAccessService.CanUseMaintenanceAsync (flag + module + Readiness Pro).</summary>
		MaintenanceUsable = 11,
		/// <summary>Records flag on and cutover finished (the nav's recordsUsable).</summary>
		RecordsUsable = 12,
		/// <summary>Logs module on and Records not usable (the nav's legacy Logs link).</summary>
		LegacyLogsOnly = 13,
		/// <summary>Business Operations module switch on and Invoicing.CustomerInvoicing flag on (the nav rule; lapse = read-only).</summary>
		InvoicingVisible = 14,
		ContractorBillingUsable = 15,
		CostRecoveryUsable = 16,
		WorkforceUsable = 17,
		PayDataReportingUsable = 18,
		/// <summary>IIncidentBackOfficeAccessService.IsEnabledAsync (flag + module; lapse = read-only).</summary>
		IncidentBackOfficeEnabled = 19,
		IncidentBusinessEnabled = 20,
		IncidentCoordinationEnabled = 21,
		/// <summary>IEnhancedAiAccessService.CanUseAsync(Ai.Assistant).</summary>
		EnhancedAiAssistant = 22,
		/// <summary>AI dispatch host enabled, rolled out and Enhanced AI usable.</summary>
		AiDispatchAvailable = 23,
		/// <summary>IVoiceService.CanDepartmentUseVoiceAsync (Push-To-Talk add-on).</summary>
		VoiceAddon = 24,
		/// <summary>AdminAssistFeatureAvailability: Admin.Setup, or Admin.Assist + Ai.AdminAssist.</summary>
		AdminAssistAvailable = 25,
		/// <summary>A weather source is configured for the department.</summary>
		WeatherConfigured = 26,
		/// <summary>NerisConfig.Enabled and the department's NERIS workflow is on.</summary>
		NerisWorkflowEnabled = 27,
		/// <summary>Department has ADP enrollment offered (Security.DepartmentProtectedDataEnrollment) or already enrolled.</summary>
		DataProtectionOffered = 28,
		/// <summary>Module visibility switches of the newer modules (DepartmentModuleSettings 30-33).</summary>
		ContactsEnabled = 29,
		CertificationsEnabled = 30,
		DeploymentsEnabled = 31,
		ChatEnabled = 32
	}

	public enum DashboardRoles
	{
		None = 0,
		/// <summary>Department admin or the admin of any group.</summary>
		GroupAdmin = 1,
		DepartmentAdmin = 2
	}

	/// <summary>A per-widget setting the user can change (plan §5.1: settings are an allow-list, validated server-side).</summary>
	public sealed class DashboardWidgetSetting
	{
		public string Name { get; init; }

		public DashboardSettingTypes Type { get; init; }

		/// <summary>Number bounds; for Text the maximum length.</summary>
		public int? Min { get; init; }

		public int? Max { get; init; }

		/// <summary>Default value: int for Number, bool for Toggle, string for Select/Text, string[] for MultiSelect.</summary>
		public object Default { get; init; }

		/// <summary>Fixed option values for Select/MultiSelect; labels are resx keys W.{widgetKey}.Option.{value}.</summary>
		public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();

		/// <summary>
		/// Department-specific options instead of fixed ones (see DashboardOptionSources); the server resolves them into the
		/// bootstrap and re-checks a posted value belongs to the department.
		/// </summary>
		public string OptionsSource { get; init; }
	}

	/// <summary>Named, department-specific option lists a setting can draw from.</summary>
	public static class DashboardOptionSources
	{
		public const string Groups = "groups";
		public const string Stations = "stations";
		public const string UnitTypes = "unitTypes";
		public const string CallPriorities = "callPriorities";
		public const string MapVisualizations = "mapVisualizations";
		public const string IncidentActivations = "incidentActivations";
		public const string CalendarTypes = "calendarTypes";
		public const string DocumentCategories = "documentCategories";
		public const string NoteCategories = "noteCategories";
	}

	/// <summary>Everything that must pass for a viewer to see a widget (plan §9). Every list is "all of", except AnyFlags.</summary>
	public sealed class DashboardWidgetGates
	{
		/// <summary>ResgridResources policy names, evaluated by the web tier against the signed-in principal.</summary>
		public IReadOnlyList<string> Policies { get; init; } = Array.Empty<string>();

		/// <summary>FeatureFlagKeys that must all be on for the department.</summary>
		public IReadOnlyList<string> Flags { get; init; } = Array.Empty<string>();

		/// <summary>When non-empty, at least one of these FeatureFlagKeys must be on.</summary>
		public IReadOnlyList<string> AnyFlags { get; init; } = Array.Empty<string>();

		public IReadOnlyList<DashboardAccessChecks> Access { get; init; } = Array.Empty<DashboardAccessChecks>();

		public DashboardRoles Role { get; init; } = DashboardRoles.None;

		/// <summary>
		/// The paid add-on behind this widget. Used only to show department admins a locked gallery card when every other
		/// gate passes except the add-on's (plan D8); entitlement itself is decided by Access.
		/// </summary>
		public PlanAddonTypes? Addon { get; init; }

		/// <summary>PermissionTypes the viewer must hold under the module's own permission screen (e.g. ManageRecordLegalHold).</summary>
		public IReadOnlyList<PermissionTypes> Permissions { get; init; } = Array.Empty<PermissionTypes>();
	}

	/// <summary>An ADP-catalogued field a widget can show (plan §9A.2).</summary>
	public sealed class DashboardProtectedField
	{
		/// <summary>ProtectedFieldCatalog field id, e.g. "messages.subject".</summary>
		public string FieldId { get; init; }

		/// <summary>The ADP view permission that covers it (resolved through AdpPermissionDefaults when no row exists).</summary>
		public PermissionTypes ViewPermission { get; init; }
	}

	/// <summary>
	/// Static metadata for one widget (plan §4.2). Definitions live in the DashboardWidgetCatalog partial files, one per
	/// category; a key is permanent once shipped.
	/// </summary>
	public sealed class DashboardWidgetDefinition
	{
		/// <summary>Stable key, "module.name" in lower camel (e.g. "calls.active").</summary>
		public string Key { get; init; }

		/// <summary>Bumped when the widget's settings shape changes incompatibly.</summary>
		public int Version { get; init; } = 1;

		public DashboardWidgetCategories Category { get; init; }

		/// <summary>Font Awesome 4.7 class without the "fa " prefix, e.g. "fa-bullhorn" (match the module's nav icon).</summary>
		public string Icon { get; init; }

		/// <summary>Where "Open module" goes, e.g. "/User/Dispatch/Dashboard".</summary>
		public string ModuleUrl { get; init; }

		public IReadOnlyList<string> AllowedSizes { get; init; } = new[] { DashboardWidgetSizes.M };

		public string DefaultSize { get; init; } = DashboardWidgetSizes.M;

		public IReadOnlyList<DashboardWidgetSetting> Settings { get; init; } = Array.Empty<DashboardWidgetSetting>();

		public IReadOnlyList<string> RealtimeTopics { get; init; } = Array.Empty<string>();

		/// <summary>Poll interval when no topic fires (plan §10): 60 operations, 300 planning/readiness, 900 records/business.</summary>
		public int DefaultRefreshSeconds { get; init; } = 300;

		public int MinRefreshSeconds { get; init; } = 60;

		public bool SupportsMultipleInstances { get; init; }

		public DashboardWidgetGates Gates { get; init; } = new DashboardWidgetGates();

		public IReadOnlyList<DashboardProtectedField> ProtectedFields { get; init; } = Array.Empty<DashboardProtectedField>();

		/// <summary>Department admins only, whatever the policies say.</summary>
		public bool AdminOnly => Gates.Role == DashboardRoles.DepartmentAdmin;

		public bool IsLive => RealtimeTopics.Count > 0;

		public string TitleResourceKey => $"W.{Key}.Title";

		public string DescriptionResourceKey => $"W.{Key}.Description";
	}

	/// <summary>Marks an IDashboardOptionSource with the DashboardOptionSources name it serves.</summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	public sealed class DashboardOptionSourceAttribute : Attribute
	{
		public DashboardOptionSourceAttribute(string name)
		{
			Name = name;
		}

		public string Name { get; }
	}

	/// <summary>Marks an IDashboardWidgetDataProvider with the widget key it serves; the container registers it keyed by it.</summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	public sealed class DashboardWidgetAttribute : Attribute
	{
		public DashboardWidgetAttribute(string key)
		{
			Key = key;
		}

		public string Key { get; }
	}
}

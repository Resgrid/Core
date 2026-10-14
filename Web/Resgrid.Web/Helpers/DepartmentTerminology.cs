using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// Request-bound view of the signed-in department's terminology (setting 118): each term resolves to the department's
	/// own word, else the shipped name in the user's language. Views reach it as <c>departmentTerms</c> (User area
	/// _ViewImports); controllers take it by constructor.
	/// </summary>
	public interface IDepartmentTerminology
	{
		/// <summary>The department's saved terms; an empty config when nobody is signed in.</summary>
		Task<DepartmentTerminologyConfig> GetAsync();

		/// <summary>The name to show for a term: the department's own, else the localized shipped name.</summary>
		Task<string> NameAsync(string key);

		/// <summary>The department's own name for a term, else <paramref name="defaultText"/> (a page heading worded its own way).</summary>
		Task<string> NameAsync(string key, string defaultText);

		/// <summary>A built-in record (or legacy log) type's name by its number.</summary>
		Task<string> RecordTypeAsync(int recordType);

		/// <summary>A record's type name from its definition key; a department definition shows its own name, else its key.</summary>
		Task<string> DefinitionAsync(string definitionKey, string departmentDefinitionName = null);

		/// <summary>The shipped name of a term in the current UI language.</summary>
		string DefaultName(string key);
	}

	/// <summary>The setting is read once per request (registered per lifetime scope).</summary>
	public sealed class DepartmentTerminology : IDepartmentTerminology
	{
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IHttpContextAccessor _httpContextAccessor;
		private readonly IStringLocalizer<Resgrid.Localization.Common> _commonLocalizer;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.UserDefinedFields.UserDefinedFields> _recordTypeLocalizer;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.WorkOrders.WorkOrders> _workOrderLocalizer;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Checklists.Checklists> _checklistLocalizer;
		private Task<DepartmentTerminologyConfig> _config;

		public DepartmentTerminology(IDepartmentSettingsService departmentSettingsService, IHttpContextAccessor httpContextAccessor,
			IStringLocalizer<Resgrid.Localization.Common> commonLocalizer,
			IStringLocalizer<Resgrid.Localization.Areas.User.UserDefinedFields.UserDefinedFields> recordTypeLocalizer,
			IStringLocalizer<Resgrid.Localization.Areas.User.WorkOrders.WorkOrders> workOrderLocalizer,
			IStringLocalizer<Resgrid.Localization.Areas.User.Checklists.Checklists> checklistLocalizer)
		{
			_departmentSettingsService = departmentSettingsService;
			_httpContextAccessor = httpContextAccessor;
			_commonLocalizer = commonLocalizer;
			_recordTypeLocalizer = recordTypeLocalizer;
			_workOrderLocalizer = workOrderLocalizer;
			_checklistLocalizer = checklistLocalizer;
		}

		/// <summary>The department's saved terms; an empty config when nobody is signed in.</summary>
		public Task<DepartmentTerminologyConfig> GetAsync()
		{
			return _config ??= LoadAsync();
		}

		/// <summary>The name to show for a term: the department's own, else the localized shipped name.</summary>
		public async Task<string> NameAsync(string key)
		{
			return (await GetAsync()).TextFor(key, DefaultName(key));
		}

		/// <summary>
		/// The department's own name for a term, else <paramref name="defaultText"/>: for a page whose shipped heading is
		/// worded differently from its navigation entry ("Reporting" for Reports), so an unrenamed page keeps its heading.
		/// </summary>
		public async Task<string> NameAsync(string key, string defaultText)
		{
			return (await GetAsync()).TextFor(key, defaultText);
		}

		/// <summary>A built-in record (or legacy log) type's name by its number.</summary>
		public async Task<string> RecordTypeAsync(int recordType)
		{
			return (await GetAsync()).RecordTypeName(recordType, DefaultName(DepartmentTerms.RecordTypeKey((RmsOperationalRecordType)recordType)));
		}

		/// <summary>
		/// A record's type name from its definition key; a department definition shows <paramref name="departmentDefinitionName"/>,
		/// else its key.
		/// </summary>
		public async Task<string> DefinitionAsync(string definitionKey, string departmentDefinitionName = null)
		{
			var termKey = DepartmentTerms.RecordTypeKey(definitionKey);
			if (DepartmentTerms.IsKnown(termKey))
				return (await GetAsync()).TextFor(termKey, DefaultName(termKey));

			return string.IsNullOrWhiteSpace(departmentDefinitionName) ? definitionKey : departmentDefinitionName;
		}

		/// <summary>The shipped name of a term in the current UI language (the English catalog name when no translation exists).</summary>
		public string DefaultName(string key)
		{
			var term = DepartmentTerms.Find(key);
			if (term == null)
				return key;

			if (!ResourceKeys.TryGetValue(key, out var resource))
				return term.DefaultName;

			LocalizedString text;
			switch (resource.Source)
			{
				case ResourceSource.RecordTypes:
					text = _recordTypeLocalizer[resource.Name];
					break;
				case ResourceSource.WorkOrders:
					text = _workOrderLocalizer[resource.Name];
					break;
				case ResourceSource.Checklists:
					text = _checklistLocalizer[resource.Name];
					break;
				default:
					text = _commonLocalizer[resource.Name];
					break;
			}

			return text == null || text.ResourceNotFound || string.IsNullOrWhiteSpace(text.Value) ? term.DefaultName : text.Value;
		}

		private async Task<DepartmentTerminologyConfig> LoadAsync()
		{
			var departmentId = CurrentDepartmentId();
			if (departmentId <= 0)
				return new DepartmentTerminologyConfig();

			try
			{
				return await _departmentSettingsService.GetDepartmentTerminologyAsync(departmentId) ?? new DepartmentTerminologyConfig();
			}
			catch (Exception ex)
			{
				// Labels only: a failed read must never take the navigation or a page down.
				Framework.Logging.LogException(ex, "Department terminology could not be read; showing the shipped names.");
				return new DepartmentTerminologyConfig();
			}
		}

		private int CurrentDepartmentId()
		{
			var claim = _httpContextAccessor?.HttpContext?.User?.FindFirst(ClaimTypes.PrimaryGroupSid);
			return claim != null && int.TryParse(claim.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;
		}

		private enum ResourceSource
		{
			Common,
			RecordTypes,
			WorkOrders,
			Checklists
		}

		private readonly struct ResourceKey
		{
			public ResourceKey(ResourceSource source, string name)
			{
				Source = source;
				Name = name;
			}

			public ResourceSource Source { get; }
			public string Name { get; }
		}

		/// <summary>Where each term's shipped name is translated: the navigation keys for modules, the record type keys for types.</summary>
		private static readonly Dictionary<string, ResourceKey> ResourceKeys = new Dictionary<string, ResourceKey>(StringComparer.Ordinal)
		{
			{ DepartmentTerms.Calls, new ResourceKey(ResourceSource.Common, "CallsModule") },
			{ DepartmentTerms.Personnel, new ResourceKey(ResourceSource.Common, "PersonnelModule") },
			{ DepartmentTerms.Units, new ResourceKey(ResourceSource.Common, "UnitsModule") },
			{ DepartmentTerms.Contacts, new ResourceKey(ResourceSource.Common, "ContactsModule") },
			{ DepartmentTerms.Mapping, new ResourceKey(ResourceSource.Common, "MappingModule") },
			{ DepartmentTerms.Shifts, new ResourceKey(ResourceSource.Common, "ShiftsModule") },
			{ DepartmentTerms.Records, new ResourceKey(ResourceSource.Common, "RecordsModule") },
			{ DepartmentTerms.Logs, new ResourceKey(ResourceSource.Common, "LogsModule") },
			{ DepartmentTerms.Reports, new ResourceKey(ResourceSource.Common, "ReportsModule") },
			{ DepartmentTerms.Documents, new ResourceKey(ResourceSource.Common, "DocumentsModule") },
			{ DepartmentTerms.Calendar, new ResourceKey(ResourceSource.Common, "CalendarModule") },
			{ DepartmentTerms.Notes, new ResourceKey(ResourceSource.Common, "NotesModule") },
			{ DepartmentTerms.Trainings, new ResourceKey(ResourceSource.Common, "TrainingsModule") },
			{ DepartmentTerms.Inventory, new ResourceKey(ResourceSource.Common, "InventoryModule") },
			{ DepartmentTerms.Readiness, new ResourceKey(ResourceSource.Common, "NavigationReadiness") },
			{ DepartmentTerms.WorkOrders, new ResourceKey(ResourceSource.WorkOrders, "WorkOrders") },
			{ DepartmentTerms.Checklists, new ResourceKey(ResourceSource.Checklists, "Checklists") },
			{ DepartmentTerms.WorkforceBusiness, new ResourceKey(ResourceSource.Common, "NavigationWorkforceBusiness") },
			{ DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Run), new ResourceKey(ResourceSource.RecordTypes, "RecordTypeRun") },
			{ DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Training), new ResourceKey(ResourceSource.RecordTypes, "RecordTypeTraining") },
			{ DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Work), new ResourceKey(ResourceSource.RecordTypes, "RecordTypeWork") },
			{ DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Meeting), new ResourceKey(ResourceSource.RecordTypes, "RecordTypeMeeting") },
			{ DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Coroner), new ResourceKey(ResourceSource.RecordTypes, "RecordTypeCoroner") },
			{ DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Callback), new ResourceKey(ResourceSource.RecordTypes, "RecordTypeCallback") },
			{ DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.UnitActivity), new ResourceKey(ResourceSource.RecordTypes, "RecordTypeUnitActivity") },
			{ DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.NerisIncidentReport), new ResourceKey(ResourceSource.RecordTypes, "RecordTypeNerisIncident") }
		};

		/// <summary>Term keys whose shipped name comes from a resource (test hook: every catalog term must have one).</summary>
		public static IEnumerable<string> LocalizedTermKeys => ResourceKeys.Keys;
	}
}

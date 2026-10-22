using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
	/// _ViewImports); controllers take it by constructor. Localized strings carry the same words through
	/// <see cref="DepartmentTermStringLocalizerFactory"/>, using the substitution prepared here.
	/// </summary>
	public interface IDepartmentTerminology
	{
		/// <summary>The department's saved terms; an empty config when nobody is signed in.</summary>
		Task<DepartmentTerminologyConfig> GetAsync();

		/// <summary>The name to show for a term: the department's own, else the localized shipped name.</summary>
		Task<string> NameAsync(string key);

		/// <summary>The department's own name for a term, else <paramref name="defaultText"/> (a page heading worded its own way).</summary>
		Task<string> NameAsync(string key, string defaultText);

		/// <summary>The word for one item of a term ("Team" for Units renamed Teams), else the localized shipped item word.</summary>
		Task<string> SingularAsync(string key);

		/// <summary>A built-in record (or legacy log) type's name by its number.</summary>
		Task<string> RecordTypeAsync(int recordType);

		/// <summary>A record's type name from its definition key; a department definition shows its own name, else its key.</summary>
		Task<string> DefinitionAsync(string definitionKey, string departmentDefinitionName = null);

		/// <summary>The shipped name of a term in the current UI language.</summary>
		string DefaultName(string key);

		/// <summary>The shipped item word of a term in the current UI language; null for a term without one.</summary>
		string DefaultSingular(string key);

		/// <summary>Reads the terminology and readies this request's substitution (the MVC filter calls it before every action).</summary>
		Task<DepartmentTermSubstitution> PrepareAsync();

		/// <summary>Keeps Resgrid's own wording for the rest of this request (the Terminology page).</summary>
		void Suppress();

		/// <summary>
		/// Text built in code (not from a resource) with the department's words in place of renamed terms, as a resource of
		/// <paramref name="resource"/> would get them; null applies the terms renamed everywhere. Needs <see cref="PrepareAsync"/>.
		/// </summary>
		string Apply(string text, string resource = null);

		/// <summary>The rules for <c>resgrid.terms.apply</c> (text built in page scripts); null when nothing is renamed.</summary>
		Task<DepartmentTermClientRules> ClientRulesAsync();
	}

	/// <summary>
	/// What page scripts need to carry the department's words into the English text they build: per renamed word, the shipped
	/// English forms and the department's word, plus the protected phrases. Serialized by the layout into resgrid.terms.setup.
	/// </summary>
	public sealed class DepartmentTermClientRules
	{
		[System.Text.Json.Serialization.JsonPropertyName("rules")]
		public List<DepartmentTermClientRule> Rules { get; } = new List<DepartmentTermClientRule>();

		[System.Text.Json.Serialization.JsonPropertyName("phrases")]
		public List<string> Phrases { get; } = new List<string>();

		[System.Text.Json.Serialization.JsonPropertyName("patterns")]
		public List<string> Patterns { get; } = new List<string>();
	}

	public sealed class DepartmentTermClientRule
	{
		[System.Text.Json.Serialization.JsonPropertyName("key")]
		public string Key { get; set; }

		/// <summary>True when the word is replaced in every module; false only on its own module's pages.</summary>
		[System.Text.Json.Serialization.JsonPropertyName("everywhere")]
		public bool Everywhere { get; set; }

		[System.Text.Json.Serialization.JsonPropertyName("forms")]
		public List<string> Forms { get; set; }

		[System.Text.Json.Serialization.JsonPropertyName("text")]
		public string Text { get; set; }
	}

	/// <summary>The setting is read once per request (registered per lifetime scope).</summary>
	public sealed class DepartmentTerminology : IDepartmentTerminology
	{
		private const string ItemsKey = "Resgrid.DepartmentTermSubstitution";
		private const string EnglishItemsKey = "Resgrid.DepartmentTermSubstitution.English";
		private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

		/// <summary>Built substitutions by language and saved terms; departments with the same names share one.</summary>
		private static readonly ConcurrentDictionary<string, DepartmentTermSubstitution> Substitutions = new ConcurrentDictionary<string, DepartmentTermSubstitution>(StringComparer.Ordinal);
		private const int MaxCachedSubstitutions = 2000;

		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IHttpContextAccessor _httpContextAccessor;
		private readonly IStringLocalizer _commonLocalizer;
		private readonly IStringLocalizer _recordTypeLocalizer;
		private readonly IStringLocalizer _workOrderLocalizer;
		private readonly IStringLocalizer _checklistLocalizer;
		private Task<DepartmentTerminologyConfig> _config;
		private DepartmentTermSubstitution _substitution;
		private DepartmentTermSubstitution _englishSubstitution;
		private bool _suppressed;

		public DepartmentTerminology(IDepartmentSettingsService departmentSettingsService, IHttpContextAccessor httpContextAccessor,
			IShippedStringLocalizerFactory shippedLocalizers)
		{
			_departmentSettingsService = departmentSettingsService;
			_httpContextAccessor = httpContextAccessor;
			_commonLocalizer = shippedLocalizers.Create(typeof(Resgrid.Localization.Common));
			_recordTypeLocalizer = shippedLocalizers.Create(typeof(Resgrid.Localization.Areas.User.UserDefinedFields.UserDefinedFields));
			_workOrderLocalizer = shippedLocalizers.Create(typeof(Resgrid.Localization.Areas.User.WorkOrders.WorkOrders));
			_checklistLocalizer = shippedLocalizers.Create(typeof(Resgrid.Localization.Areas.User.Checklists.Checklists));
		}

		/// <summary>The substitution <see cref="PrepareAsync"/> stored for the request; null before it ran or outside a request.</summary>
		public static DepartmentTermSubstitution CurrentSubstitution(HttpContext context)
		{
			return context?.Items != null && context.Items.TryGetValue(ItemsKey, out var value) ? value as DepartmentTermSubstitution : null;
		}

		/// <summary>
		/// The request's substitution for English written in code (a CSV header, a tree label), whatever the member's language;
		/// null before <see cref="PrepareAsync"/> ran or outside a request.
		/// </summary>
		public static DepartmentTermSubstitution CurrentEnglishSubstitution(HttpContext context)
		{
			return context?.Items != null && context.Items.TryGetValue(EnglishItemsKey, out var value) ? value as DepartmentTermSubstitution : null;
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

		/// <summary>The word for one item of a term ("Team" for Units renamed Teams), else the localized shipped item word.</summary>
		public async Task<string> SingularAsync(string key)
		{
			return (await GetAsync()).SingularFor(key) ?? DefaultSingular(key);
		}

		/// <summary>
		/// A built-in record (or legacy log) type's name by its number. A type the department has not renamed still follows its
		/// renamed modules ("Unit activity" reads "Team activity" with Units renamed Teams).
		/// </summary>
		public async Task<string> RecordTypeAsync(int recordType)
		{
			var termKey = DepartmentTerms.RecordTypeKey((RmsOperationalRecordType)recordType);
			var saved = (await GetAsync()).TextFor(termKey);
			if (saved != null)
				return saved;

			return DepartmentTerms.IsKnown(termKey)
				? await ShippedRecordTypeAsync(termKey)
				: DepartmentTerms.DefaultRecordTypeName(recordType);
		}

		/// <summary>
		/// A record's type name from its definition key; a department definition shows <paramref name="departmentDefinitionName"/>,
		/// else its key.
		/// </summary>
		public async Task<string> DefinitionAsync(string definitionKey, string departmentDefinitionName = null)
		{
			var termKey = DepartmentTerms.RecordTypeKey(definitionKey);
			if (DepartmentTerms.IsKnown(termKey))
				return (await GetAsync()).TextFor(termKey) ?? await ShippedRecordTypeAsync(termKey);

			return string.IsNullOrWhiteSpace(departmentDefinitionName) ? definitionKey : departmentDefinitionName;
		}

		/// <summary>The shipped name of a term in the current UI language (the English catalog name when no translation exists).</summary>
		public string DefaultName(string key)
		{
			var term = DepartmentTerms.Find(key);
			if (term == null)
				return key;

			return ResourceKeys.TryGetValue(key, out var resource) ? Shipped(resource) ?? term.DefaultName : term.DefaultName;
		}

		/// <summary>The shipped item word of a term in the current UI language; null for a term without one.</summary>
		public string DefaultSingular(string key)
		{
			var term = DepartmentTerms.Find(key);
			if (term == null || !term.HasSingular)
				return null;

			return SingularResourceKeys.TryGetValue(key, out var resource) ? Shipped(resource) ?? term.DefaultSingular : term.DefaultSingular;
		}

		/// <summary>Reads the terminology and readies this request's substitution (the MVC filter calls it before every action).</summary>
		public async Task<DepartmentTermSubstitution> PrepareAsync()
		{
			if (_suppressed)
				return DepartmentTermSubstitution.None;

			if (_substitution == null)
			{
				var config = await GetAsync();
				if (_suppressed)
					return DepartmentTermSubstitution.None;

				var culture = CultureInfo.CurrentUICulture;
				_substitution = Build(config, culture);
				_englishSubstitution = IsEnglish(culture) ? _substitution : Build(config, English);
				var context = _httpContextAccessor?.HttpContext;
				if (context != null)
				{
					context.Items[ItemsKey] = _substitution;
					context.Items[EnglishItemsKey] = _englishSubstitution;
				}
			}

			return _substitution;
		}

		/// <summary>Keeps Resgrid's own wording for the rest of this request (the Terminology page).</summary>
		public void Suppress()
		{
			_suppressed = true;
			_substitution = DepartmentTermSubstitution.None;
			_englishSubstitution = DepartmentTermSubstitution.None;
			var context = _httpContextAccessor?.HttpContext;
			if (context != null)
			{
				context.Items[ItemsKey] = DepartmentTermSubstitution.None;
				context.Items[EnglishItemsKey] = DepartmentTermSubstitution.None;
			}
		}

		/// <summary>
		/// English text built in code with the department's words in place of renamed terms, as a string of
		/// <paramref name="resource"/> would get them (null: the terms renamed everywhere). Returns the text unchanged before
		/// <see cref="PrepareAsync"/>.
		/// </summary>
		public string Apply(string text, string resource = null)
		{
			return ApplyTo(_suppressed ? null : _englishSubstitution, text, resource);
		}

		/// <summary>English text built in code (a CSV header, a tree label) with the request's department terms; see <see cref="Apply"/>.</summary>
		public static string ApplyEnglish(HttpContext context, string text, string resource = null)
		{
			return ApplyTo(CurrentEnglishSubstitution(context), text, resource);
		}

		private static string ApplyTo(DepartmentTermSubstitution substitution, string text, string resource)
		{
			if (substitution == null || substitution.IsEmpty)
				return text;

			return substitution.Apply(text, match => DepartmentTermScopes.Applies(resource, null, match));
		}

		/// <summary>The rules for <c>resgrid.terms.apply</c>: the English words our scripts are written in; null when nothing is renamed.</summary>
		public async Task<DepartmentTermClientRules> ClientRulesAsync()
		{
			if (_suppressed)
				return null;

			var config = await GetAsync();
			if (config == null || !config.HasAny())
				return null;

			var client = new DepartmentTermClientRules();
			foreach (var term in DepartmentTerms.All.Where(t => t.Category == DepartmentTermCategories.Module))
			{
				var name = config.TextFor(term.Key);
				var singular = config.SingularFor(term.Key);
				if (name == null && singular == null)
					continue;

				if (name != null)
				{
					var forms = new List<string> { term.DefaultName };
					if (ExtraEnglishNames.TryGetValue(term.Key, out var extra))
						forms.AddRange(extra);
					client.Rules.Add(new DepartmentTermClientRule { Key = term.Key, Everywhere = DepartmentTermScopes.IsEverywhere(term.Key, true), Forms = forms, Text = name });
				}

				if (singular != null)
				{
					client.Rules.Add(new DepartmentTermClientRule
					{
						Key = term.Key,
						Everywhere = DepartmentTermScopes.IsEverywhere(term.Key, false),
						Forms = new List<string> { term.DefaultSingular },
						Text = singular
					});
				}

				if (DepartmentTermSubstitution.ProtectedPhrases.TryGetValue(term.Key, out var phrases))
					client.Phrases.AddRange(phrases);
				if (DepartmentTermSubstitution.ProtectedPatterns.TryGetValue(term.Key, out var patterns))
					client.Patterns.AddRange(patterns);
			}

			return client.Rules.Count == 0 ? null : client;
		}

		/// <summary>
		/// Whether string <paramref name="name"/> of resource <paramref name="resource"/> is the name of <paramref name="termKey"/>'s
		/// module (Common.NotesModule), which is renamed wherever it is used even when the term's words are not.
		/// </summary>
		public static bool NamesModule(string resource, string name, string termKey)
		{
			return resource != null && name != null && termKey != null && ModuleNameStrings.Contains(resource + "." + name + "|" + termKey);
		}

		/// <summary>
		/// The shipped words of every module term in <paramref name="culture"/>: the translated names for the current UI language,
		/// and the English catalog words only for English. In a translated string an English "Unit" is the Unit app's name, not a unit.
		/// </summary>
		public IEnumerable<DepartmentTermWordForms> ShippedForms(CultureInfo culture)
		{
			var english = IsEnglish(culture);
			var translated = string.Equals(culture.Name, CultureInfo.CurrentUICulture.Name, StringComparison.OrdinalIgnoreCase);
			foreach (var term in DepartmentTerms.All.Where(t => t.Category == DepartmentTermCategories.Module))
			{
				var names = new List<string>();
				var singulars = new List<string>();
				if (translated)
				{
					names.Add(DefaultName(term.Key));
					if (term.HasSingular)
						singulars.Add(DefaultSingular(term.Key));
				}

				if (english)
				{
					names.Add(term.DefaultName);
					if (ExtraEnglishNames.TryGetValue(term.Key, out var extra))
						names.AddRange(extra);
					if (term.HasSingular)
						singulars.Add(term.DefaultSingular);
				}

				yield return new DepartmentTermWordForms(term.Key, names, singulars);
			}
		}

		private static bool IsEnglish(CultureInfo culture)
		{
			return culture != null && culture.TwoLetterISOLanguageName == "en";
		}

		private DepartmentTermSubstitution Build(DepartmentTerminologyConfig config, CultureInfo culture)
		{
			if (config == null || !config.HasAny())
				return DepartmentTermSubstitution.None;

			var cacheKey = culture.Name + "\u001f" + string.Join("\u001e",
				DepartmentTerms.All.Select(t => t.Key + "=" + config.TextFor(t.Key) + "|" + config.SavedSingularFor(t.Key)));

			if (Substitutions.TryGetValue(cacheKey, out var cached))
				return cached;

			if (Substitutions.Count >= MaxCachedSubstitutions)
				Substitutions.Clear();

			try
			{
				return Substitutions.GetOrAdd(cacheKey, _ => DepartmentTermSubstitution.Create(config, ShippedForms(culture).ToList(), culture));
			}
			catch (Exception ex)
			{
				// Labels only: a substitution that cannot be built leaves Resgrid's words in place.
				Framework.Logging.LogException(ex, "Department terminology substitution could not be built; showing the shipped names.");
				return DepartmentTermSubstitution.None;
			}
		}

		private async Task<string> ShippedRecordTypeAsync(string termKey)
		{
			var shipped = DefaultName(termKey);
			var substitution = await PrepareAsync();
			return substitution.IsEmpty
				? shipped
				: substitution.Apply(shipped, match => DepartmentTermScopes.Applies(RecordTypeResource, null, match));
		}

		private string Shipped(ResourceKey resource)
		{
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

			return text == null || text.ResourceNotFound || string.IsNullOrWhiteSpace(text.Value) ? null : text.Value;
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

			/// <summary>The localization class name, as <see cref="DepartmentTermStringLocalizer"/> knows its resource.</summary>
			public string ResourceName => Source switch
			{
				ResourceSource.RecordTypes => RecordTypeResource,
				ResourceSource.WorkOrders => "WorkOrders",
				ResourceSource.Checklists => "Checklists",
				_ => "Common"
			};
		}

		private const string RecordTypeResource = "UserDefinedFields";

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

		/// <summary>Where each term's item word is translated (Common "Term*" strings).</summary>
		private static readonly Dictionary<string, ResourceKey> SingularResourceKeys = new Dictionary<string, ResourceKey>(StringComparer.Ordinal)
		{
			{ DepartmentTerms.Calls, new ResourceKey(ResourceSource.Common, "TermCall") },
			{ DepartmentTerms.Personnel, new ResourceKey(ResourceSource.Common, "TermPerson") },
			{ DepartmentTerms.Units, new ResourceKey(ResourceSource.Common, "TermUnit") },
			{ DepartmentTerms.Contacts, new ResourceKey(ResourceSource.Common, "TermContact") },
			{ DepartmentTerms.Shifts, new ResourceKey(ResourceSource.Common, "TermShift") },
			{ DepartmentTerms.Records, new ResourceKey(ResourceSource.Common, "TermRecord") },
			{ DepartmentTerms.Logs, new ResourceKey(ResourceSource.Common, "TermLog") },
			{ DepartmentTerms.Reports, new ResourceKey(ResourceSource.Common, "TermReport") },
			{ DepartmentTerms.Documents, new ResourceKey(ResourceSource.Common, "TermDocument") },
			{ DepartmentTerms.Notes, new ResourceKey(ResourceSource.Common, "TermNote") },
			{ DepartmentTerms.Trainings, new ResourceKey(ResourceSource.Common, "TermTraining") },
			{ DepartmentTerms.WorkOrders, new ResourceKey(ResourceSource.Common, "TermWorkOrder") },
			{ DepartmentTerms.Checklists, new ResourceKey(ResourceSource.Common, "TermChecklist") }
		};

		/// <summary>English names a module also ships under (the navigation says "Workforce & Business Ops").</summary>
		private static readonly Dictionary<string, string[]> ExtraEnglishNames = new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			{ DepartmentTerms.WorkforceBusiness, new[] { "Workforce & Business Ops" } }
		};

		/// <summary>"Resource.Name|termKey" for every string that is a module's name.</summary>
		private static readonly HashSet<string> ModuleNameStrings = new HashSet<string>(
			ResourceKeys.Where(k => DepartmentTerms.Find(k.Key)?.Category == DepartmentTermCategories.Module)
				.Select(k => k.Value.ResourceName + "." + k.Value.Name + "|" + k.Key), StringComparer.Ordinal);

		/// <summary>Term keys whose shipped name comes from a resource (test hook: every catalog term must have one).</summary>
		public static IEnumerable<string> LocalizedTermKeys => ResourceKeys.Keys;

		/// <summary>Term keys whose shipped item word comes from a resource (test hook: every term with an item word must have one).</summary>
		public static IEnumerable<string> LocalizedSingularKeys => SingularResourceKeys.Keys;

		/// <summary>The Common resource names holding the item words (test hook: every one is translated).</summary>
		public static IEnumerable<string> SingularResourceNames => SingularResourceKeys.Values.Select(k => k.Name);
	}
}

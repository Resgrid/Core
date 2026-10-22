using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Resgrid.Model;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// Resgrid's text exactly as shipped, before the department's terminology is applied. Only the terminology itself reads
	/// through it (the Terminology page shows Resgrid's names next to the department's); everything else takes
	/// <see cref="IStringLocalizer{T}"/>, which carries the department's words.
	/// </summary>
	public interface IShippedStringLocalizerFactory
	{
		IStringLocalizer Create(Type resourceSource);
	}

	public sealed class ShippedStringLocalizerFactory : IShippedStringLocalizerFactory
	{
		private readonly ResourceManagerStringLocalizerFactory _inner;

		public ShippedStringLocalizerFactory(ResourceManagerStringLocalizerFactory inner)
		{
			_inner = inner;
		}

		public IStringLocalizer Create(Type resourceSource)
		{
			return _inner.Create(resourceSource);
		}
	}

	/// <summary>
	/// Every <see cref="IStringLocalizer{T}"/> in the web app comes from here: Resgrid's strings with the signed-in department's
	/// terminology carried through them, so a department that renamed Units to Teams sees "New Team" and "Team Staffing" on
	/// buttons, headings, columns, messages and exports, in every module. The request's substitution is prepared by
	/// <see cref="Filters.DepartmentTermsFilter"/>; outside an MVC request (or with no renamed term) strings pass through untouched.
	/// </summary>
	public sealed class DepartmentTermStringLocalizerFactory : IStringLocalizerFactory
	{
		private readonly IStringLocalizerFactory _inner;
		private readonly IHttpContextAccessor _httpContextAccessor;

		public DepartmentTermStringLocalizerFactory(IStringLocalizerFactory inner, IHttpContextAccessor httpContextAccessor)
		{
			_inner = inner;
			_httpContextAccessor = httpContextAccessor;
		}

		public IStringLocalizer Create(Type resourceSource)
		{
			return new DepartmentTermStringLocalizer(_inner.Create(resourceSource), resourceSource?.Name, _httpContextAccessor);
		}

		public IStringLocalizer Create(string baseName, string location)
		{
			var resource = baseName?.Substring(baseName.LastIndexOf('.') + 1);
			return new DepartmentTermStringLocalizer(_inner.Create(baseName, location), resource, _httpContextAccessor);
		}
	}

	/// <summary>
	/// One resource's strings with the department's words in place of renamed terms. A formatted string is substituted before
	/// its values go in, so a value (a unit called "Unit 5", a member's note) is never rewritten.
	/// </summary>
	public sealed class DepartmentTermStringLocalizer : IStringLocalizer
	{
		private readonly IStringLocalizer _inner;
		private readonly string _resource;
		private readonly IHttpContextAccessor _httpContextAccessor;

		public DepartmentTermStringLocalizer(IStringLocalizer inner, string resource, IHttpContextAccessor httpContextAccessor)
		{
			_inner = inner;
			_resource = resource;
			_httpContextAccessor = httpContextAccessor;
		}

		public LocalizedString this[string name]
		{
			get
			{
				var text = _inner[name];
				var substitution = Current();
				if (substitution == null || text.ResourceNotFound)
					return text;

				return new LocalizedString(name, Apply(substitution, name, text.Value), false, text.SearchedLocation);
			}
		}

		public LocalizedString this[string name, params object[] arguments]
		{
			get
			{
				var substitution = Current();
				if (substitution == null)
					return _inner[name, arguments];

				var format = _inner[name];
				if (format.ResourceNotFound)
					return _inner[name, arguments];

				var value = string.Format(CultureInfo.CurrentCulture, Apply(substitution, name, format.Value), arguments ?? Array.Empty<object>());
				return new LocalizedString(name, value, false, format.SearchedLocation);
			}
		}

		public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
		{
			var substitution = Current();
			foreach (var text in _inner.GetAllStrings(includeParentCultures))
			{
				yield return substitution == null || text.ResourceNotFound
					? text
					: new LocalizedString(text.Name, Apply(substitution, text.Name, text.Value), false, text.SearchedLocation);
			}
		}

		private string Apply(DepartmentTermSubstitution substitution, string name, string value)
		{
			return substitution.Apply(value, match => DepartmentTermScopes.Applies(_resource, name, match));
		}

		private DepartmentTermSubstitution Current()
		{
			var substitution = DepartmentTerminology.CurrentSubstitution(_httpContextAccessor?.HttpContext);
			return substitution == null || substitution.IsEmpty ? null : substitution;
		}
	}

	/// <summary>English text written in code rather than a resource (a tree label, a CSV header, a JSON message).</summary>
	public static class DepartmentTermText
	{
		/// <summary>
		/// <paramref name="text"/> with the signed-in department's words in place of renamed terms. <paramref name="resource"/> is the
		/// localization class of the text's module ("Contacts"), so that module's own words are replaced too; null replaces only
		/// the terms renamed throughout Resgrid. Text built from a resource string already carries the terms.
		/// </summary>
		public static string WithDepartmentTerms(this HttpContext context, string text, string resource = null)
		{
			return DepartmentTerminology.ApplyEnglish(context, text, resource);
		}
	}

	/// <summary>
	/// Where a renamed term's words are replaced. Words that only ever mean the module's thing (call, unit, shift, checklist ...)
	/// are replaced in every module's text; words that are also everyday words elsewhere (a call's notes, an audit log, a
	/// contact phone number, the report a member files) are replaced inside their own module's text and wherever a string names
	/// the module outright. A handful of strings where a word means something else in every language are listed by key.
	/// </summary>
	public static class DepartmentTermScopes
	{
		/// <summary>Terms replaced in every resource.</summary>
		private static readonly HashSet<string> EverywhereTerms = new HashSet<string>(StringComparer.Ordinal)
		{
			DepartmentTerms.Calls, DepartmentTerms.Personnel, DepartmentTerms.Units, DepartmentTerms.Shifts, DepartmentTerms.Inventory,
			DepartmentTerms.Readiness, DepartmentTerms.WorkOrders, DepartmentTerms.Checklists, DepartmentTerms.WorkforceBusiness
		};

		/// <summary>The resources (localization classes) of each term's own module, where every one of its words is replaced.</summary>
		private static readonly Dictionary<string, HashSet<string>> ModuleResources = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
		{
			{ DepartmentTerms.Calls, Set("Call", "Dashboard") },
			{ DepartmentTerms.Personnel, Set("Person") },
			{ DepartmentTerms.Units, Set("Units") },
			{ DepartmentTerms.Contacts, Set("Contacts") },
			{ DepartmentTerms.Mapping, Set("Mapping", "MapVisualizations") },
			{ DepartmentTerms.Shifts, Set("Shifts") },
			{ DepartmentTerms.Records, Set("Records", "ReportSources") },
			{ DepartmentTerms.Logs, Set("Logs") },
			{ DepartmentTerms.Reports, Set("Reports") },
			{ DepartmentTerms.Documents, Set("Documents") },
			{ DepartmentTerms.Calendar, Set("Calendar") },
			{ DepartmentTerms.Notes, Set("Note") },
			{ DepartmentTerms.Trainings, Set("Trainings") },
			{ DepartmentTerms.Inventory, Set("Inventory") },
			{ DepartmentTerms.WorkOrders, Set("WorkOrders") },
			{ DepartmentTerms.Checklists, Set("Checklists") }
		};

		/// <summary>Terms whose item word is generic outside the module: "the person who", "per person".</summary>
		private static readonly HashSet<string> ItemWordInModuleOnly = new HashSet<string>(StringComparer.Ordinal) { DepartmentTerms.Personnel };

		/// <summary>
		/// Translations of an everywhere term that are also everyday words in their language (Spanish "personal" is also the
		/// adjective, Polish "zmiany" also means "changes", Arabic "الوحدات" is also "modules"): replaced inside the module only.
		/// </summary>
		private static readonly Dictionary<string, HashSet<string>> AmbiguousForms = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
		{
			{ DepartmentTerms.Personnel, Set("personal", "personale") },
			{ DepartmentTerms.Shifts, Set("zmiany", "zmiana", "зміни", "зміна") },
			{ DepartmentTerms.Units, Set("الوحدات", "وحدة") }
		};

		/// <summary>Strings where an everywhere term's word means something else, in every language ("unit of measure", "voice call").</summary>
		private static readonly Dictionary<string, HashSet<string>> ExcludedStrings = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
		{
			{
				DepartmentTerms.Units, Set(
					"Inventory.UnitOfMeasure", "Inventory.UnitCost", "Inventory.M4AverageUnitCost", "Inventory.HelpFieldAmount",
					"Inventory.HelpFieldAsset", "Inventory.HelpFieldTrackingMode", "Inventory.HelpFieldUnitCost", "Inventory.StepAssetItemHint",
					"Inventory.StepAssetIdentityHint", "Inventory.StepKitIssueHint",
					"WorkOrders.UnitCost", "WorkOrders.ConditionUnit", "WorkOrders.MeterUnit", "WorkOrders.UseMeterReset",
					"Workforce.DepreciationRate", "Workforce.DistanceUnit", "Workforce.ExpectedAnnualUtilizationHelp", "Workforce.FuelUnit",
					"Workforce.UnitPrice", "Workforce.UsefulLifeQuantity", "Workforce.ResourceComponentsHelp",
					"DataProtection.StepUpClientUnit", "ReportSources.OriginUnitApp", "AdminAssist.Feature.unit-app",
					"Security.SecurityPolicySharedRequiredUnit", "TwoFactor.PasskeyAppUnit", "MapVisualizations.OptionDistanceUnit",
					"Checklists.Units")
			},
			{
				DepartmentTerms.Calls, Set(
					"CustomStatuses.StaffingOnCall", "AdminAssist.Profile.on-call", "CommunicationTest.VoiceCall", "ContractorBilling.CallSign",
					"Deployments.CallSign", "ReportSources.OriginVoice", "EditProfile.HomeNotVerifiedWarning", "EditProfile.GrandfatheredHomeWarning",
					"DepartmentImport.FieldDesc.personnel.home_phone", "DepartmentImport.EntityDesc.emergency-contacts",
					"DepartmentImport.FieldDesc.emergency-contacts.name", "DepartmentImport.FieldDesc.emergency-contacts.notes",
					"DepartmentImport.FieldDesc.emergency-contacts.is_primary", "AdminAssist.TableHelp.ChatbotDepartmentConfig.LlmApiEndpoint")
			}
		};

		/// <summary>Whether <paramref name="match"/> is replaced in string <paramref name="name"/> of resource <paramref name="resource"/>.</summary>
		public static bool Applies(string resource, string name, DepartmentTermMatch match)
		{
			var qualified = resource + "." + name;
			if (ExcludedStrings.TryGetValue(match.Key, out var excluded) && excluded.Contains(qualified))
				return false;

			if (DepartmentTerminology.NamesModule(resource, name, match.Key))
				return true;

			if (ModuleResources.TryGetValue(match.Key, out var own) && resource != null && own.Contains(resource))
				return true;

			if (!EverywhereTerms.Contains(match.Key))
				return false;

			if (!match.IsName && ItemWordInModuleOnly.Contains(match.Key))
				return false;

			return !(AmbiguousForms.TryGetValue(match.Key, out var ambiguous) && ambiguous.Contains(match.Form));
		}

		/// <summary>Whether a term's words are replaced in every module (false: only inside its own module).</summary>
		public static bool IsEverywhere(string key)
		{
			return EverywhereTerms.Contains(key);
		}

		/// <summary>Whether a term's module name (or, with <paramref name="isName"/> false, its item word) is replaced in every module.</summary>
		public static bool IsEverywhere(string key, bool isName)
		{
			return EverywhereTerms.Contains(key) && (isName || !ItemWordInModuleOnly.Contains(key));
		}

		private static HashSet<string> Set(params string[] values)
		{
			return new HashSet<string>(values, StringComparer.Ordinal);
		}
	}
}

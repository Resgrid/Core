using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>Which kind of name a department term replaces (Department -> Terminology).</summary>
	public enum DepartmentTermCategories
	{
		/// <summary>A module's name: its navigation entry and the title of its main page.</summary>
		Module = 1,

		/// <summary>A built-in record type, and the legacy log type with the same number (Run, Training, Work ...).</summary>
		RecordType = 2
	}

	/// <summary>One name a department may replace with its own word.</summary>
	public sealed class DepartmentTermDefinition
	{
		public DepartmentTermDefinition(string key, DepartmentTermCategories category, string defaultName)
		{
			Key = key;
			Category = category;
			DefaultName = defaultName;
		}

		/// <summary>Stable id stored in <see cref="DepartmentTermOverride.Key"/> and sent to the apps. Never rename one.</summary>
		public string Key { get; }

		public DepartmentTermCategories Category { get; }

		/// <summary>The English name Resgrid ships. Screens that localize show their translated name instead.</summary>
		public string DefaultName { get; }
	}

	/// <summary>
	/// The names a department can replace with its own (setting 118, <see cref="DepartmentTerminologyConfig"/>). A term changes
	/// the label only: never a permission, a route, an API value or a stored key. Sentences that mention a module in passing
	/// keep the shipped wording.
	/// </summary>
	public static class DepartmentTerms
	{
		/// <summary>Longest name a department may save for a term.</summary>
		public const int MaxLength = 50;

		public const string Calls = "module.calls";
		public const string Personnel = "module.personnel";
		public const string Units = "module.units";
		public const string Contacts = "module.contacts";
		public const string Mapping = "module.mapping";
		public const string Shifts = "module.shifts";
		public const string Records = "module.records";
		public const string Logs = "module.logs";
		public const string Reports = "module.reports";
		public const string Documents = "module.documents";
		public const string Calendar = "module.calendar";
		public const string Notes = "module.notes";
		public const string Trainings = "module.trainings";
		public const string Inventory = "module.inventory";
		public const string Readiness = "module.readiness";
		public const string WorkOrders = "module.work-orders";
		public const string Checklists = "module.checklists";
		public const string WorkforceBusiness = "module.workforce-business";

		/// <summary>Prefix of every record type term; the rest is the locked definition key without "system.".</summary>
		public const string RecordTypePrefix = "record-type.";

		public static readonly IReadOnlyList<DepartmentTermDefinition> All = new List<DepartmentTermDefinition>
		{
			new DepartmentTermDefinition(Calls, DepartmentTermCategories.Module, "Calls"),
			new DepartmentTermDefinition(Personnel, DepartmentTermCategories.Module, "Personnel"),
			new DepartmentTermDefinition(Units, DepartmentTermCategories.Module, "Units"),
			new DepartmentTermDefinition(Contacts, DepartmentTermCategories.Module, "Contacts"),
			new DepartmentTermDefinition(Mapping, DepartmentTermCategories.Module, "Mapping"),
			new DepartmentTermDefinition(Shifts, DepartmentTermCategories.Module, "Shifts"),
			new DepartmentTermDefinition(Records, DepartmentTermCategories.Module, "Records"),
			new DepartmentTermDefinition(Logs, DepartmentTermCategories.Module, "Logs"),
			new DepartmentTermDefinition(Reports, DepartmentTermCategories.Module, "Reports"),
			new DepartmentTermDefinition(Documents, DepartmentTermCategories.Module, "Documents"),
			new DepartmentTermDefinition(Calendar, DepartmentTermCategories.Module, "Calendar"),
			new DepartmentTermDefinition(Notes, DepartmentTermCategories.Module, "Notes"),
			new DepartmentTermDefinition(Trainings, DepartmentTermCategories.Module, "Trainings"),
			new DepartmentTermDefinition(Inventory, DepartmentTermCategories.Module, "Inventory"),
			new DepartmentTermDefinition(Readiness, DepartmentTermCategories.Module, "Readiness"),
			new DepartmentTermDefinition(WorkOrders, DepartmentTermCategories.Module, "Work Orders"),
			new DepartmentTermDefinition(Checklists, DepartmentTermCategories.Module, "Checklists"),
			new DepartmentTermDefinition(WorkforceBusiness, DepartmentTermCategories.Module, "Workforce & Business"),

			new DepartmentTermDefinition(RecordTypeKey(RmsDefinitionKeys.Run), DepartmentTermCategories.RecordType, "Run"),
			new DepartmentTermDefinition(RecordTypeKey(RmsDefinitionKeys.Training), DepartmentTermCategories.RecordType, "Training"),
			new DepartmentTermDefinition(RecordTypeKey(RmsDefinitionKeys.Work), DepartmentTermCategories.RecordType, "Work"),
			new DepartmentTermDefinition(RecordTypeKey(RmsDefinitionKeys.Meeting), DepartmentTermCategories.RecordType, "Meeting"),
			new DepartmentTermDefinition(RecordTypeKey(RmsDefinitionKeys.Coroner), DepartmentTermCategories.RecordType, "Coroner"),
			new DepartmentTermDefinition(RecordTypeKey(RmsDefinitionKeys.Callback), DepartmentTermCategories.RecordType, "Callback"),
			new DepartmentTermDefinition(RecordTypeKey(RmsDefinitionKeys.UnitActivity), DepartmentTermCategories.RecordType, "Unit activity"),
			new DepartmentTermDefinition(RecordTypeKey(RmsDefinitionKeys.NerisIncidentReport), DepartmentTermCategories.RecordType, "NERIS incident report")
		};

		private static readonly Dictionary<string, DepartmentTermDefinition> ByKey = All.ToDictionary(t => t.Key, StringComparer.Ordinal);

		public static DepartmentTermDefinition Find(string key)
		{
			return key != null && ByKey.TryGetValue(key, out var term) ? term : null;
		}

		public static bool IsKnown(string key)
		{
			return Find(key) != null;
		}

		/// <summary>The term key for a locked definition ("system.run" -> "record-type.run"); null for a department definition key.</summary>
		public static string RecordTypeKey(string definitionKey)
		{
			if (!RmsDefinitionKeys.IsSystemKey(definitionKey))
				return null;

			return RecordTypePrefix + definitionKey.Substring(RmsDefinitionKeys.SystemPrefix.Length);
		}

		public static string RecordTypeKey(RmsOperationalRecordType type)
		{
			return Enum.IsDefined(typeof(RmsOperationalRecordType), type) ? RecordTypeKey(RmsDefinitionKeys.ForType(type)) : null;
		}

		/// <summary>Legacy log types share their numbers with the record types, so a renamed Run is renamed in Logs too.</summary>
		public static string RecordTypeKey(LogTypes type)
		{
			return RecordTypeKey((RmsOperationalRecordType)(int)type);
		}

		/// <summary>
		/// The shipped English name of a built-in record type: "Unit activity", not the enum's "UnitActivity". An unknown
		/// type number falls back to the number itself so nothing renders blank.
		/// </summary>
		public static string DefaultRecordTypeName(int recordType)
		{
			var key = RecordTypeKey((RmsOperationalRecordType)recordType);
			return Find(key)?.DefaultName ?? recordType.ToString();
		}

		/// <summary>
		/// Cleans a name typed by an administrator: control characters removed, runs of white space collapsed, trimmed.
		/// Returns null for an empty name (the term follows the default). Length is checked by <see cref="IsValidText"/>.
		/// </summary>
		public static string Normalize(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return null;

			var builder = new StringBuilder(text.Length);
			var lastWasSpace = false;
			foreach (var c in text)
			{
				if (char.IsWhiteSpace(c))
				{
					if (!lastWasSpace && builder.Length > 0)
						builder.Append(' ');
					lastWasSpace = true;
					continue;
				}

				if (char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format)
					continue;

				builder.Append(c);
				lastWasSpace = false;
			}

			var cleaned = builder.ToString().Trim();
			return cleaned.Length == 0 ? null : cleaned;
		}

		public static bool IsValidText(string normalizedText)
		{
			return normalizedText == null || normalizedText.Length <= MaxLength;
		}
	}

	/// <summary>
	/// Department setting 118 (DepartmentTerminology): the department's own names for modules and built-in record types,
	/// e.g. "Incident" for Run or "Apparatus" for Units. A term with no entry shows the shipped (localized) name. The same
	/// word shows in every language, because it is the department's word.
	/// </summary>
	[ProtoContract]
	public class DepartmentTerminologyConfig
	{
		public DepartmentTerminologyConfig()
		{
			Terms = new List<DepartmentTermOverride>();
		}

		/// <summary>One entry per renamed term; keys come from <see cref="DepartmentTerms"/>.</summary>
		[ProtoMember(1)]
		public List<DepartmentTermOverride> Terms { get; set; }

		/// <summary>The department's name for a term, or null when it uses the default (or a saved one no longer validates).</summary>
		public string TextFor(string key)
		{
			if (key == null)
				return null;

			var text = (Terms ?? new List<DepartmentTermOverride>())
				.FirstOrDefault(t => t != null && string.Equals(t.Key, key, StringComparison.Ordinal))?.Text;
			var normalized = DepartmentTerms.Normalize(text);

			return DepartmentTerms.IsValidText(normalized) ? normalized : null;
		}

		/// <summary>The department's name for a term, or <paramref name="defaultText"/> when it has none.</summary>
		public string TextFor(string key, string defaultText)
		{
			return TextFor(key) ?? defaultText;
		}

		/// <summary>
		/// A built-in record type's name: the department's own, else <paramref name="defaultText"/> (a localized name), else the
		/// shipped English name.
		/// </summary>
		public string RecordTypeName(int recordType, string defaultText = null)
		{
			return TextFor(DepartmentTerms.RecordTypeKey((RmsOperationalRecordType)recordType))
				?? defaultText
				?? DepartmentTerms.DefaultRecordTypeName(recordType);
		}

		/// <summary>
		/// A record's type name from its definition key: built-in keys resolve like <see cref="RecordTypeName(int, string)"/>;
		/// a department definition shows <paramref name="departmentDefinitionName"/> (its own name), else its key.
		/// </summary>
		public string DefinitionName(string definitionKey, string departmentDefinitionName = null)
		{
			var termKey = DepartmentTerms.RecordTypeKey(definitionKey);
			var term = DepartmentTerms.Find(termKey);
			if (term != null)
				return TextFor(termKey) ?? term.DefaultName;

			return string.IsNullOrWhiteSpace(departmentDefinitionName) ? definitionKey : departmentDefinitionName;
		}

		/// <summary>Sets a term's name; null or blank removes it so the term follows the default again. Unknown keys are ignored.</summary>
		public void Set(string key, string text)
		{
			if (!DepartmentTerms.IsKnown(key))
				return;

			Terms ??= new List<DepartmentTermOverride>();
			Terms.RemoveAll(t => t == null || string.Equals(t.Key, key, StringComparison.Ordinal));

			var normalized = DepartmentTerms.Normalize(text);
			if (normalized != null && DepartmentTerms.IsValidText(normalized))
				Terms.Add(new DepartmentTermOverride { Key = key, Text = normalized });
		}
	}

	[ProtoContract]
	public class DepartmentTermOverride
	{
		/// <summary>A <see cref="DepartmentTerms"/> key, e.g. "record-type.run".</summary>
		[ProtoMember(1)]
		public string Key { get; set; }

		/// <summary>The department's name for the term, at most <see cref="DepartmentTerms.MaxLength"/> characters.</summary>
		[ProtoMember(2)]
		public string Text { get; set; }
	}
}

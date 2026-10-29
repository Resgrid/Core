using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.IncidentLogistics;

namespace Resgrid.Web.Areas.User.Models.IncidentBackOffice
{
	/// <summary>How a ledger field renders and binds (plan §14.7 forms: form-horizontal, field-level messages, DepartmentTime inputs).</summary>
	public enum LedgerInput
	{
		Text, TextArea, Int, Decimal, Money, Bool, Date, DateTime, Enum, Ref, User, Contact, Unit, Group, Vendor, ReadOnly
	}

	/// <summary>One field of a ledger table as the generic desk editor shows it.</summary>
	public sealed class LedgerField
	{
		public string Name { get; set; }
		public LedgerInput Input { get; set; }
		public Type EnumType { get; set; }
		/// <summary>The Incident Back Office table a Ref field points at.</summary>
		public string RefTable { get; set; }
		public bool Required { get; set; }
		public int MaxLength { get; set; }
		public bool InList { get; set; }
		/// <summary>ADP-catalogued: rendered only with the table's sensitive claim, never in a list column (plan §15.3a).</summary>
		public bool Protected { get; set; }
		/// <summary>Shown but not posted (lifecycle status, system values).</summary>
		public bool ReadOnly { get; set; }
		public bool Nullable { get; set; }
		/// <summary>Usable as a list filter (enums and booleans).</summary>
		public bool Filter { get; set; }
	}

	public enum LedgerScope { Activation = 0, Department = 1, DepartmentOrActivation = 2 }

	/// <summary>The generic desk descriptor of one table: fields, scope, ordering, search and the claims that gate it.</summary>
	public sealed class LedgerKindDescriptor
	{
		public string Key { get; set; }
		public string Table { get; set; }
		public Type EntityType { get; set; }
		public string Controller { get; set; }
		public LedgerScope Scope { get; set; }
		public string OrderBy { get; set; } = "AddedOn";
		public bool Descending { get; set; } = true;
		public IReadOnlyList<string> SearchColumns { get; set; } = Array.Empty<string>();
		public IReadOnlyList<LedgerField> Fields { get; set; } = Array.Empty<LedgerField>();
		/// <summary>Rows the system writes (snapshots, history, ledgers posted by services): listed, never edited here.</summary>
		public bool IsReadOnly { get; set; }
		/// <summary>Rows people create but never edit or delete (authorizations, adjustments, ETA updates): a correction is a new row.</summary>
		public bool AppendOnly { get; set; }
		public bool CanDelete { get; set; } = true;
		public string Track { get; set; }
		public Func<bool> CanRead { get; set; } = () => false;
		public Func<bool> CanWrite { get; set; } = () => false;
		/// <summary>The claim that reveals the table's protected fields.</summary>
		public Func<bool> CanSeeProtected { get; set; } = () => false;
		/// <summary>The claim that moves the table's lifecycle (defaults to CanWrite).</summary>
		public Func<int, bool> CanTransitionTo { get; set; }
		public string LabelField { get; set; }
		public string StatusField { get; set; }
	}

	public sealed class LedgerOption
	{
		public string Value { get; set; }
		public string Text { get; set; }
	}

	public sealed class LedgerRow
	{
		public string Id { get; set; }
		public int? Status { get; set; }
		public Dictionary<string, string> Cells { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
		public IIncidentLedgerEntity Entity { get; set; }
	}

	/// <summary>Context shared by every incident-scoped desk (plan §14.5 context bar).</summary>
	public class IncidentDeskView
	{
		public IncidentBackOfficeAccess Access { get; set; } = IncidentBackOfficeAccess.Denied;
		public IncidentActivationContext Context { get; set; }
		public string ActivationId => Context?.Activation?.IncidentActivationId;
		public bool CanWrite { get; set; }
		public string Message { get; set; }
		/// <summary>An informational line shown once (a self-service link to hand over).</summary>
		public string Notice { get; set; }
		public bool Saved { get; set; }
		public string Tab { get; set; }
		public string Section { get; set; }
	}

	public sealed class LedgerListView : IncidentDeskView
	{
		public LedgerKindDescriptor Kind { get; set; }
		public List<LedgerRow> Rows { get; set; } = new List<LedgerRow>();
		public List<LedgerField> Columns { get; set; } = new List<LedgerField>();
		public Dictionary<string, string> Filters { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
		public string Search { get; set; }
		public int Page { get; set; }
		public bool HasMore { get; set; }
		public int Total { get; set; }
		public string ParentField { get; set; }
		public string ParentValue { get; set; }
		public string ParentLabel { get; set; }
		public bool KindCanWrite { get; set; }
		public IReadOnlyList<LedgerKindDescriptor> Siblings { get; set; } = Array.Empty<LedgerKindDescriptor>();
	}

	public sealed class LedgerEditView : IncidentDeskView
	{
		public LedgerKindDescriptor Kind { get; set; }
		public string Id { get; set; }
		public int RowVersion { get; set; }
		public bool IsNew => string.IsNullOrWhiteSpace(Id);
		public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
		public Dictionary<string, List<LedgerOption>> Options { get; set; } = new Dictionary<string, List<LedgerOption>>(StringComparer.Ordinal);
		public Dictionary<string, string> Errors { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
		public bool ShowProtected { get; set; }
		public bool KindCanWrite { get; set; }
		public int? Status { get; set; }
		public IReadOnlyList<int> NextStatuses { get; set; } = Array.Empty<int>();
		public Type StatusEnum { get; set; }
		public string ParentField { get; set; }
		public string ParentValue { get; set; }
		/// <summary>Child tables listed under this row (a facility's capacities, a team's slots).</summary>
		public List<LedgerKindDescriptor> Children { get; set; } = new List<LedgerKindDescriptor>();
		public string ChildParentField { get; set; }
		/// <summary>Domain actions on this row (end an assignment, check a participant in), posted to the desk with the row id.</summary>
		public List<LedgerDeskAction> RowActions { get; set; } = new List<LedgerDeskAction>();
	}

	/// <summary>A desk's landing page: each table it hosts with its row count in the selected incident.</summary>
	public sealed class LedgerDeskView : IncidentDeskView
	{
		public List<LedgerDeskEntry> Entries { get; set; } = new List<LedgerDeskEntry>();
		/// <summary>The desk's own workflows (enrollment, scan check-in, team activation) shown above the tables.</summary>
		public List<LedgerDeskAction> Actions { get; set; } = new List<LedgerDeskAction>();
	}

	public sealed class LedgerDeskAction
	{
		public string Action { get; set; }
		public string LabelKey { get; set; }
		public string Icon { get; set; }
		public bool Primary { get; set; }
		/// <summary>Row actions only: the resx key of a confirmation prompt.</summary>
		public string ConfirmKey { get; set; }
	}

	public sealed class LedgerDeskEntry
	{
		public LedgerKindDescriptor Kind { get; set; }
		public int Count { get; set; }
		public bool CanWrite { get; set; }
	}

	/// <summary>
	/// Display text of the generic desk, from the module's resx (Kind.{Entity}, Field.{Entity}.{Field} then Field.{Field},
	/// Enum.{Enum}.{Value}, Help.{Entity}.{Field}); an untranslated key falls back to the humanized name, never to the raw key.
	/// </summary>
	public static class LedgerText
	{
		public static string Kind(IStringLocalizer strings, LedgerKindDescriptor kind)
		{
			var text = strings["Kind." + kind.Key];
			return text.ResourceNotFound ? Humanize(kind.Table) : text.Value;
		}

		public static string Field(IStringLocalizer strings, LedgerKindDescriptor kind, string field)
		{
			var specific = strings["Field." + kind.EntityType.Name + "." + field];
			if (!specific.ResourceNotFound) return specific.Value;
			var common = strings["Field." + field];
			return common.ResourceNotFound ? Humanize(field.EndsWith("Id", StringComparison.Ordinal) && field.Length > 2 ? field.Substring(0, field.Length - 2) : field) : common.Value;
		}

		public static string Help(IStringLocalizer strings, LedgerKindDescriptor kind, string field)
		{
			var text = strings["Help." + kind.EntityType.Name + "." + field];
			return text.ResourceNotFound ? null : text.Value;
		}

		public static string Status(IStringLocalizer strings, Type enumType, int value)
		{
			if (enumType == null) return value.ToString(CultureInfo.InvariantCulture);
			var name = Enum.GetName(enumType, value) ?? value.ToString(CultureInfo.InvariantCulture);
			var text = strings["Enum." + enumType.Name + "." + name];
			return text.ResourceNotFound ? Humanize(name) : text.Value;
		}

		/// <summary>Badge colour by lifecycle meaning; the text always carries the status too (colour is never the only cue).</summary>
		public static string StatusClass(Type enumType, int value)
		{
			var name = enumType == null ? null : Enum.GetName(enumType, value);
			if (name == null) return "label-default";
			if (Regex.IsMatch(name, "Cancel|Reject|Void|Denied|Declined|Disputed|Failed|Withdrawn|Unavailable|Overdue|Blocked")) return "label-danger";
			if (Regex.IsMatch(name, "Closed|Complete|Paid|Released|Done|Settled|Reconciled|Received|Returned|Demobilized|Archived|Sealed|Certified|Fulfilled")) return "label-success";
			if (Regex.IsMatch(name, "Approved|Active|Accepted|Filled|Committed|Ordered|Open|Assigned|InProgress|Issued|CheckedIn|Confirmed|Submitted|Current")) return "label-primary";
			if (Regex.IsMatch(name, "Draft|Pending|Requested|Planned|Proposed|New|Staged")) return "label-default";
			return "label-warning";
		}

		public static string Humanize(string name)
		{
			if (string.IsNullOrEmpty(name)) return name;
			var spaced = Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
			return spaced.StartsWith("Incident ", StringComparison.Ordinal) && spaced.Length > 9 ? spaced.Substring(9) : spaced;
		}
	}
}

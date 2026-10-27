using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Resgrid.Model;
using Resgrid.Model.IncidentLogistics;

namespace Resgrid.Web.Areas.User.Models.IncidentBackOffice
{
	/// <summary>The incident selector (plan §14.5): open activations first, closed ones on request.</summary>
	public sealed class IncidentSelectorView : IncidentDeskView
	{
		public List<IncidentActivation> Activations { get; set; } = new List<IncidentActivation>();
		public bool IncludeClosed { get; set; }
		public bool CanOpen { get; set; }
		public string SelectedId { get; set; }
	}

	/// <summary>The activation wizard (plan §9.0): kind, identity, link to a call, locale and authority.</summary>
	public sealed class IncidentOpenView : IncidentDeskView
	{
		public IncidentActivationInput Input { get; set; } = new IncidentActivationInput();
		public List<LedgerOption> Calls { get; set; } = new List<LedgerOption>();
		public List<LedgerOption> Parents { get; set; } = new List<LedgerOption>();
		public List<LedgerOption> Profiles { get; set; } = new List<LedgerOption>();
		public List<LedgerOption> TimeZones { get; set; } = new List<LedgerOption>();
		public Dictionary<string, string> Errors { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
	}

	/// <summary>One activation's record: identity, links, complex membership and its lifecycle.</summary>
	public sealed class IncidentActivationView : IncidentDeskView
	{
		public IncidentActivation Activation { get; set; }
		public IReadOnlyList<int> NextStatuses { get; set; } = Array.Empty<int>();
		public bool CanChangeStatus { get; set; }
		public bool CanManage { get; set; }
		public List<LedgerOption> Calls { get; set; } = new List<LedgerOption>();
		public List<LedgerOption> Parents { get; set; } = new List<LedgerOption>();
		public string CallLabel { get; set; }
		public Dictionary<string, string> People { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>The command desk (plan §9.17): the incident's numbers with their own as-of time, and the way into each section.</summary>
	public sealed class IncidentCommandDeskView : IncidentDeskView
	{
		public IncidentDeskSummary Summary { get; set; }
		public List<IncidentPositionAssignment> Holders { get; set; } = new List<IncidentPositionAssignment>();
		public Dictionary<string, string> PositionNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, string> People { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>The operations desk: the personnel picture (support status and position apart) and the resources on the incident.</summary>
	public sealed class IncidentOperationsDeskView : IncidentDeskView
	{
		public IncidentDeskSummary Summary { get; set; }
		public List<IncidentPersonnelPictureRow> People { get; set; } = new List<IncidentPersonnelPictureRow>();
		public List<IncidentResource> Resources { get; set; } = new List<IncidentResource>();
	}

	public sealed class IncidentBudgetLineRow
	{
		public IncidentBudgetLine Line { get; set; }
		public decimal Committed { get; set; }
		public decimal Percent => Line == null || Line.RevisedAmount <= 0 ? 0 : Math.Round(Committed / Line.RevisedAmount * 100m, 1);
	}

	/// <summary>The finance desk: budget against commitments, the latest projection, and the finance queues.</summary>
	public sealed class IncidentFinanceDeskView : IncidentDeskView
	{
		public List<IncidentBudgetLineRow> Lines { get; set; } = new List<IncidentBudgetLineRow>();
		public decimal Committed { get; set; }
		public int OpenCommitments { get; set; }
		public IncidentCostProjection Projection { get; set; }
		public int? TimeAwaitingCertification { get; set; }
		public int? TimeAwaitingApproval { get; set; }
		public int? InvoiceExceptions { get; set; }
	}

	/// <summary>The module's department policies (registry §4C settings), one form per policy.</summary>
	public sealed class IncidentSettingsView : IncidentDeskView
	{
		public List<IncidentPolicySection> Sections { get; set; } = new List<IncidentPolicySection>();
	}

	public sealed class IncidentPolicySection
	{
		public string Key { get; set; }
		public int SettingType { get; set; }
		public List<IncidentPolicyField> Fields { get; set; } = new List<IncidentPolicyField>();
		public Dictionary<string, string> Errors { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
	}

	public enum IncidentPolicyInput { Text, Int, Decimal, Bool, List, Enum, ReadOnly, PositionMap }

	public sealed class IncidentPolicyField
	{
		public string Name { get; set; }
		public IncidentPolicyInput Input { get; set; }
		public string Value { get; set; }
		public Type EnumType { get; set; }
		/// <summary>PositionMap only: notice type → comma-separated position codes, defaults shown as the placeholder.</summary>
		public List<(int Notice, string Value, string Default)> Map { get; set; }
	}

	/// <summary>
	/// Reflection form over a policy class: ints, decimals, booleans, strings, string lists (comma-separated), enum-typed ints and the
	/// notification routing map. Acknowledgment stamps (…ByUserId, …On) are display-only and set by the controller, never posted.
	/// </summary>
	public static class IncidentPolicyForm
	{
		private static readonly Dictionary<(Type, string), Type> EnumInts = new Dictionary<(Type, string), Type>
		{
			[(typeof(IncidentEnrollmentPolicy), nameof(IncidentEnrollmentPolicy.Mode))] = typeof(IncidentEnrollmentPolicies)
		};

		private static IEnumerable<PropertyInfo> Properties(Type type) => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite);

		private static bool IsStamp(PropertyInfo p) => p.Name.EndsWith("ByUserId", StringComparison.Ordinal) || p.PropertyType == typeof(DateTime?) || p.PropertyType == typeof(DateTime);

		public static List<IncidentPolicyField> Build(object policy)
		{
			var fields = new List<IncidentPolicyField>();
			foreach (var p in Properties(policy.GetType()))
			{
				var value = p.GetValue(policy);
				var field = new IncidentPolicyField { Name = p.Name };
				if (IsStamp(p))
				{
					field.Input = IncidentPolicyInput.ReadOnly;
					field.Value = value is DateTime d ? d.ToString("u", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture);
				}
				else if (EnumInts.TryGetValue((policy.GetType(), p.Name), out var enumType))
				{
					field.Input = IncidentPolicyInput.Enum;
					field.EnumType = enumType;
					field.Value = Convert.ToString(value, CultureInfo.InvariantCulture);
				}
				else if (p.PropertyType == typeof(Dictionary<int, List<string>>))
				{
					field.Input = IncidentPolicyInput.PositionMap;
					var map = value as Dictionary<int, List<string>> ?? new Dictionary<int, List<string>>();
					field.Map = IncidentNotificationRoutingPolicy.Defaults.Keys.OrderBy(k => k)
						.Select(k => (k, map.TryGetValue(k, out var codes) && codes != null ? string.Join(", ", codes) : string.Empty, string.Join(", ", IncidentNotificationRoutingPolicy.Defaults[k])))
						.ToList();
				}
				else if (p.PropertyType == typeof(List<string>))
				{
					field.Input = IncidentPolicyInput.List;
					field.Value = value is List<string> list ? string.Join(", ", list) : string.Empty;
				}
				else if (p.PropertyType == typeof(bool))
				{
					field.Input = IncidentPolicyInput.Bool;
					field.Value = (bool)value ? "true" : "false";
				}
				else if (p.PropertyType == typeof(int))
				{
					field.Input = IncidentPolicyInput.Int;
					field.Value = ((int)value).ToString(CultureInfo.InvariantCulture);
				}
				else if (p.PropertyType == typeof(decimal))
				{
					field.Input = IncidentPolicyInput.Decimal;
					field.Value = ((decimal)value).ToString("0.####", CultureInfo.InvariantCulture);
				}
				else
				{
					field.Input = IncidentPolicyInput.Text;
					field.Value = Convert.ToString(value, CultureInfo.InvariantCulture);
				}
				fields.Add(field);
			}
			return fields;
		}

		/// <summary>Binds the posted values onto <paramref name="policy"/>; returns field → error key ("InvalidNumber", "InvalidValue", "TooLong").</summary>
		public static Dictionary<string, string> Bind(object policy, IFormCollection form)
		{
			var errors = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var p in Properties(policy.GetType()))
			{
				if (IsStamp(p)) continue;
				var raw = form[p.Name].ToString().Trim();
				if (p.PropertyType == typeof(bool))
				{
					p.SetValue(policy, form[p.Name].Any(v => v == "true"));
				}
				else if (p.PropertyType == typeof(Dictionary<int, List<string>>))
				{
					var map = new Dictionary<int, List<string>>();
					foreach (var notice in IncidentNotificationRoutingPolicy.Defaults.Keys)
					{
						var codes = Split(form[p.Name + "_" + notice.ToString(CultureInfo.InvariantCulture)].ToString());
						if (codes.Any(c => c.Length > 16)) { errors[p.Name] = "TooLong"; continue; }
						if (codes.Count > 0) map[notice] = codes.Select(c => c.ToUpperInvariant()).ToList();
					}
					p.SetValue(policy, map);
				}
				else if (p.PropertyType == typeof(List<string>))
				{
					var items = Split(raw);
					if (items.Any(i => i.Length > 253)) { errors[p.Name] = "TooLong"; continue; }
					p.SetValue(policy, items);
				}
				else if (p.PropertyType == typeof(int))
				{
					if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 0) { errors[p.Name] = "InvalidNumber"; continue; }
					if (EnumInts.TryGetValue((policy.GetType(), p.Name), out var enumType) && !Enum.IsDefined(enumType, number)) { errors[p.Name] = "InvalidValue"; continue; }
					p.SetValue(policy, number);
				}
				else if (p.PropertyType == typeof(decimal))
				{
					if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0) { errors[p.Name] = "InvalidNumber"; continue; }
					p.SetValue(policy, amount);
				}
				else if (p.PropertyType == typeof(string))
				{
					if (raw.Length > 100) { errors[p.Name] = "TooLong"; continue; }
					p.SetValue(policy, string.IsNullOrEmpty(raw) ? null : raw);
				}
			}
			return errors;
		}

		private static List<string> Split(string raw) =>
			(raw ?? string.Empty).Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}
}

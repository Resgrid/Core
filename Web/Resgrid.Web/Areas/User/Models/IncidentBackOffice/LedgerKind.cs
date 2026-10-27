using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Resgrid.Model;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Models.IncidentBackOffice
{
	/// <summary>The generic desk operations of one table, typed by the controller that hosts it.</summary>
	public interface ILedgerKind
	{
		LedgerKindDescriptor Descriptor { get; }
		Task<LedgerListView> ListAsync(LedgerRequest request, CancellationToken cancellationToken);
		Task<LedgerEditView> EditAsync(LedgerRequest request, string id, CancellationToken cancellationToken);
		/// <summary>Binds the descriptor's fields (and only those) from the form and saves; errors come back keyed by field.</summary>
		Task<(string Id, Dictionary<string, string> Errors)> SaveAsync(LedgerRequest request, IFormCollection form, CancellationToken cancellationToken);
		Task DeleteAsync(LedgerRequest request, string id, CancellationToken cancellationToken);
		Task TransitionAsync(LedgerRequest request, string id, int status, string reason, CancellationToken cancellationToken);
		Task<IReadOnlyList<IIncidentLedgerEntity>> RowsAsync(LedgerRequest request, IncidentLedgerQuery query, CancellationToken cancellationToken);
		/// <summary>Rows the desk's list would show (scope and filters applied, no paging).</summary>
		Task<int> CountAsync(LedgerRequest request, CancellationToken cancellationToken);
	}

	/// <summary>Everything a ledger call needs from the request: who, which activation, filters and the desk services.</summary>
	public sealed class LedgerRequest
	{
		public IncidentActor Actor { get; set; }
		public IncidentActivationContext Context { get; set; }
		public IReadOnlyList<string> Scope { get; set; } = Array.Empty<string>();
		public IncidentTime Time { get; set; }
		public IncidentDeskServices Desk { get; set; }
		public Dictionary<string, string> Filters { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
		public string Search { get; set; }
		public int Page { get; set; }
		public int PageSize { get; set; } = 50;
		public string ParentField { get; set; }
		public string ParentValue { get; set; }
		public bool Reveal { get; set; }
	}

	public sealed class LedgerKind<T> : ILedgerKind where T : class, IIncidentLedgerEntity, new()
	{
		private static readonly Dictionary<string, PropertyInfo> Properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p.CanRead && p.CanWrite).ToDictionary(p => p.Name, StringComparer.Ordinal);

		private readonly IIncidentLedgerService<T> _service;

		public LedgerKind(IIncidentLedgerService<T> service, LedgerKindDescriptor descriptor)
		{
			_service = service;
			Descriptor = descriptor;
		}

		public LedgerKindDescriptor Descriptor { get; }

		private IncidentLedgerQuery Query(LedgerRequest request)
		{
			var query = new IncidentLedgerQuery();
			if (Descriptor.Scope == LedgerScope.Activation)
				query.ActivationIds = request.Scope;
			else if (Descriptor.Scope == LedgerScope.DepartmentOrActivation && request.Scope.Count > 0)
			{
				query.ActivationIds = request.Scope;
				query.IncludeDepartmentCatalog = true;
			}
			else if (Descriptor.Scope == LedgerScope.DepartmentOrActivation)
				query.DepartmentCatalogOnly = true;

			foreach (var (name, value) in request.Filters)
			{
				var field = Descriptor.Fields.FirstOrDefault(f => f.Name == name && f.Filter);
				if (field == null || string.IsNullOrWhiteSpace(value)) continue;
				var parsed = Parse(field, value, request.Time, out var error);
				if (error == null && parsed != null) query.Where(name, parsed);
			}

			if (!string.IsNullOrWhiteSpace(request.ParentField) && Descriptor.Fields.Any(f => f.Name == request.ParentField && (f.Input == LedgerInput.Ref || f.Input == LedgerInput.Vendor)))
				query.Where(request.ParentField, request.ParentValue);

			if (!string.IsNullOrWhiteSpace(request.Search) && Descriptor.SearchColumns.Count > 0)
			{
				query.Search = request.Search.Length > 100 ? request.Search.Substring(0, 100) : request.Search;
				query.SearchColumns = Descriptor.SearchColumns;
			}
			query.Order(Descriptor.OrderBy, Descriptor.Descending);
			return query;
		}

		public Task<int> CountAsync(LedgerRequest request, CancellationToken cancellationToken) => _service.CountAsync(request.Actor, Query(request), cancellationToken);

		public async Task<IReadOnlyList<IIncidentLedgerEntity>> RowsAsync(LedgerRequest request, IncidentLedgerQuery query, CancellationToken cancellationToken)
			=> (await _service.ListAsync(request.Actor, query, cancellationToken)).Cast<IIncidentLedgerEntity>().ToList();

		public async Task<LedgerListView> ListAsync(LedgerRequest request, CancellationToken cancellationToken)
		{
			var query = Query(request).Page(request.Page * request.PageSize, request.PageSize + 1);
			var rows = (await _service.ListAsync(request.Actor, query, cancellationToken)).ToList();
			var view = new LedgerListView
			{
				Kind = Descriptor,
				Page = request.Page,
				HasMore = rows.Count > request.PageSize,
				Search = request.Search,
				Filters = request.Filters,
				ParentField = request.ParentField,
				ParentValue = request.ParentValue,
				Columns = Descriptor.Fields.Where(f => f.InList && !f.Protected).ToList()
			};
			rows = rows.Take(request.PageSize).ToList();
			var labels = await LabelsAsync(request, rows, view.Columns, cancellationToken);
			foreach (var row in rows)
			{
				var line = new LedgerRow { Id = row.IdValue as string, Entity = row };
				if (Descriptor.StatusField != null && Properties.TryGetValue(Descriptor.StatusField, out var status))
					line.Status = (int)status.GetValue(row);
				foreach (var column in view.Columns)
					line.Cells[column.Name] = Display(column, Properties[column.Name].GetValue(row), labels, request);
				view.Rows.Add(line);
			}
			return view;
		}

		public async Task<LedgerEditView> EditAsync(LedgerRequest request, string id, CancellationToken cancellationToken)
		{
			var view = new LedgerEditView { Kind = Descriptor, Id = id, ParentField = request.ParentField, ParentValue = request.ParentValue };
			T entity;
			if (string.IsNullOrWhiteSpace(id))
			{
				entity = new T();
				if (!string.IsNullOrWhiteSpace(request.ParentField) && Properties.TryGetValue(request.ParentField, out var parent) && parent.PropertyType == typeof(string))
					parent.SetValue(entity, request.ParentValue);
			}
			else
			{
				view.ShowProtected = request.Reveal && Descriptor.CanSeeProtected();
				entity = await _service.GetAsync(request.Actor, id, view.ShowProtected, cancellationToken);
				if (entity == null)
					return null;
				view.RowVersion = (entity as IIncidentVersioned)?.RowVersion ?? 0;
			}

			if (Descriptor.StatusField != null && Properties.TryGetValue(Descriptor.StatusField, out var status) && !string.IsNullOrWhiteSpace(id))
			{
				view.Status = (int)status.GetValue(entity);
				view.StatusEnum = _service.Lifecycle?.StatusEnum;
				if (_service.Lifecycle != null)
					view.NextStatuses = _service.Lifecycle.NextStatuses(view.Status.Value)
						.Where(s => Descriptor.CanTransitionTo == null ? Descriptor.CanWrite() : Descriptor.CanTransitionTo(s)).ToList();
			}

			foreach (var field in Descriptor.Fields)
				view.Values[field.Name] = Edit(field, Properties[field.Name].GetValue(entity), request);
			await OptionsAsync(request, view, cancellationToken);
			return view;
		}

		public async Task<(string Id, Dictionary<string, string> Errors)> SaveAsync(LedgerRequest request, IFormCollection form, CancellationToken cancellationToken)
		{
			var errors = new Dictionary<string, string>(StringComparer.Ordinal);
			var id = form["Id"].ToString();
			T entity;
			int? expected = null;
			if (string.IsNullOrWhiteSpace(id))
			{
				entity = new T { DepartmentId = request.Actor.DepartmentId };
				if (Descriptor.Scope == LedgerScope.Activation || Descriptor.Scope == LedgerScope.DepartmentOrActivation && form["ForIncident"] == "true")
				{
					if (entity is IIncidentScopedEntity scoped)
						scoped.IncidentActivationId = request.Context?.Activation?.IncidentActivationId;
				}
			}
			else
			{
				entity = await _service.GetAsync(request.Actor, id, false, cancellationToken);
				if (entity == null)
				{
					errors[""] = "RecordNotFound";
					return (null, errors);
				}
				expected = int.TryParse(form["RowVersion"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) ? version : (entity as IIncidentVersioned)?.RowVersion;
			}

			var showProtected = Descriptor.CanSeeProtected();
			foreach (var field in Descriptor.Fields)
			{
				if (field.ReadOnly || field.Input == LedgerInput.ReadOnly || field.Protected && !showProtected)
					continue;
				if (!form.ContainsKey(field.Name) && field.Input != LedgerInput.Bool)
					continue;
				var raw = field.Input == LedgerInput.Bool ? (form[field.Name].Any(v => v == "true") ? "true" : "false") : form[field.Name].ToString();
				if (field.Protected && raw == ProtectedDataEnvelope.RedactionValue)
					continue; // An unrevealed value posted back keeps the stored envelope.
				var value = Parse(field, raw, request.Time, out var error);
				if (error == null && field.Required && (value == null || value is string s && string.IsNullOrWhiteSpace(s)))
					error = "Required";
				if (error != null)
				{
					errors[field.Name] = error;
					continue;
				}
				var property = Properties[field.Name];
				if (value == null && property.PropertyType.IsValueType && System.Nullable.GetUnderlyingType(property.PropertyType) == null)
					continue;
				property.SetValue(entity, value);
			}

			if (errors.Count > 0)
				return (id, errors);
			var saved = await _service.SaveAsync(request.Actor, entity, expected, cancellationToken);
			return (saved.IdValue as string, errors);
		}

		public Task DeleteAsync(LedgerRequest request, string id, CancellationToken cancellationToken) => _service.DeleteAsync(request.Actor, id, cancellationToken);

		public Task TransitionAsync(LedgerRequest request, string id, int status, string reason, CancellationToken cancellationToken)
			=> _service.TransitionAsync(request.Actor, id, status, reason, null, cancellationToken);

		private static object Parse(LedgerField field, string raw, IncidentTime time, out string error)
		{
			error = null;
			var value = raw?.Trim();
			var property = Properties[field.Name];
			var type = System.Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
			if (string.IsNullOrEmpty(value))
				return type == typeof(bool) ? false : null;

			switch (field.Input)
			{
				case LedgerInput.Bool:
					return value == "true" || value == "on";
				case LedgerInput.Int:
				case LedgerInput.Enum:
				case LedgerInput.Unit:
				case LedgerInput.Group:
					if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) { error = "InvalidNumber"; return null; }
					if (field.EnumType != null && !Enum.IsDefined(field.EnumType, number)) { error = "InvalidValue"; return null; }
					return type == typeof(long) ? (object)(long)number : number;
				case LedgerInput.Decimal:
				case LedgerInput.Money:
					if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) { error = "InvalidNumber"; return null; }
					return amount;
				case LedgerInput.Date:
					if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) { error = "InvalidDate"; return null; }
					return date;
				case LedgerInput.DateTime:
					var utc = time.Parse(value);
					if (!utc.HasValue) { error = "InvalidDate"; return null; }
					return utc.Value;
				default:
					if (field.MaxLength > 0 && value.Length > field.MaxLength) { error = "TooLong"; return null; }
					return value;
			}
		}

		private static string Edit(LedgerField field, object value, LedgerRequest request)
		{
			if (value == null) return null;
			return field.Input switch
			{
				LedgerInput.DateTime => request.Time.Input((DateTime)value),
				LedgerInput.Date => IncidentTime.Date((DateTime)value),
				LedgerInput.Bool => (bool)value ? "true" : "false",
				LedgerInput.Decimal or LedgerInput.Money => ((decimal)value).ToString("0.####", CultureInfo.InvariantCulture),
				_ => Convert.ToString(value, CultureInfo.InvariantCulture)
			};
		}

		private string Display(LedgerField field, object value, Dictionary<string, IReadOnlyDictionary<string, string>> labels, LedgerRequest request)
		{
			if (value == null) return string.Empty;
			var strings = request.Desk.Strings;
			switch (field.Input)
			{
				case LedgerInput.Enum:
					var name = Enum.GetName(field.EnumType, value) ?? value.ToString();
					var text = strings["Enum." + field.EnumType.Name + "." + name];
					return text.ResourceNotFound ? name : text.Value;
				case LedgerInput.Bool:
					return (bool)value ? strings["Yes"].Value : strings["No"].Value;
				case LedgerInput.DateTime:
					return request.Time.Format((DateTime)value);
				case LedgerInput.Date:
					return IncidentTime.Date((DateTime)value);
				case LedgerInput.Money:
					return ((decimal)value).ToString("N2", CultureInfo.CurrentCulture);
				case LedgerInput.Decimal:
					return ((decimal)value).ToString("0.##", CultureInfo.CurrentCulture);
				case LedgerInput.Ref:
				case LedgerInput.User:
				case LedgerInput.Contact:
				case LedgerInput.Unit:
				case LedgerInput.Group:
				case LedgerInput.Vendor:
					var key = Convert.ToString(value, CultureInfo.InvariantCulture);
					return labels.TryGetValue(field.Name, out var map) && map.TryGetValue(key, out var label) ? label : key;
				default:
					return Convert.ToString(value, CultureInfo.CurrentCulture);
			}
		}

		private async Task<Dictionary<string, IReadOnlyDictionary<string, string>>> LabelsAsync(LedgerRequest request, IReadOnlyList<T> rows, IEnumerable<LedgerField> fields, CancellationToken cancellationToken)
		{
			var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
			foreach (var field in fields)
			{
				var ids = rows.Select(r => Convert.ToString(Properties[field.Name].GetValue(r), CultureInfo.InvariantCulture)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
				if (ids.Count == 0) continue;
				switch (field.Input)
				{
					case LedgerInput.Ref:
						result[field.Name] = await request.Desk.Lookups.GetLabelsAsync(request.Actor, field.RefTable, ids, cancellationToken);
						break;
					case LedgerInput.Vendor:
						result[field.Name] = await request.Desk.Lookups.GetLabelsAsync(request.Actor, "InventoryVendors", ids, cancellationToken);
						break;
					case LedgerInput.User:
						result[field.Name] = await request.Desk.PeopleAsync(request.Actor.DepartmentId);
						break;
					case LedgerInput.Contact:
						result[field.Name] = await request.Desk.ContactsAsync(request.Actor.DepartmentId);
						break;
					case LedgerInput.Unit:
						result[field.Name] = await request.Desk.UnitsAsync(request.Actor.DepartmentId);
						break;
					case LedgerInput.Group:
						result[field.Name] = await request.Desk.GroupsAsync(request.Actor.DepartmentId);
						break;
				}
			}
			return result;
		}

		private async Task OptionsAsync(LedgerRequest request, LedgerEditView view, CancellationToken cancellationToken)
		{
			foreach (var field in Descriptor.Fields)
			{
				List<LedgerOption> options = null;
				switch (field.Input)
				{
					case LedgerInput.Enum:
						options = Enum.GetValues(field.EnumType).Cast<object>().Select(v => new LedgerOption
						{
							Value = Convert.ToInt32(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
							Text = request.Desk.Strings["Enum." + field.EnumType.Name + "." + Enum.GetName(field.EnumType, v)] is { ResourceNotFound: false } t ? t.Value : Enum.GetName(field.EnumType, v)
						}).ToList();
						break;
					case LedgerInput.Ref:
						options = (await request.Desk.Lookups.GetOptionsAsync(request.Actor, field.RefTable, request.Scope, cancellationToken)).Select(r => new LedgerOption { Value = r.Id, Text = r.Label }).ToList();
						break;
					case LedgerInput.Vendor:
						options = (await request.Desk.Lookups.GetOptionsAsync(request.Actor, "InventoryVendors", request.Scope, cancellationToken)).Select(r => new LedgerOption { Value = r.Id, Text = r.Label }).ToList();
						break;
					case LedgerInput.User:
						options = (await request.Desk.PeopleAsync(request.Actor.DepartmentId)).OrderBy(p => p.Value).Select(p => new LedgerOption { Value = p.Key, Text = p.Value }).ToList();
						break;
					case LedgerInput.Contact:
						options = (await request.Desk.ContactsAsync(request.Actor.DepartmentId)).OrderBy(p => p.Value).Select(p => new LedgerOption { Value = p.Key, Text = p.Value }).ToList();
						break;
					case LedgerInput.Unit:
						options = (await request.Desk.UnitsAsync(request.Actor.DepartmentId)).OrderBy(p => p.Value).Select(p => new LedgerOption { Value = p.Key, Text = p.Value }).ToList();
						break;
					case LedgerInput.Group:
						options = (await request.Desk.GroupsAsync(request.Actor.DepartmentId)).OrderBy(p => p.Value).Select(p => new LedgerOption { Value = p.Key, Text = p.Value }).ToList();
						break;
				}
				if (options != null)
					view.Options[field.Name] = options;
			}
		}
	}
}

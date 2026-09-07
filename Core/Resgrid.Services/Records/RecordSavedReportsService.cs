using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Department saved reports over one definition (RMS plan section 4.1 "Reporting and presentation", RMS-1B):
	/// allowlisted typed columns, bounded filters, one group-by and count/sum/avg/min/max where the pinned field
	/// allows it. Runs go through the visibility-filtered projection query, never raw SQL, and are capped at
	/// <see cref="RmsSavedReportDefinition.MaxRows"/>. Cross-version columns need an explicit mapping; unmapped
	/// versions are reported, never coerced.
	/// </summary>
	public class RecordSavedReportsService : IRecordSavedReportsService
	{
		public static readonly IReadOnlyDictionary<string, string> BuiltInColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["record.number"] = "Record number", ["record.draft_reference"] = "Draft reference", ["record.state"] = "State", ["record.definition_version"] = "Definition version",
			["record.started_on"] = "Started", ["record.ended_on"] = "Ended", ["record.finalized_on"] = "Finalized", ["record.author"] = "Author", ["record.group"] = "Station / group", ["record.call_id"] = "Call"
		};

		private readonly IRmsSavedReportDefinitionsRepository _reports;
		private readonly IRecordDefinitionsService _definitions;
		private readonly IRecordsService _records;
		private readonly IRmsOperationalRecordsRepository _recordRows;
		private readonly IRmsRecordValuesRepository _values;
		private readonly IRmsRecordValueGroupsRepository _groups;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRmsAccessAuditsRepository _audits;

		public RecordSavedReportsService(IRmsSavedReportDefinitionsRepository reports, IRecordDefinitionsService definitions, IRecordsService records, IRmsOperationalRecordsRepository recordRows,
			IRmsRecordValuesRepository values, IRmsRecordValueGroupsRepository groups, IRecordsAuthorizationService authorization, IRmsAccessAuditsRepository audits)
		{
			_reports = reports;
			_definitions = definitions;
			_records = records;
			_recordRows = recordRows;
			_values = values;
			_groups = groups;
			_authorization = authorization;
			_audits = audits;
		}

		public async Task<List<RmsSavedReportDefinition>> GetForDepartmentAsync(int departmentId) => (await _reports.GetForDepartmentAsync(departmentId))?.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList() ?? new List<RmsSavedReportDefinition>();

		public Task<RmsSavedReportDefinition> GetAsync(int departmentId, string reportId) => _reports.GetByIdForDepartmentAsync(departmentId, reportId);

		public async Task<RecordReportValidation> ValidateAsync(int departmentId, RmsSavedReportDefinition report)
		{
			var result = new RecordReportValidation();
			var issues = result.Issues;
			if (report == null) { issues.Add(RecordDefinitionIssue.Error("", "missing", "Nothing to validate.")); return result; }
			if (string.IsNullOrWhiteSpace(report.Name) || report.Name.Length > 200) issues.Add(RecordDefinitionIssue.Error("name", "required", "A report name of at most 200 characters is required."));
			var aggregate = string.IsNullOrWhiteSpace(report.DefinitionKey) ? null : await _definitions.GetAsync(departmentId, report.DefinitionKey);
			if (aggregate == null) { issues.Add(RecordDefinitionIssue.Error("definitionKey", "unknown_definition", "Saved reports run over one department definition.")); return result; }
			var version = report.DefinitionVersion.HasValue ? aggregate.Versions.FirstOrDefault(v => v.Version == report.DefinitionVersion) : aggregate.Published;
			if (version == null) { issues.Add(RecordDefinitionIssue.Error("definitionVersion", "unknown_version", "The definition has no such published version.")); return result; }
			var schema = version.Schema;
			var spec = report.Spec;
			if (spec.Columns.Count == 0) issues.Add(RecordDefinitionIssue.Error("columns", "no_columns", "Choose at least one column."));
			if (spec.Columns.Count > RmsSavedReportDefinition.MaxColumns) issues.Add(RecordDefinitionIssue.Error("columns", "too_many", $"At most {RmsSavedReportDefinition.MaxColumns} columns."));
			if (spec.Filters.Count > RmsSavedReportDefinition.MaxFilters) issues.Add(RecordDefinitionIssue.Error("filters", "too_many", $"At most {RmsSavedReportDefinition.MaxFilters} filters."));
			if (spec.WindowDays.HasValue && (spec.WindowDays < 1 || spec.WindowDays > 3660)) issues.Add(RecordDefinitionIssue.Error("windowDays", "out_of_range", "The window is 1 to 3660 days."));
			if (report.MaxRowsPerRun < 1 || report.MaxRowsPerRun > RmsSavedReportDefinition.MaxRows) issues.Add(RecordDefinitionIssue.Error("maxRowsPerRun", "out_of_range", $"Rows per run is 1 to {RmsSavedReportDefinition.MaxRows}."));
			foreach (var column in spec.Columns)
			{
				if (BuiltInColumns.ContainsKey(column)) continue;
				var field = schema.FindField(column);
				if (field == null) { issues.Add(RecordDefinitionIssue.Error("columns", "unknown_field", $"'{column}' is not a field of version {version.Version}.")); continue; }
				if (!field.Exportable) issues.Add(RecordDefinitionIssue.Error("columns", "not_exportable", $"'{column}' is not exportable."));
				if (field.Classification == RmsFieldClassification.Protected) issues.Add(RecordDefinitionIssue.Error("columns", "protected", $"'{column}' is protected; protected values never enter a saved report."));
				if (field.Classification == RmsFieldClassification.Restricted && !report.IncludeRestricted) issues.Add(RecordDefinitionIssue.Error("columns", "restricted", $"'{column}' is restricted; enable IncludeRestricted (RecordRestricted_View is checked at run time)."));
			}
			foreach (var filter in spec.Filters)
			{
				var field = schema.FindField(filter.FieldKey);
				if (field == null && !BuiltInColumns.ContainsKey(filter.FieldKey ?? string.Empty)) { issues.Add(RecordDefinitionIssue.Error("filters", "unknown_field", $"Filter '{filter.FieldKey}' names no field.")); continue; }
				if (field != null && !field.Filterable) issues.Add(RecordDefinitionIssue.Error("filters", "not_filterable", $"'{filter.FieldKey}' is not filterable."));
				if (field != null && field.Classification != RmsFieldClassification.Standard && !report.IncludeRestricted) issues.Add(RecordDefinitionIssue.Error("filters", "restricted", $"'{filter.FieldKey}' is restricted."));
				if (filter.Operator == RmsRuleOperator.And || filter.Operator == RmsRuleOperator.Or) issues.Add(RecordDefinitionIssue.Error("filters", "bad_operator", "Report filters combine with AND; nested AND/OR is not a filter."));
			}
			if (!string.IsNullOrWhiteSpace(spec.GroupByFieldKey))
			{
				var field = schema.FindField(spec.GroupByFieldKey);
				if (field == null && !BuiltInColumns.ContainsKey(spec.GroupByFieldKey)) issues.Add(RecordDefinitionIssue.Error("groupBy", "unknown_field", $"'{spec.GroupByFieldKey}' names no field."));
				else if (field != null && !field.Groupable) issues.Add(RecordDefinitionIssue.Error("groupBy", "not_groupable", $"'{spec.GroupByFieldKey}' cannot group a report."));
			}
			foreach (var aggregate2 in spec.Aggregates)
			{
				if (aggregate2.Aggregate == RmsReportAggregate.Count) continue;
				var field = schema.FindField(aggregate2.FieldKey);
				if (field == null) issues.Add(RecordDefinitionIssue.Error("aggregates", "unknown_field", $"Aggregate '{aggregate2.FieldKey}' names no field."));
				else if (!field.Aggregatable) issues.Add(RecordDefinitionIssue.Error("aggregates", "not_aggregatable", $"'{aggregate2.FieldKey}' cannot be summed or averaged."));
			}
			foreach (var mapping in spec.VersionMappings)
				if (!aggregate.Versions.Any(v => v.Version == mapping.Key)) issues.Add(RecordDefinitionIssue.Warning("versionMappings", "unknown_version", $"Mapping for version {mapping.Key} names a version that does not exist."));
			return result;
		}

		public async Task<RmsSavedReportDefinition> SaveAsync(int departmentId, string userId, RmsSavedReportDefinition report, CancellationToken cancellationToken = default)
		{
			if (report == null) throw new ArgumentNullException(nameof(report));
			await RequireManageAsync(userId, departmentId);
			if (report.IncludeRestricted && !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords))
				throw new UnauthorizedAccessException("Including restricted fields needs RecordRestricted_View.");
			var validation = await ValidateAsync(departmentId, report);
			if (!validation.IsValid) throw new ArgumentException(string.Join(" ", validation.Issues.Where(i => i.Severity == "error").Select(i => i.Message)));

			var now = DateTime.UtcNow;
			var existing = string.IsNullOrWhiteSpace(report.RmsSavedReportDefinitionId) ? null : await _reports.GetByIdForDepartmentAsync(departmentId, report.RmsSavedReportDefinitionId);
			if (existing == null)
			{
				report.RmsSavedReportDefinitionId = Guid.NewGuid().ToString();
				report.DepartmentId = departmentId;
				report.ProtectionId = Guid.NewGuid().ToString();
				report.CreatedOn = now; report.CreatedByUserId = userId; report.ModifiedOn = now; report.ModifiedByUserId = userId; report.RowVersion = 1;
				await _reports.InsertAsync(report, cancellationToken, true);
				await AuditAsync(departmentId, userId, report, "Create saved report", cancellationToken);
				return report;
			}
			if (existing.RowVersion != report.RowVersion) throw new RecordConcurrencyException(existing.RmsSavedReportDefinitionId, report.RowVersion, existing.RowVersion);
			existing.Name = report.Name; existing.Description = report.Description; existing.DefinitionKey = report.DefinitionKey; existing.DefinitionVersion = report.DefinitionVersion;
			existing.SpecJson = report.SpecJson; existing.MaxRowsPerRun = report.MaxRowsPerRun; existing.IncludeRestricted = report.IncludeRestricted;
			existing.ModifiedOn = now; existing.ModifiedByUserId = userId; existing.RowVersion += 1;
			await _reports.UpdateAsync(existing, cancellationToken, true);
			await AuditAsync(departmentId, userId, existing, "Update saved report", cancellationToken);
			return existing;
		}

		public async Task<bool> DeleteAsync(int departmentId, string userId, string reportId, CancellationToken cancellationToken = default)
		{
			await RequireManageAsync(userId, departmentId);
			var report = await _reports.GetByIdForDepartmentAsync(departmentId, reportId);
			if (report == null) return false;
			report.DeletedOn = DateTime.UtcNow; report.ModifiedOn = report.DeletedOn.Value; report.ModifiedByUserId = userId; report.RowVersion += 1;
			await _reports.UpdateAsync(report, cancellationToken, true);
			await AuditAsync(departmentId, userId, report, "Delete saved report", cancellationToken);
			return true;
		}

		public async Task<RecordReportResult> RunAsync(int departmentId, string userId, string reportId, CancellationToken cancellationToken = default)
		{
			var report = await _reports.GetByIdForDepartmentAsync(departmentId, reportId) ?? throw new ArgumentException("Unknown report.", nameof(reportId));
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.CreateRecord) && !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ManageRecordReports))
				throw new UnauthorizedAccessException("Running Record reports is not authorized.");
			var canViewRestricted = await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords);
			if (report.IncludeRestricted && !canViewRestricted) throw new UnauthorizedAccessException("This report includes restricted fields; RecordRestricted_View is required to run it.");
			var validation = await ValidateAsync(departmentId, report);
			if (!validation.IsValid) throw new InvalidOperationException("The report no longer validates: " + string.Join(" ", validation.Issues.Where(i => i.Severity == "error").Select(i => i.Message)));

			var aggregate = await _definitions.GetAsync(departmentId, report.DefinitionKey);
			var versions = aggregate.Versions.ToDictionary(v => v.Version);
			var reportVersion = report.DefinitionVersion.HasValue ? versions[report.DefinitionVersion.Value] : aggregate.Published;
			var spec = report.Spec;
			var take = Math.Min(report.MaxRowsPerRun <= 0 ? RmsSavedReportDefinition.MaxRows : report.MaxRowsPerRun, RmsSavedReportDefinition.MaxRows);

			var states = spec.IncludeDrafts
				? new[] { RmsRecordState.Draft, RmsRecordState.ReadyForReview, RmsRecordState.Returned, RmsRecordState.Approved, RmsRecordState.Finalized, RmsRecordState.Amended, RmsRecordState.Submitted, RmsRecordState.Accepted, RmsRecordState.Rejected, RmsRecordState.Corrected }
				: new[] { RmsRecordState.Finalized, RmsRecordState.Amended, RmsRecordState.Submitted, RmsRecordState.Accepted, RmsRecordState.Rejected, RmsRecordState.Corrected };
			var query = new RmsRecordQuery
			{
				DefinitionKey = aggregate.Definition.DefinitionKey, States = states.Select(s => (int)s).ToList(), ViewerUserId = userId, Skip = 0, Take = take + 1,
				VisibleGroupIds = await _authorization.IsGroupScopedAsync(departmentId) ? await _authorization.GetVisibleGroupIdsAsync(userId, departmentId) : null
			};
			var projections = await _records.QueryAsync(departmentId, query);
			var since = spec.WindowDays.HasValue ? DateTime.UtcNow.AddDays(-spec.WindowDays.Value) : (DateTime?)null;
			if (since.HasValue) projections = projections.Where(p => (p.FinalizedOn ?? p.OccurredOn ?? p.RecordCreatedOn) >= since.Value).ToList();
			var truncated = projections.Count > take;
			projections = projections.Take(take).ToList();

			var rows = (await _recordRows.GetByIdsAsync(departmentId, projections.Select(p => p.SourceId)))?.ToList() ?? new List<RmsOperationalRecord>();
			var revisionIds = rows.Where(r => r.CurrentRevisionId != null && !RmsLifecycle.IsEditable((RmsRecordState)r.State)).Select(r => r.CurrentRevisionId).ToList();
			var draftIds = rows.Where(r => r.CurrentRevisionId == null || RmsLifecycle.IsEditable((RmsRecordState)r.State)).Select(r => r.RmsOperationalRecordId).ToList();
			var valueRows = new List<RmsRecordValue>(); var groupRows = new List<RmsRecordValueGroup>();
			if (revisionIds.Count > 0) { valueRows.AddRange(await _values.GetForRevisionsAsync(departmentId, revisionIds) ?? Enumerable.Empty<RmsRecordValue>()); groupRows.AddRange(await _groups.GetForRevisionsAsync(departmentId, revisionIds) ?? Enumerable.Empty<RmsRecordValueGroup>()); }
			if (draftIds.Count > 0) { valueRows.AddRange(await _values.GetForRecordsAsync(departmentId, draftIds, true) ?? Enumerable.Empty<RmsRecordValue>()); groupRows.AddRange(await _groups.GetForRecordsAsync(departmentId, draftIds, true) ?? Enumerable.Empty<RmsRecordValueGroup>()); }

			var result = new RecordReportResult { ReportId = report.RmsSavedReportDefinitionId, Name = report.Name, DefinitionKey = report.DefinitionKey, DefinitionVersion = reportVersion?.Version, RanOn = DateTime.UtcNow, Columns = spec.Columns.ToList(), Truncated = truncated };
			result.ColumnLabels = spec.Columns.Select(c => BuiltInColumns.TryGetValue(c, out var l) ? l : reportVersion?.Schema.FindField(c)?.Label ?? c).ToList();

			var shaped = new List<(RmsOperationalRecord Record, RecordValueSet Values, RecordDefinitionSchema Schema, Dictionary<string, string> Map)>();
			foreach (var record in rows)
			{
				if (!versions.TryGetValue(record.DefinitionVersion, out var version)) { if (!result.UnmappedVersions.Contains(record.DefinitionVersion)) result.UnmappedVersions.Add(record.DefinitionVersion); continue; }
				var isDraft = draftIds.Contains(record.RmsOperationalRecordId);
				var set = RecordTypedValuesService.Shape(version.Schema,
					groupRows.Where(g => g.RecordId == record.RmsOperationalRecordId && (isDraft ? g.RevisionId == null : g.RevisionId == record.CurrentRevisionId)),
					valueRows.Where(v => v.RecordId == record.RmsOperationalRecordId && (isDraft ? v.RevisionId == null : v.RevisionId == record.CurrentRevisionId)), canViewRestricted && report.IncludeRestricted);
				Dictionary<string, string> map = null;
				if (reportVersion != null && record.DefinitionVersion != reportVersion.Version)
				{
					if (!spec.VersionMappings.TryGetValue(record.DefinitionVersion, out map))
					{
						// Same keys carry over; anything else is unmapped and the version is reported.
						map = spec.Columns.Concat(spec.Filters.Select(f => f.FieldKey)).Concat(new[] { spec.GroupByFieldKey }).Concat(spec.Aggregates.Select(a => a.FieldKey)).Where(k => k != null && version.Schema.FindField(k) != null).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(k => k, k => k, StringComparer.OrdinalIgnoreCase);
						if (spec.Columns.Any(c => !BuiltInColumns.ContainsKey(c) && !map.ContainsKey(c)) && !result.UnmappedVersions.Contains(record.DefinitionVersion)) result.UnmappedVersions.Add(record.DefinitionVersion);
					}
				}
				shaped.Add((record, set, version.Schema, map));
			}

			var matched = shaped.Where(s => spec.Filters.All(f => Matches(f, Cell(s, f.FieldKey), s.Record))).ToList();
			result.TotalMatched = matched.Count;
			IEnumerable<(RmsOperationalRecord Record, RecordValueSet Values, RecordDefinitionSchema Schema, Dictionary<string, string> Map)> ordered = matched;
			if (!string.IsNullOrWhiteSpace(spec.SortFieldKey))
			{
				Func<(RmsOperationalRecord Record, RecordValueSet Values, RecordDefinitionSchema Schema, Dictionary<string, string> Map), object> key = s => { var c = Cell(s, spec.SortFieldKey); return c?.Number ?? (object)(c?.Value ?? BuiltIn(s.Record, spec.SortFieldKey) ?? string.Empty); };
				ordered = spec.SortDescending ? matched.OrderByDescending(key, Comparer<object>.Create(CompareValues)) : matched.OrderBy(key, Comparer<object>.Create(CompareValues));
			}
			foreach (var item in ordered)
				result.Rows.Add(spec.Columns.Select(c => BuiltInColumns.ContainsKey(c) ? BuiltIn(item.Record, c) : Cell(item, c)?.Display ?? string.Empty).ToList());

			if (!string.IsNullOrWhiteSpace(spec.GroupByFieldKey) || spec.Aggregates.Count > 0)
			{
				var groups = string.IsNullOrWhiteSpace(spec.GroupByFieldKey)
					? new[] { new { Key = "(all)", Items = matched } }.Select(g => (g.Key, g.Items.AsEnumerable()))
					: matched.GroupBy(s => BuiltInColumns.ContainsKey(spec.GroupByFieldKey) ? BuiltIn(s.Record, spec.GroupByFieldKey) ?? "(blank)" : Cell(s, spec.GroupByFieldKey)?.Display ?? "(blank)", StringComparer.OrdinalIgnoreCase).Select(g => (g.Key, g.AsEnumerable()));
				foreach (var (groupKey, items) in groups.OrderBy(g => g.Item1, StringComparer.OrdinalIgnoreCase))
				{
					var list = items.ToList();
					var group = new RecordReportGroup { GroupKey = groupKey, GroupLabel = groupKey, Count = list.Count };
					foreach (var aggregate2 in spec.Aggregates)
					{
						var name = aggregate2.Aggregate == RmsReportAggregate.Count ? "count" : aggregate2.Aggregate.ToString().ToLowerInvariant() + ":" + aggregate2.FieldKey;
						if (aggregate2.Aggregate == RmsReportAggregate.Count) { group.Aggregates[name] = list.Count; continue; }
						var numbers = list.Select(s => Cell(s, aggregate2.FieldKey)).Where(c => c?.Number.HasValue == true).Select(c => c.CanonicalNumber ?? c.Number.Value).ToList();
						group.Aggregates[name] = numbers.Count == 0 ? (decimal?)null : aggregate2.Aggregate switch
						{
							RmsReportAggregate.Sum => numbers.Sum(),
							RmsReportAggregate.Average => decimal.Round(numbers.Average(), 4),
							RmsReportAggregate.Minimum => numbers.Min(),
							RmsReportAggregate.Maximum => numbers.Max(),
							_ => null
						};
					}
					result.Groups.Add(group);
				}
			}
			if (result.UnmappedVersions.Count > 0) result.Warnings.Add("Definition versions without a column mapping: " + string.Join(", ", result.UnmappedVersions.OrderBy(v => v)) + ". Declare a mapping to include them.");
			if (truncated) result.Warnings.Add($"The run stopped at {take} records; narrow the window or filters.");

			report.LastRunOn = result.RanOn; report.LastRunByUserId = userId;
			await _reports.UpdateAsync(report, cancellationToken, true);
			await AuditAsync(departmentId, userId, report, $"Run saved report ({result.TotalMatched} records)", cancellationToken);
			return result;
		}

		private static RecordValueCell Cell((RmsOperationalRecord Record, RecordValueSet Values, RecordDefinitionSchema Schema, Dictionary<string, string> Map) item, string key)
		{
			if (string.IsNullOrWhiteSpace(key) || BuiltInColumns.ContainsKey(key)) return null;
			var mapped = item.Map == null ? key : item.Map.TryGetValue(key, out var m) ? m : null;
			if (mapped == null) return null;
			return item.Values.Scalar(mapped) ?? item.Values.Sections.Where(s => s.Repeating).SelectMany(s => s.Rows).SelectMany(r => r.Cells).FirstOrDefault(c => string.Equals(c.FieldKey, mapped, StringComparison.OrdinalIgnoreCase));
		}

		private static string BuiltIn(RmsOperationalRecord record, string key)
		{
			switch ((key ?? string.Empty).ToLowerInvariant())
			{
				case "record.number": return record.RecordNumber;
				case "record.draft_reference": return record.DraftReference;
				case "record.state": return ((RmsRecordState)record.State).ToString();
				case "record.definition_version": return record.DefinitionVersion.ToString(CultureInfo.InvariantCulture);
				case "record.started_on": return record.StartedOn?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
				case "record.ended_on": return record.EndedOn?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
				case "record.finalized_on": return record.FinalizedOn?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
				case "record.author": return record.AuthorUserId;
				case "record.group": return record.StationGroupId?.ToString(CultureInfo.InvariantCulture);
				case "record.call_id": return record.CallId?.ToString(CultureInfo.InvariantCulture);
				default: return null;
			}
		}

		private static bool Matches(RecordReportFilter filter, RecordValueCell cell, RmsOperationalRecord record)
		{
			var value = cell?.Value ?? BuiltIn(record, filter.FieldKey);
			var values = cell?.Values ?? (value == null ? new List<string>() : new List<string> { value });
			switch (filter.Operator)
			{
				case RmsRuleOperator.IsEmpty: return values.Count == 0 && string.IsNullOrWhiteSpace(value);
				case RmsRuleOperator.IsNotEmpty: return values.Count > 0 && !string.IsNullOrWhiteSpace(value);
				case RmsRuleOperator.Equals: return values.Any(v => string.Equals(v, filter.Value, StringComparison.OrdinalIgnoreCase)) || cell != null && string.Equals(cell.Display, filter.Value, StringComparison.OrdinalIgnoreCase);
				case RmsRuleOperator.NotEquals: return !(values.Any(v => string.Equals(v, filter.Value, StringComparison.OrdinalIgnoreCase)) || cell != null && string.Equals(cell.Display, filter.Value, StringComparison.OrdinalIgnoreCase));
				case RmsRuleOperator.InSet: return values.Any(v => (filter.Values ?? new List<string>()).Contains(v, StringComparer.OrdinalIgnoreCase));
				case RmsRuleOperator.NotInSet: return !values.Any(v => (filter.Values ?? new List<string>()).Contains(v, StringComparer.OrdinalIgnoreCase));
				case RmsRuleOperator.InRange:
					if (cell?.Number.HasValue == true) { var n = cell.CanonicalNumber ?? cell.Number.Value; return (!filter.Min.HasValue || n >= filter.Min) && (!filter.Max.HasValue || n <= filter.Max); }
					if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when)) return (!filter.MinDate.HasValue || when >= filter.MinDate) && (!filter.MaxDate.HasValue || when <= filter.MaxDate);
					return false;
				default: return true;
			}
		}

		private static int CompareValues(object a, object b)
		{
			if (a is decimal x && b is decimal y) return x.CompareTo(y);
			return string.Compare(Convert.ToString(a, CultureInfo.InvariantCulture), Convert.ToString(b, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
		}

		public string ToCsv(RecordReportResult result)
		{
			var sb = new StringBuilder();
			sb.Append((char)0xFEFF);
			sb.AppendLine(string.Join(",", result.ColumnLabels.Select(l => RecordsExportRenderer.Cell(l, ","))));
			foreach (var row in result.Rows) sb.AppendLine(string.Join(",", row.Select(c => RecordsExportRenderer.Cell(c, ","))));
			if (result.Groups.Count > 0)
			{
				sb.AppendLine();
				var aggregateNames = result.Groups.SelectMany(g => g.Aggregates.Keys).Distinct().ToList();
				sb.AppendLine(string.Join(",", new[] { "Group", "Count" }.Concat(aggregateNames).Select(l => RecordsExportRenderer.Cell(l, ","))));
				foreach (var group in result.Groups)
					sb.AppendLine(string.Join(",", new[] { group.GroupLabel, group.Count.ToString(CultureInfo.InvariantCulture) }.Concat(aggregateNames.Select(n => group.Aggregates.TryGetValue(n, out var v) && v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : string.Empty)).Select(c => RecordsExportRenderer.Cell(c, ","))));
			}
			return sb.ToString();
		}

		private async Task RequireManageAsync(string userId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(userId) || !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ManageRecordReports))
				throw new UnauthorizedAccessException("Managing Record reports is not authorized.");
		}

		private Task AuditAsync(int departmentId, string userId, RmsSavedReportDefinition report, string purpose, CancellationToken cancellationToken)
			=> _audits.InsertAsync(new RmsAccessAudit { DepartmentId = departmentId, RecordId = report.RmsSavedReportDefinitionId, Action = (int)RmsAccessAuditAction.Export, ActorUserId = userId, Purpose = purpose + " " + report.Name, OriginClient = (int)RmsOriginClient.Web, Successful = true, OccurredOn = DateTime.UtcNow, CorrelationId = report.RmsSavedReportDefinitionId }, cancellationToken, true);
	}
}

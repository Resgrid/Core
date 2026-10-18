using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>The sealed canonical form of a staged row: typed values, reference tokens and kept custom fields.</summary>
	public sealed class ImportCanonicalPayload
	{
		/// <summary>Field key → canonical string or list (references hold the cleaned source text).</summary>
		[JsonProperty("v")]
		public Dictionary<string, object> Values { get; set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Reference field key → resolved tokens (src:/id:/new:/label:), set by L6.</summary>
		[JsonProperty("r")]
		public Dictionary<string, List<string>> References { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

		/// <summary>"Keep as custom field" columns: label → value.</summary>
		[JsonProperty("c")]
		public Dictionary<string, string> CustomFields { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	public sealed class ImportValidationSummary
	{
		public int Rows { get; set; }
		public int Ready { get; set; }
		public int Warnings { get; set; }
		public int Blocked { get; set; }
		public int Excluded { get; set; }
		public ImportDatasetStatus Status { get; set; }
	}

	public interface IImportValidationService
	{
		/// <summary>Runs ladder levels L1–L6 over a dataset's staged rows and refreshes its counts, status and staging digest.</summary>
		Task<ImportValidationSummary> ValidateDatasetAsync(int departmentId, string datasetId, Func<CancellationToken, Task<bool>> heartbeat,
			CancellationToken cancellationToken = default);

		/// <summary>Decrypts a dataset's staged rows for writers (typed canonical values with resolved reference tokens).</summary>
		Task<List<ImportCanonicalRow>> ReadCanonicalRowsAsync(ImportDataset dataset, int afterRowNumber, int take, CancellationToken cancellationToken = default);

		/// <summary>Hash used for value-map lookups (normalized source value).</summary>
		string ValueHash(string value);
	}

	/// <summary>
	/// The verification ladder over staged rows, levels L1–L6 (wizard plan section 9): parse shape, typed conversion,
	/// required and cross-field rules, in-file uniqueness and hierarchy cycles, and references. Deterministic for a given
	/// file, mapping, value maps and catalog version; issues carry codes, columns and row numbers, never values; an
	/// administrator's decision on an issue survives a re-run.
	/// </summary>
	public class ImportValidationService : IImportValidationService
	{
		private const int PageSize = 500;

		private readonly IImportWorkspacesRepository _workspaces;
		private readonly IImportDatasetsRepository _datasets;
		private readonly IImportArtifactsRepository _artifacts;
		private readonly IImportSourcesRepository _sources;
		private readonly IImportStagedRowsRepository _rows;
		private readonly IImportIssuesRepository _issues;
		private readonly IImportColumnMapsRepository _columnMaps;
		private readonly IImportValueMapsRepository _valueMaps;
		private readonly IImportCatalog _catalog;
		private readonly IImportKeyService _keys;
		private readonly IImportArtifactCipher _cipher;
		private readonly IDepartmentsService _departments;
		private readonly IAddressService _addresses;
		private readonly IPhoneNumberProcesserProvider _phones;
		private readonly IEnumerable<IImportTargetWriter> _writers;
		private readonly IImportExternalIdsRepository _externalIds;

		public ImportValidationService(IImportWorkspacesRepository workspaces, IImportDatasetsRepository datasets, IImportArtifactsRepository artifacts,
			IImportSourcesRepository sources, IImportStagedRowsRepository rows, IImportIssuesRepository issues, IImportColumnMapsRepository columnMaps,
			IImportValueMapsRepository valueMaps, IImportCatalog catalog, IImportKeyService keys, IImportArtifactCipher cipher, IDepartmentsService departments,
			IAddressService addresses, IPhoneNumberProcesserProvider phones, IEnumerable<IImportTargetWriter> writers, IImportExternalIdsRepository externalIds)
		{
			_externalIds = externalIds;
			_workspaces = workspaces;
			_datasets = datasets;
			_artifacts = artifacts;
			_sources = sources;
			_rows = rows;
			_issues = issues;
			_columnMaps = columnMaps;
			_valueMaps = valueMaps;
			_catalog = catalog;
			_keys = keys;
			_cipher = cipher;
			_departments = departments;
			_addresses = addresses;
			_phones = phones;
			_writers = writers;
		}

		public string ValueHash(string value) =>
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ImportFieldConverter.Normalize(value)))).ToLowerInvariant();

		public async Task<ImportValidationSummary> ValidateDatasetAsync(int departmentId, string datasetId, Func<CancellationToken, Task<bool>> heartbeat,
			CancellationToken cancellationToken = default)
		{
			var dataset = await _datasets.GetByIdAsync(departmentId, datasetId, cancellationToken) ?? throw new DepartmentImportException("import_dataset_not_found", 404);
			var workspace = await _workspaces.GetByIdAsync(departmentId, dataset.WorkspaceId, cancellationToken);
			var artifact = await _artifacts.GetByIdAsync(departmentId, dataset.ArtifactId, cancellationToken);
			var catalog = _catalog.GetVersion(workspace?.CatalogVersion) ?? _catalog;
			var contract = catalog.GetContract(dataset.EntityKey);
			var map = await LoadMapAsync(dataset, cancellationToken);
			var issues = new List<ImportIssue>();
			var summary = new ImportValidationSummary();

			if (artifact == null || artifact.PurgedOn.HasValue)
				return await FinishAsync(dataset, issues, summary, ImportDatasetStatus.Failed, "IMP-ARTIFACT-PURGED", cancellationToken);

			if (contract == null || map == null || !dataset.DestinationConfirmed)
			{
				if (contract != null && dataset.DetectionConfidence < ImportColumnMatcher.AutoConfirmThreshold && !dataset.DestinationConfirmed)
					issues.Add(ImportIssueFactory.Create(dataset, null, null, null, "IMP-DETECT-LOW-CONFIDENCE", ImportLadderLevel.L2Shape, ImportIssueSeverity.Blocking,
						new Dictionary<string, string> { ["confidence"] = dataset.DetectionConfidence.ToString(CultureInfo.InvariantCulture) }));
				return await FinishAsync(dataset, issues, summary, ImportDatasetStatus.NeedsMapping, null, cancellationToken);
			}

			var key = await _keys.UnwrapAsync(departmentId, artifact.WrappedDataKey);
			var codec = new ImportStagingCodec(_cipher);
			var context = await ConversionContextAsync(departmentId);
			var valueMaps = await _valueMaps.GetForDatasetAsync(departmentId, dataset.Id, cancellationToken);

			// ---- L2: the mapping covers every required field --------------------------------------------------------
			var bound = new HashSet<string>(map.Columns.Where(c => c.FieldKey != null && c.Kind != (int)ImportColumnBindingKind.Ignore).Select(c => c.FieldKey),
				StringComparer.OrdinalIgnoreCase);
			var unmapped = contract.Fields.Where(f => f.Required && !bound.Contains(f.Key) && !map.Constants.ContainsKey(f.Key)).ToList();
			foreach (var field in unmapped)
				issues.Add(ImportIssueFactory.Create(dataset, null, null, field.Key, "IMP-MAP-REQUIRED-UNMAPPED", ImportLadderLevel.L2Shape, ImportIssueSeverity.Blocking));
			foreach (var duplicate in map.Columns.Where(c => c.FieldKey != null && c.Kind != (int)ImportColumnBindingKind.Ignore)
						 .GroupBy(c => c.FieldKey, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
				issues.Add(ImportIssueFactory.Create(dataset, null, null, duplicate.Key, "IMP-MAP-FIELD-TWICE", ImportLadderLevel.L2Shape, ImportIssueSeverity.Blocking));
			if (unmapped.Count > 0)
				return await FinishAsync(dataset, issues, summary, ImportDatasetStatus.NeedsMapping, null, cancellationToken);

			// ---- Pass 1: L1, L3, L4 per row; collect uniqueness and reference inputs -----------------------------
			List<string> headers = null;
			int[] columnIndex = null;
			var exampleColumn = -1;
			var rowStates = new Dictionary<string, ImportRowValidationState>();
			var rowIssues = new Dictionary<string, List<ImportIssue>>();
			var uniques = contract.Fields.Where(f => f.Unique == "in-file" && f.Key != "source_id").ToDictionary(f => f.Key,
				f => new Dictionary<string, List<ImportStagedRow>>(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);
			var sourceKeys = new Dictionary<string, List<ImportStagedRow>>(StringComparer.OrdinalIgnoreCase);
			var canonicals = new Dictionary<string, ImportCanonicalPayload>();
			var stagedById = new Dictionary<string, ImportStagedRow>();

			void AddIssue(ImportStagedRow row, ImportIssue issue)
			{
				if (!rowIssues.TryGetValue(row.Id, out var list))
					rowIssues[row.Id] = list = new List<ImportIssue>();
				list.Add(issue);
			}

			var after = 0;
			while (true)
			{
				var page = await _rows.GetPageAsync(departmentId, dataset.Id, after, PageSize, cancellationToken);
				if (page.Count == 0)
					break;

				foreach (var row in page)
				{
					after = row.RowNumber;
					var cells = codec.Open<string[]>(key, ImportStagingCodec.Binding(dataset.Id, row.RowNumber, "source"), row.SourcePayload) ?? Array.Empty<string>();

					if (headers == null)
					{
						if (cells.All(string.IsNullOrWhiteSpace))
						{
							rowStates[row.Id] = ImportRowValidationState.Excluded;
							stagedById[row.Id] = row;
							continue;
						}

						headers = cells.Select(c => c?.Trim() ?? string.Empty).ToList();
						exampleColumn = headers.FindIndex(ImportColumnMatcher.IsExampleMarker);
						columnIndex = map.Columns.Select(c => headers.FindIndex(h => string.Equals(h, c.SourceColumn?.Trim(), StringComparison.Ordinal))).ToArray();
						foreach (var duplicate in headers.Where(h => h.Length > 0).GroupBy(h => h, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
							issues.Add(ImportIssueFactory.Create(dataset, null, duplicate.Key, null, "IMP-PARSE-HEADER-DUP", ImportLadderLevel.L1Parse, ImportIssueSeverity.Warning));
						rowStates[row.Id] = ImportRowValidationState.Excluded;
						stagedById[row.Id] = row;
						continue;
					}

					stagedById[row.Id] = row;
					var isExample = exampleColumn >= 0 && exampleColumn < cells.Length && string.Equals(cells[exampleColumn]?.Trim(), "x", StringComparison.OrdinalIgnoreCase);
					row.IsExample = isExample;
					if (isExample || cells.All(string.IsNullOrWhiteSpace) || row.Excluded)
					{
						rowStates[row.Id] = ImportRowValidationState.Excluded;
						if (row.Excluded)
							summary.Excluded++;
						continue;
					}

					summary.Rows++;

					// L1: values past the last header usually mean a stray delimiter shifted the row.
					for (var i = headers.Count; i < cells.Length; i++)
						if (!string.IsNullOrWhiteSpace(cells[i]))
						{
							AddIssue(row, ImportIssueFactory.Create(dataset, row, null, null, "IMP-PARSE-COLUMNS", ImportLadderLevel.L1Parse, ImportIssueSeverity.Blocking,
								new Dictionary<string, string> { ["expected"] = headers.Count.ToString(CultureInfo.InvariantCulture), ["found"] = cells.Length.ToString(CultureInfo.InvariantCulture) }));
							break;
						}

					var canonical = new ImportCanonicalPayload();
					for (var b = 0; b < map.Columns.Count; b++)
					{
						var binding = map.Columns[b];
						if (binding.Kind == (int)ImportColumnBindingKind.Ignore || columnIndex[b] < 0)
							continue;

						var raw = columnIndex[b] < cells.Length ? cells[columnIndex[b]] : null;
						if (ImportFieldConverter.HasControlCharacters(raw))
						{
							AddIssue(row, ImportIssueFactory.Create(dataset, row, binding.SourceColumn, binding.FieldKey, "IMP-FILE-CONTROL-CHAR", ImportLadderLevel.L1Parse, ImportIssueSeverity.Blocking));
							continue;
						}

						raw = ImportTransforms.Apply(binding.Transforms, raw);

						if (binding.Kind == (int)ImportColumnBindingKind.KeepAsCustomField || binding.Kind == (int)ImportColumnBindingKind.RetainAsSourceLabel)
						{
							var cleaned = ImportFieldConverter.Clean(raw);
							if (cleaned != null)
								canonical.CustomFields[string.IsNullOrWhiteSpace(binding.CustomFieldLabel) ? binding.SourceColumn : binding.CustomFieldLabel] = cleaned;
							continue;
						}

						var field = contract.Field(binding.FieldKey);
						if (field == null)
							continue;

						var columnContext = ContextFor(context, binding, field, valueMaps, map);
						var converted = ImportFieldConverter.Convert(field, raw, columnContext);
						if (converted.Invalid)
						{
							var severity = ImportIssueFactory.SeverityFor(converted.IssueCode, field.Required);
							AddIssue(row, ImportIssueFactory.Create(dataset, row, binding.SourceColumn, field.Key, converted.IssueCode, ImportLadderLevel.L3Type, severity, converted.Parameters));
							// Text that is only too long is kept, shortened, when the administrator accepts the warning.
							if (converted.IssueCode == "IMP-TYPE-TOO-LONG" && field.MaxLength.HasValue)
								canonical.Values[field.Key] = ImportFieldConverter.Clean(raw).Substring(0, field.MaxLength.Value);
							continue;
						}

						if (converted.Value != null)
							canonical.Values[field.Key] = converted.Value;
					}

					// Constants fill fields that have no column.
					foreach (var constant in map.Constants.Where(c => !canonical.Values.ContainsKey(c.Key)))
					{
						var field = contract.Field(constant.Key);
						if (field == null)
							continue;
						var converted = ImportFieldConverter.Convert(field, constant.Value, context);
						if (!converted.Invalid && converted.Value != null)
							canonical.Values[field.Key] = converted.Value;
					}

					// L4: required fields and cross-field rules.
					foreach (var field in contract.Fields.Where(f => f.Required && !canonical.Values.ContainsKey(f.Key)))
						if (!(rowIssues.TryGetValue(row.Id, out var existing) && existing.Any(i => i.FieldKey == field.Key)))
							AddIssue(row, ImportIssueFactory.Create(dataset, row, map.Columns.FirstOrDefault(c => c.FieldKey == field.Key)?.SourceColumn, field.Key,
								"IMP-REQUIRED-MISSING", ImportLadderLevel.L4RowRules, ImportIssueSeverity.Blocking));

					foreach (var rule in contract.RowRules)
						if (!RulePasses(rule, canonical.Values))
							AddIssue(row, ImportIssueFactory.Create(dataset, row, null, rule.Field ?? rule.Fields.FirstOrDefault(), rule.Code, ImportLadderLevel.L4RowRules, ImportIssueSeverity.Blocking));

					// The row's source identity: its source id, or the documented deterministic key (section 8.5).
					row.SourceKey = SourceKeyFor(contract, canonical.Values);
					if (row.SourceKey != null)
					{
						if (!sourceKeys.TryGetValue(row.SourceKey, out var withKey))
							sourceKeys[row.SourceKey] = withKey = new List<ImportStagedRow>();
						withKey.Add(row);
					}

					foreach (var unique in uniques)
						if (canonical.Values.TryGetValue(unique.Key, out var value) && value is string s)
						{
							var normalized = ImportFieldConverter.Normalize(s);
							if (!unique.Value.TryGetValue(normalized, out var list))
								unique.Value[normalized] = list = new List<ImportStagedRow>();
							list.Add(row);
						}

					canonicals[row.Id] = canonical;
				}

				if (heartbeat != null && !await heartbeat(cancellationToken))
					throw new OperationCanceledException("Import run lease lost.");
			}

			// ---- L5: uniqueness within the file -------------------------------------------------------------------
			foreach (var duplicate in sourceKeys.Where(k => k.Value.Count > 1))
				foreach (var row in duplicate.Value)
					AddIssue(row, ImportIssueFactory.Create(dataset, row, map.SourceIdentityColumn, "source_id",
						contract.IdentityFields.Count > 0 && canonicals[row.Id].Values.ContainsKey("source_id") ? "IMP-DUP-SOURCE-ID" : "IMP-DUP-IN-FILE",
						ImportLadderLevel.L5FileRules, ImportIssueSeverity.Blocking,
						new Dictionary<string, string> { ["rows"] = string.Join(", ", duplicate.Value.Select(r => r.RowNumber).Take(10)) }));

			foreach (var unique in uniques)
				foreach (var duplicate in unique.Value.Where(v => v.Value.Count > 1))
					foreach (var row in duplicate.Value)
						if (!(rowIssues.TryGetValue(row.Id, out var existing) && existing.Any(i => i.Code == "IMP-DUP-IN-FILE" || i.Code == "IMP-DUP-SOURCE-ID")))
							AddIssue(row, ImportIssueFactory.Create(dataset, row, map.Columns.FirstOrDefault(c => c.FieldKey == unique.Key)?.SourceColumn, unique.Key,
								"IMP-DUP-IN-FILE", ImportLadderLevel.L5FileRules, ImportIssueSeverity.Blocking,
								new Dictionary<string, string> { ["rows"] = string.Join(", ", duplicate.Value.Select(r => r.RowNumber).Take(10)) }));

			// ---- L6: references -----------------------------------------------------------------------------------
			var referenceFields = contract.Fields.Where(f => f.Type == ImportFieldTypes.Reference || (f.Type == ImportFieldTypes.Multi && f.ItemType == ImportFieldTypes.Reference)).ToList();
			if (referenceFields.Count > 0)
			{
				var indexes = new Dictionary<string, ReferenceIndex>(StringComparer.OrdinalIgnoreCase);
				foreach (var entity in referenceFields.Select(f => f.References).Distinct(StringComparer.OrdinalIgnoreCase))
					indexes[entity] = string.Equals(entity, contract.Key, StringComparison.OrdinalIgnoreCase)
						? ReferenceIndex.FromRows(canonicals.Select(c => (stagedById[c.Key].SourceKey, c.Value.Values)), catalog.GetContract(entity))
						: await BuildReferenceIndexAsync(departmentId, dataset.WorkspaceId, catalog.GetContract(entity), cancellationToken);

				// A continuation (section 5.2): a Source ID the earlier import wrote points at the record it created or linked.
				var sourceKey = await ImportSourceKeys.ForDatasetAsync(_sources, dataset, cancellationToken);
				if (!string.Equals(sourceKey, ImportSourceKeys.Workspace(dataset.WorkspaceId), StringComparison.Ordinal))
					foreach (var group in referenceFields.GroupBy(f => f.References, StringComparer.OrdinalIgnoreCase))
					{
						var raws = canonicals.Values.SelectMany(c => group.SelectMany(f => ReferenceRaws(c, f.Key))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
						indexes[group.Key].Continued = (await _externalIds.GetForSourceIdsAsync(departmentId, sourceKey, group.Key, raws, cancellationToken))
							.Where(e => e.State == (int)ImportExternalIdState.Written || e.State == (int)ImportExternalIdState.Linked)
							.GroupBy(e => e.SourceId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().TargetKey, StringComparer.OrdinalIgnoreCase);
					}

				foreach (var (rowId, canonical) in canonicals)
				{
					var row = stagedById[rowId];
					var dependencies = new List<string>();
					foreach (var field in referenceFields)
					{
						if (!canonical.Values.TryGetValue(field.Key, out var value) || value == null)
							continue;

						var raws = ReferenceRaws(canonical, field.Key);
						var tokens = new List<string>();
						foreach (var raw in raws.Where(r => !string.IsNullOrWhiteSpace(r)))
						{
							var token = Resolve(indexes[field.References], field, raw, valueMaps, out var ambiguous);
							if (ambiguous)
							{
								AddIssue(row, ImportIssueFactory.Create(dataset, row, map.Columns.FirstOrDefault(c => c.FieldKey == field.Key)?.SourceColumn, field.Key,
									"IMP-REF-AMBIGUOUS", ImportLadderLevel.L6References, ImportIssueSeverity.Blocking));
								continue;
							}

							if (token == null)
							{
								AddIssue(row, ImportIssueFactory.Create(dataset, row, map.Columns.FirstOrDefault(c => c.FieldKey == field.Key)?.SourceColumn, field.Key,
									"IMP-REF-UNRESOLVED", ImportLadderLevel.L6References, ImportIssueSeverity.Blocking,
									new Dictionary<string, string> { ["entity"] = field.References }));
								continue;
							}

							if (token.Length == 0)
								continue;

							tokens.Add(token);
							if (ImportReferenceToken.Is(token, ImportReferenceToken.Staged))
								dependencies.Add(field.References + "|" + ImportReferenceToken.Value(token, ImportReferenceToken.Staged));
						}

						canonical.References[field.Key] = tokens;
					}

					row.DependencyKeys = dependencies.Count == 0 ? null : JsonConvert.SerializeObject(dependencies.Distinct().ToList());
				}

				// L5 (after L6): a self-referencing hierarchy (a group's parent) must not loop.
				foreach (var field in referenceFields.Where(f => string.Equals(f.References, contract.Key, StringComparison.OrdinalIgnoreCase)))
					foreach (var rowId in FindCycles(canonicals, stagedById, field.Key))
						AddIssue(stagedById[rowId], ImportIssueFactory.Create(dataset, stagedById[rowId], null, field.Key, "IMP-HIERARCHY-CYCLE",
							ImportLadderLevel.L5FileRules, ImportIssueSeverity.Blocking));
			}

			// ---- Persist issues (keeping decisions), row states and payloads ------------------------------------
			var existingIssues = (await _issues.GetForDatasetAsync(departmentId, dataset.Id, cancellationToken)).ToDictionary(i => i.IdentityKey, StringComparer.Ordinal);
			var decided = new HashSet<string>(existingIssues.Values.Where(i => i.Status != (int)ImportIssueStatus.Open).Select(i => i.IdentityKey), StringComparer.Ordinal);
			var excludedByDecision = new HashSet<string>(existingIssues.Values.Where(i => i.Status == (int)ImportIssueStatus.Excluded && i.StagedRowId != null)
				.Select(i => i.StagedRowId), StringComparer.Ordinal);

			foreach (var (rowId, row) in stagedById)
			{
				if (!canonicals.TryGetValue(rowId, out var canonical))
				{
					row.ValidationState = (int)(rowStates.TryGetValue(rowId, out var state) ? state : ImportRowValidationState.Excluded);
					row.CanonicalPayload = null;
					row.ContentHash = null;
				}
				else
				{
					var open = rowIssues.TryGetValue(rowId, out var list) ? list.Where(i => !decided.Contains(i.IdentityKey)).ToList() : new List<ImportIssue>();
					var excluded = row.Excluded || excludedByDecision.Contains(rowId);
					row.ValidationState = (int)(excluded ? ImportRowValidationState.Excluded
						: open.Any(i => i.Severity == (int)ImportIssueSeverity.Blocking) ? ImportRowValidationState.Blocked
						: open.Any(i => i.Severity == (int)ImportIssueSeverity.Warning) ? ImportRowValidationState.Warning
						: ImportRowValidationState.Valid);

					if (excluded) summary.Excluded++;
					else if (row.ValidationState == (int)ImportRowValidationState.Blocked) summary.Blocked++;
					else
					{
						summary.Ready++;
						if (row.ValidationState == (int)ImportRowValidationState.Warning)
							summary.Warnings++;
					}

					row.CanonicalPayload = codec.Seal(key, ImportStagingCodec.Binding(dataset.Id, row.RowNumber, "canonical"), canonical);
					row.ContentHash = ImportStagingCodec.Hash(key, Flatten(canonical));
				}

				row.UpdatedOn = DateTime.UtcNow;
				await _rows.UpdateStateAsync(row, cancellationToken);
			}

			issues.AddRange(rowIssues.Values.SelectMany(i => i));
			var status = summary.Blocked > 0 || issues.Any(i => i.StagedRowId == null && i.Severity == (int)ImportIssueSeverity.Blocking && !decided.Contains(i.IdentityKey))
				? ImportDatasetStatus.NeedsAttention
				: ImportDatasetStatus.Ready;

			return await FinishAsync(dataset, issues, summary, status, null, cancellationToken, decided);
		}

		public async Task<List<ImportCanonicalRow>> ReadCanonicalRowsAsync(ImportDataset dataset, int afterRowNumber, int take, CancellationToken cancellationToken = default)
		{
			var artifact = await _artifacts.GetByIdAsync(dataset.DepartmentId, dataset.ArtifactId, cancellationToken)
				?? throw new ImportArtifactIntegrityException("IMP-ARTIFACT-MISSING");
			var key = await _keys.UnwrapAsync(dataset.DepartmentId, artifact.WrappedDataKey);
			var codec = new ImportStagingCodec(_cipher);
			var page = await _rows.GetPageAsync(dataset.DepartmentId, dataset.Id, afterRowNumber, take, cancellationToken);

			return page.Where(r => r.CanonicalPayload != null).Select(r =>
			{
				var payload = codec.Open<ImportCanonicalPayload>(key, ImportStagingCodec.Binding(dataset.Id, r.RowNumber, "canonical"), r.CanonicalPayload)
					?? new ImportCanonicalPayload();
				var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
				foreach (var (field, value) in payload.Values)
					values[field] = Normalize(value);
				foreach (var (field, tokens) in payload.References)
					values[field] = tokens.Count == 1 && !(payload.Values.TryGetValue(field, out var original) && original is Newtonsoft.Json.Linq.JArray) ? tokens[0] : (object)tokens;

				return new ImportCanonicalRow
				{
					StagedRowId = r.Id,
					DatasetId = r.DatasetId,
					RowNumber = r.RowNumber,
					SourceKey = r.SourceKey,
					Values = values,
					CustomFields = payload.CustomFields,
					Decision = (ImportRowDecision)r.Decision,
					TargetKey = r.TargetKey,
					TargetVersion = r.TargetVersionAtPreview,
					ValidationState = r.Excluded ? ImportRowValidationState.Excluded : (ImportRowValidationState)r.ValidationState,
					Outcome = (ImportRowOutcome)r.Outcome
				};
			}).ToList();
		}

		// ---- helpers ------------------------------------------------------------------------------------------------

		private static object Normalize(object value) => value switch
		{
			Newtonsoft.Json.Linq.JArray array => array.Select(t => t.ToString()).ToList(),
			Newtonsoft.Json.Linq.JValue scalar => scalar.Value?.ToString(),
			_ => value
		};

		private static Dictionary<string, object> Flatten(ImportCanonicalPayload payload)
		{
			var flat = new Dictionary<string, object>(StringComparer.Ordinal);
			foreach (var (k, v) in payload.Values) flat["v:" + k] = Normalize(v);
			foreach (var (k, v) in payload.References) flat["r:" + k] = v;
			foreach (var (k, v) in payload.CustomFields) flat["c:" + k] = v;
			return flat;
		}

		private async Task<ImportValidationSummary> FinishAsync(ImportDataset dataset, List<ImportIssue> issues, ImportValidationSummary summary,
			ImportDatasetStatus status, string failureCode, CancellationToken cancellationToken, HashSet<string> decided = null)
		{
			// Replace this dataset's open ladder issues (L1–L6); decisions on earlier issues survive by identity.
			await _issues.DeleteOpenSystemIssuesAsync(dataset.DepartmentId, dataset.Id, (int)ImportLadderLevel.L1Parse, cancellationToken);
			if (decided == null)
				decided = new HashSet<string>((await _issues.GetForDatasetAsync(dataset.DepartmentId, dataset.Id, cancellationToken))
					.Select(i => i.IdentityKey), StringComparer.Ordinal);
			var fresh = issues.Where(i => !decided.Contains(i.IdentityKey)).GroupBy(i => i.IdentityKey).Select(g => g.First()).ToList();
			await _issues.InsertManyAsync(fresh, cancellationToken);

			var previousDigest = dataset.StagingDigest;
			dataset.RowCount = summary.Rows;
			dataset.ReadyCount = summary.Ready;
			dataset.WarningCount = summary.Warnings;
			dataset.BlockedCount = summary.Blocked;
			dataset.ExcludedCount = summary.Excluded;
			dataset.FailureCode = failureCode;
			dataset.AnalyzedOn = DateTime.UtcNow;
			dataset.UpdatedOn = DateTime.UtcNow;
			dataset.StagingDigest = await DigestAsync(dataset, cancellationToken);

			var current = (ImportDatasetStatus)dataset.Status;
			var locked = current == ImportDatasetStatus.Importing || current == ImportDatasetStatus.Imported || current == ImportDatasetStatus.Verified ||
				current == ImportDatasetStatus.Excluded || current == ImportDatasetStatus.Superseded;
			if (!locked)
			{
				// An approved dataset whose staged state changed must be approved again (section 10.2).
				if (current == ImportDatasetStatus.Approved && previousDigest == dataset.StagingDigest && status == ImportDatasetStatus.Ready)
					status = ImportDatasetStatus.Approved;
				else if (current == ImportDatasetStatus.Approved)
					DepartmentImportStateMachine.InvalidateApproval(dataset);
				dataset.Status = (int)status;
			}

			summary.Status = (ImportDatasetStatus)dataset.Status;
			if (!await _datasets.UpdateWithRevisionAsync(dataset, dataset.Revision, cancellationToken))
			{
				// A concurrent change (mapping saved mid-run) bumped the revision; its own revalidation will refresh the counts.
				Logging.LogInfo($"Import dataset {dataset.Id} changed during validation; counts left to the next run.");
			}

			return summary;
		}

		private async Task<string> DigestAsync(ImportDataset dataset, CancellationToken cancellationToken)
		{
			var rows = await _rows.GetDigestInputsAsync(dataset.DepartmentId, dataset.Id, cancellationToken);
			var builder = new StringBuilder();
			foreach (var row in rows)
				builder.Append(row.RowNumber).Append('|').Append(row.ContentHash).Append('|').Append(row.ValidationState).Append('|').Append(row.Excluded ? 1 : 0).Append('\n');
			builder.Append(dataset.EntityKey).Append('|').Append(dataset.ColumnMapId);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
		}

		private async Task<ImportColumnMapBody> LoadMapAsync(ImportDataset dataset, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(dataset.ColumnMapId))
				return null;
			var map = await _columnMaps.GetByIdAsync(dataset.DepartmentId, dataset.ColumnMapId, cancellationToken);
			return map == null ? null : JsonConvert.DeserializeObject<ImportColumnMapBody>(map.Body);
		}

		private async Task<ImportConversionContext> ConversionContextAsync(int departmentId)
		{
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			string country = null;
			if (department?.AddressId != null)
			{
				try
				{
					country = (await _addresses.GetAddressByIdAsync(department.AddressId.Value))?.Country;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			return new ImportConversionContext
			{
				Phones = _phones,
				PhoneRegion = PhoneRegionHelper.ToIso(country) ?? "us",
				TimeZoneId = department?.TimeZone
			};
		}

		private ImportConversionContext ContextFor(ImportConversionContext baseline, ImportColumnBinding binding, ImportFieldContract field,
			List<ImportValueMap> valueMaps, ImportColumnMapBody map)
		{
			var context = new ImportConversionContext
			{
				Phones = baseline.Phones,
				PhoneRegion = baseline.PhoneRegion,
				TimeZoneId = baseline.TimeZoneId,
				DateFormat = map.Formats != null && binding.SourceColumn != null && map.Formats.TryGetValue(binding.SourceColumn, out var format) ? format : null
			};

			if (field.Type == ImportFieldTypes.Choice || field.ItemType == ImportFieldTypes.Choice)
				context.ChoiceMap = valueMaps.Where(v => string.Equals(v.FieldKey, field.Key, StringComparison.OrdinalIgnoreCase))
					.GroupBy(v => ImportFieldConverter.Normalize(v.SourceValue))
					.ToDictionary(g => g.Key, g => g.First().Action == (int)ImportValueMapAction.LeaveEmpty ? string.Empty : g.First().TargetCode);

			return context;
		}

		private string Resolve(ReferenceIndex index, ImportFieldContract field, string raw, List<ImportValueMap> valueMaps, out bool ambiguous)
		{
			ambiguous = false;
			var hash = ValueHash(raw);
			var decision = valueMaps.FirstOrDefault(v => string.Equals(v.FieldKey, field.Key, StringComparison.OrdinalIgnoreCase) && v.SourceValueHash == hash);
			if (decision != null)
			{
				switch ((ImportValueMapAction)decision.Action)
				{
					case ImportValueMapAction.MapToExisting:
						return string.IsNullOrWhiteSpace(decision.TargetCode) ? null : ImportReferenceToken.ForExisting(decision.TargetCode);
					case ImportValueMapAction.CreateNew:
						return ImportReferenceToken.ForNew(raw.Trim());
					case ImportValueMapAction.KeepAsSourceLabel:
						return ImportReferenceToken.ForLabel(raw.Trim());
					case ImportValueMapAction.LeaveEmpty:
						return string.Empty;
				}
			}

			return index.Resolve(raw, out ambiguous);
		}

		/// <summary>A reference field's raw values in a row (one, or each item of a multi-value).</summary>
		private static List<string> ReferenceRaws(ImportCanonicalPayload canonical, string fieldKey)
		{
			if (!canonical.Values.TryGetValue(fieldKey, out var value) || value == null)
				return new List<string>();
			return value is string single
				? new List<string> { single.Trim() }
				: ((IEnumerable<object>)value).Select(v => (v as string ?? v?.ToString())?.Trim()).Where(v => !string.IsNullOrEmpty(v)).ToList();
		}

		private async Task<ReferenceIndex> BuildReferenceIndexAsync(int departmentId, string workspaceId, ImportEntityContract target, CancellationToken cancellationToken)
		{
			var rows = new List<(string SourceKey, Dictionary<string, object> Values)>();
			if (target != null)
			{
				var datasets = (await _datasets.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
					.Where(d => DepartmentImportStateMachine.IsLive(d) && string.Equals(d.EntityKey, target.Key, StringComparison.OrdinalIgnoreCase)).ToList();

				foreach (var dataset in datasets)
				{
					var after = 0;
					while (true)
					{
						var page = await ReadCanonicalRowsAsync(dataset, after, 1000, cancellationToken);
						if (page.Count == 0)
							break;
						after = page[page.Count - 1].RowNumber;
						rows.AddRange(page.Where(r => r.SourceKey != null).Select(r => (r.SourceKey, r.Values)));
					}
				}
			}

			var index = ReferenceIndex.FromRows(rows, target);
			var writer = target == null ? null : _writers.FirstOrDefault(w => string.Equals(w.EntityKey, target.Key, StringComparison.OrdinalIgnoreCase));
			if (writer != null)
				index.Existing = await writer.FindExistingAsync(departmentId, cancellationToken);

			return index;
		}

		public static string SourceKeyFor(ImportEntityContract contract, IReadOnlyDictionary<string, object> values)
		{
			if (contract.IdentityFields.Count > 0 && contract.IdentityFields.All(f => values.TryGetValue(f, out var v) && v is string s && s.Length > 0))
				return string.Join("|", contract.IdentityFields.Select(f => (string)values[f]));

			if (contract.NaturalKey.Count == 0)
				return null;

			var parts = contract.NaturalKey.Select(f => values.TryGetValue(f, out var v) ? (v is string s ? s : JsonConvert.SerializeObject(v)) : string.Empty).ToList();
			if (parts.All(string.IsNullOrEmpty))
				return null;

			// The key is stored in clear (staged rows, the external-id map) and outlives the encrypted staging: a natural key
			// made of personal, restricted or protected values (an email, a certificate number) is kept only as its hash.
			var natural = string.Join("|", parts.Select(ImportFieldConverter.Normalize));
			return contract.NaturalKey.Any(k => contract.Field(k) is { } field && (field.Protected != null || field.SensitivityLevel != ImportFieldSensitivity.Internal))
				? "nkh:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(natural))).ToLowerInvariant()
				: "nk:" + natural;
		}

		private static bool RulePasses(ImportRowRule rule, IReadOnlyDictionary<string, object> values)
		{
			bool Has(string f) => f != null && values.TryGetValue(f, out var v) && v != null;
			switch (rule.Kind)
			{
				case "after-or-equal":
					return !Has(rule.Field) || !Has(rule.Other) || string.CompareOrdinal(values[rule.Field] as string, values[rule.Other] as string) >= 0;
				case "both-or-neither":
					var present = rule.Fields.Count(Has);
					return present == 0 || present == rule.Fields.Count;
				case "one-of-required":
					return rule.Fields.Any(Has);
				default:
					return true;
			}
		}

		private static IEnumerable<string> FindCycles(Dictionary<string, ImportCanonicalPayload> canonicals, Dictionary<string, ImportStagedRow> rows, string field)
		{
			var parentOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var rowBySource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var (rowId, canonical) in canonicals)
			{
				var sourceKey = rows[rowId].SourceKey;
				if (sourceKey == null)
					continue;
				rowBySource[sourceKey] = rowId;
				if (canonical.References.TryGetValue(field, out var tokens) && tokens.Count > 0 && ImportReferenceToken.Is(tokens[0], ImportReferenceToken.Staged))
					parentOf[sourceKey] = ImportReferenceToken.Value(tokens[0], ImportReferenceToken.Staged);
			}

			var inCycle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var start in parentOf.Keys)
			{
				var seen = new List<string>();
				var current = start;
				while (current != null && parentOf.TryGetValue(current, out var parent))
				{
					if (seen.Contains(current, StringComparer.OrdinalIgnoreCase))
					{
						foreach (var member in seen.SkipWhile(s => !string.Equals(s, current, StringComparison.OrdinalIgnoreCase)))
							inCycle.Add(member);
						break;
					}
					seen.Add(current);
					current = parent;
				}
			}

			return inCycle.Where(rowBySource.ContainsKey).Select(k => rowBySource[k]);
		}

		/// <summary>Where a reference value can point: staged rows (by source key or natural key) and existing records.</summary>
		private sealed class ReferenceIndex
		{
			private readonly Dictionary<string, string> _bySourceKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			private readonly Dictionary<string, HashSet<string>> _byNatural = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

			public IReadOnlyDictionary<string, string> Existing { get; set; } = new Dictionary<string, string>();
			/// <summary>Source ID → target key of records an earlier import (this workspace continues) wrote or linked.</summary>
			public IReadOnlyDictionary<string, string> Continued { get; set; } = new Dictionary<string, string>();

			public static ReferenceIndex FromRows(IEnumerable<(string SourceKey, Dictionary<string, object> Values)> rows, ImportEntityContract contract)
			{
				var index = new ReferenceIndex();
				foreach (var (sourceKey, values) in rows)
				{
					if (string.IsNullOrWhiteSpace(sourceKey))
						continue;
					index._bySourceKey[sourceKey] = sourceKey;
					foreach (var natural in NaturalValues(contract, values))
					{
						if (!index._byNatural.TryGetValue(natural, out var keys))
							index._byNatural[natural] = keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
						keys.Add(sourceKey);
					}
				}
				return index;
			}

			private static IEnumerable<string> NaturalValues(ImportEntityContract contract, Dictionary<string, object> values)
			{
				if (contract == null)
					yield break;
				// Single-field natural keys (name, email) and codes are what other files use to point at a row.
				foreach (var field in contract.NaturalKey.Take(1).Concat(new[] { "code", "email", "name" }).Distinct())
					if (values.TryGetValue(field, out var v) && v is string s && s.Length > 0)
						yield return ImportFieldConverter.Normalize(s);
			}

			public string Resolve(string raw, out bool ambiguous)
			{
				ambiguous = false;
				var trimmed = raw.Trim();
				if (_bySourceKey.ContainsKey(trimmed))
					return ImportReferenceToken.ForStaged(_bySourceKey[trimmed]);

				var normalized = ImportFieldConverter.Normalize(trimmed);
				if (_byNatural.TryGetValue(normalized, out var keys))
				{
					if (keys.Count > 1)
					{
						ambiguous = true;
						return null;
					}
					return ImportReferenceToken.ForStaged(keys.First());
				}

				if (Continued != null && Continued.TryGetValue(trimmed, out var continued))
					return ImportReferenceToken.ForExisting(continued);

				return Existing != null && Existing.TryGetValue(normalized, out var target) ? ImportReferenceToken.ForExisting(target) : null;
			}
		}
	}
}

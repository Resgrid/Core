using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Column and value mapping (wizard plan sections 6.3 steps 6–7 and 8.4). Every source ends as an ImportColumnMap;
	/// saving a map writes a new version, so a run's approval digest always names the exact mapping it saw. Bindings are
	/// checked against the contract allowlist: no arbitrary column, no expression, only catalog transforms.
	/// </summary>
	public class ImportMappingService : IImportMappingService
	{
		private readonly IImportWorkspacesRepository _workspaces;
		private readonly IImportDatasetsRepository _datasets;
		private readonly IImportColumnMapsRepository _columnMaps;
		private readonly IImportValueMapsRepository _valueMaps;
		private readonly IImportArtifactsRepository _artifacts;
		private readonly IImportStagedRowsRepository _rows;
		private readonly IImportCatalog _catalog;
		private readonly IImportKeyService _keys;
		private readonly IImportArtifactCipher _cipher;
		private readonly IImportValidationService _validation;
		private readonly IImportRunQueue _runQueue;
		private readonly IImportTimelineService _timeline;
		private readonly IEnumerable<IImportTargetWriter> _writers;

		public ImportMappingService(IImportWorkspacesRepository workspaces, IImportDatasetsRepository datasets, IImportColumnMapsRepository columnMaps,
			IImportValueMapsRepository valueMaps, IImportArtifactsRepository artifacts, IImportStagedRowsRepository rows, IImportCatalog catalog, IImportKeyService keys,
			IImportArtifactCipher cipher, IImportValidationService validation, IImportRunQueue runQueue, IImportTimelineService timeline,
			IEnumerable<IImportTargetWriter> writers)
		{
			_workspaces = workspaces;
			_datasets = datasets;
			_columnMaps = columnMaps;
			_valueMaps = valueMaps;
			_artifacts = artifacts;
			_rows = rows;
			_catalog = catalog;
			_keys = keys;
			_cipher = cipher;
			_validation = validation;
			_runQueue = runQueue;
			_timeline = timeline;
			_writers = writers;
		}

		public async Task<ImportMappingView> GetMappingAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default)
		{
			var dataset = await _datasets.GetByIdAsync(departmentId, datasetId, cancellationToken) ?? throw new DepartmentImportException("import_dataset_not_found", 404);
			var catalog = await CatalogForAsync(dataset, cancellationToken);
			var map = string.IsNullOrWhiteSpace(dataset.ColumnMapId) ? null : await _columnMaps.GetByIdAsync(departmentId, dataset.ColumnMapId, cancellationToken);

			return new ImportMappingView
			{
				Dataset = dataset,
				Contract = catalog.GetContract(dataset.EntityKey),
				Map = map,
				Body = map == null ? new ImportColumnMapBody() : JsonConvert.DeserializeObject<ImportColumnMapBody>(map.Body),
				Profile = string.IsNullOrWhiteSpace(dataset.Profile) ? new ImportDatasetProfile() : JsonConvert.DeserializeObject<ImportDatasetProfile>(dataset.Profile),
				Destinations = catalog.InDependencyOrder().ToList(),
				Transforms = ImportTransforms.Catalog.Keys.ToList(),
				DateFormats = ImportFieldConverter.DateFormats
			};
		}

		public async Task<ImportColumnMap> SaveColumnMapAsync(int departmentId, string actor, ImportActorKind actorKind, string datasetId, ImportColumnMapBody body,
			int expectedRevision, CancellationToken cancellationToken = default)
		{
			var dataset = await RequireEditableAsync(departmentId, datasetId, cancellationToken);
			var catalog = await CatalogForAsync(dataset, cancellationToken);
			var contract = catalog.GetContract(dataset.EntityKey) ?? throw new DepartmentImportException("import_destination_required");
			var clean = Sanitize(body, contract);

			var previous = string.IsNullOrWhiteSpace(dataset.ColumnMapId) ? null : await _columnMaps.GetByIdAsync(departmentId, dataset.ColumnMapId, cancellationToken);
			var now = DateTime.UtcNow;
			var map = new ImportColumnMap
			{
				DepartmentId = departmentId,
				WorkspaceId = dataset.WorkspaceId,
				Name = previous?.Name ?? dataset.Name,
				EntityKey = contract.Key,
				ContractVersion = contract.Version,
				Version = (previous?.Version ?? 0) + 1,
				Body = JsonConvert.SerializeObject(clean),
				Origin = (int)(actorKind == ImportActorKind.ResgridStaff ? ImportColumnMapOrigin.StaffOperator
					: actorKind == ImportActorKind.SupportSuggestion ? ImportColumnMapOrigin.SupportSuggestion : ImportColumnMapOrigin.Administrator),
				HeaderSignature = previous?.HeaderSignature,
				StaffAuthored = actorKind == ImportActorKind.ResgridStaff,
				CreatedBy = actor,
				CreatedOn = now,
				UpdatedOn = now
			};
			await _columnMaps.InsertAsync(map, cancellationToken, true);

			dataset.ColumnMapId = map.Id;
			dataset.DestinationConfirmed = true;
			dataset.UpdatedOn = now;
			DepartmentImportStateMachine.InvalidateApproval(dataset);
			if (!await _datasets.UpdateWithRevisionAsync(dataset, expectedRevision, cancellationToken))
				throw new DepartmentImportException("import_changed_elsewhere", 409);

			await RevalidateAsync(departmentId, dataset.WorkspaceId, new[] { dataset.Id }, cancellationToken);
			await _timeline.RecordAsync(departmentId, dataset.WorkspaceId, actorKind, actor, "ColumnMapSaved",
				new { datasetId, version = map.Version, mapped = clean.Columns.Count(c => c.Kind != (int)ImportColumnBindingKind.Ignore) }, cancellationToken: cancellationToken);
			return map;
		}

		public async Task<ImportColumnMap> SaveMapAsAsync(int departmentId, string userId, string datasetId, string name, CancellationToken cancellationToken = default)
		{
			var dataset = await _datasets.GetByIdAsync(departmentId, datasetId, cancellationToken) ?? throw new DepartmentImportException("import_dataset_not_found", 404);
			var source = string.IsNullOrWhiteSpace(dataset.ColumnMapId) ? null : await _columnMaps.GetByIdAsync(departmentId, dataset.ColumnMapId, cancellationToken);
			if (source == null)
				throw new DepartmentImportException("import_map_missing");

			var existing = (await _columnMaps.GetSavedForDepartmentAsync(departmentId, source.EntityKey, cancellationToken))
				.Where(m => string.Equals(m.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)).OrderByDescending(m => m.Version).FirstOrDefault();
			var now = DateTime.UtcNow;
			var saved = new ImportColumnMap
			{
				DepartmentId = departmentId,
				WorkspaceId = null,
				Name = Clip(string.IsNullOrWhiteSpace(name) ? source.Name : name.Trim(), 200),
				EntityKey = source.EntityKey,
				ContractVersion = source.ContractVersion,
				Version = (existing?.Version ?? 0) + 1,
				Body = source.Body,
				Origin = source.Origin,
				IsSaved = true,
				HeaderSignature = source.HeaderSignature,
				CreatedBy = userId,
				CreatedOn = now,
				UpdatedOn = now
			};
			await _columnMaps.InsertAsync(saved, cancellationToken, true);
			await _timeline.RecordAsync(departmentId, dataset.WorkspaceId, ImportActorKind.Administrator, userId, "MapSaved", new { mapId = saved.Id, saved.Version },
				cancellationToken: cancellationToken);
			return saved;
		}

		public Task<List<ImportColumnMap>> GetSavedMapsAsync(int departmentId, string entityKey, CancellationToken cancellationToken = default) =>
			_columnMaps.GetSavedForDepartmentAsync(departmentId, entityKey, cancellationToken);

		public async Task<ImportColumnMap> ApplySavedMapAsync(int departmentId, string actor, ImportActorKind actorKind, string datasetId, string mapId,
			CancellationToken cancellationToken = default)
		{
			var saved = await _columnMaps.GetByIdAsync(departmentId, mapId, cancellationToken);
			if (saved == null || !saved.IsSaved)
				throw new DepartmentImportException("import_map_missing", 404);

			var dataset = await RequireEditableAsync(departmentId, datasetId, cancellationToken);
			if (!string.Equals(dataset.EntityKey, saved.EntityKey, StringComparison.OrdinalIgnoreCase))
			{
				var catalog = await CatalogForAsync(dataset, cancellationToken);
				var contract = catalog.GetContract(saved.EntityKey) ?? throw new DepartmentImportException("import_unknown_destination");
				dataset.EntityKey = contract.Key;
				dataset.ContractVersion = contract.Version;
				dataset.Wave = contract.Order;
				if (!await _datasets.UpdateWithRevisionAsync(dataset, dataset.Revision, cancellationToken))
					throw new DepartmentImportException("import_changed_elsewhere", 409);
			}

			return await SaveColumnMapAsync(departmentId, actor, actorKind, datasetId, JsonConvert.DeserializeObject<ImportColumnMapBody>(saved.Body),
				dataset.Revision, cancellationToken);
		}

		public async Task<List<ImportValueCandidate>> GetValueCandidatesAsync(int departmentId, string datasetId, string fieldKey, CancellationToken cancellationToken = default)
		{
			var view = await GetMappingAsync(departmentId, datasetId, cancellationToken);
			var field = view.Contract?.Field(fieldKey) ?? throw new DepartmentImportException("import_unknown_field");
			var isChoice = field.Type == ImportFieldTypes.Choice || field.ItemType == ImportFieldTypes.Choice;
			var isReference = field.Type == ImportFieldTypes.Reference || field.ItemType == ImportFieldTypes.Reference;
			if (!isChoice && !isReference)
				throw new DepartmentImportException("import_field_not_mappable");

			var columns = view.Body.Columns.Where(c => string.Equals(c.FieldKey, field.Key, StringComparison.OrdinalIgnoreCase)).Select(c => c.SourceColumn).ToList();
			var counts = await DistinctValuesAsync(view.Dataset, columns, field, cancellationToken);
			var decisions = (await _valueMaps.GetForDatasetAsync(departmentId, datasetId, cancellationToken))
				.Where(v => string.Equals(v.FieldKey, field.Key, StringComparison.OrdinalIgnoreCase)).ToDictionary(v => v.SourceValueHash);

			IReadOnlyDictionary<string, string> existing = new Dictionary<string, string>();
			if (isReference)
			{
				var writer = _writers.FirstOrDefault(w => string.Equals(w.EntityKey, field.References, StringComparison.OrdinalIgnoreCase));
				if (writer != null)
					existing = await writer.FindExistingAsync(departmentId, cancellationToken);
			}

			return counts.OrderByDescending(c => c.Value).Take(500).Select(c =>
			{
				var candidate = new ImportValueCandidate { SourceValue = c.Key, RowCount = c.Value };
				var normalized = ImportFieldConverter.Normalize(c.Key);
				if (isChoice)
				{
					var match = field.Choices.FirstOrDefault(ch => ImportFieldConverter.Normalize(ch.Code) == normalized || ch.Aliases.Any(a => ImportFieldConverter.Normalize(a) == normalized))
						?? field.Choices.FirstOrDefault(ch => normalized.Length >= 3 && (ImportFieldConverter.Normalize(ch.Code).StartsWith(normalized) ||
							ch.Aliases.Any(a => ImportFieldConverter.Normalize(a).StartsWith(normalized))));
					if (match != null)
					{
						candidate.SuggestedCode = match.Code;
						candidate.SuggestedLabel = match.LabelKey;
						candidate.Confidence = ImportFieldConverter.Normalize(match.Code) == normalized || match.Aliases.Any(a => ImportFieldConverter.Normalize(a) == normalized) ? 100 : 60;
					}
				}
				else if (existing.TryGetValue(normalized, out var target))
				{
					candidate.SuggestedCode = target;
					candidate.SuggestedLabel = c.Key;
					candidate.Confidence = 100;
				}

				if (decisions.TryGetValue(_validation.ValueHash(c.Key), out var decision))
				{
					candidate.Decision = (ImportValueMapAction)decision.Action;
					candidate.DecidedCode = decision.TargetCode;
				}

				return candidate;
			}).ToList();
		}

		public async Task<int> SaveValueMapsAsync(int departmentId, string actor, ImportActorKind actorKind, string datasetId, string fieldKey,
			IReadOnlyList<ImportValueDecision> decisions, CancellationToken cancellationToken = default)
		{
			var dataset = await RequireEditableAsync(departmentId, datasetId, cancellationToken);
			var catalog = await CatalogForAsync(dataset, cancellationToken);
			var field = catalog.GetContract(dataset.EntityKey)?.Field(fieldKey) ?? throw new DepartmentImportException("import_unknown_field");
			var isChoice = field.Type == ImportFieldTypes.Choice || field.ItemType == ImportFieldTypes.Choice;

			var existing = (await _valueMaps.GetForDatasetAsync(departmentId, datasetId, cancellationToken))
				.Where(v => string.Equals(v.FieldKey, field.Key, StringComparison.OrdinalIgnoreCase)).ToDictionary(v => v.SourceValueHash);
			var now = DateTime.UtcNow;
			var saved = 0;

			foreach (var decision in decisions.Where(d => !string.IsNullOrWhiteSpace(d.SourceValue)))
			{
				if (!Enum.IsDefined(typeof(ImportValueMapAction), decision.Action))
					throw new DepartmentImportException("import_value_action_invalid");
				// Choice fields map only to their own codes; "create new" exists only for references.
				if (isChoice && decision.Action == ImportValueMapAction.MapToExisting &&
					!field.Choices.Any(c => string.Equals(c.Code, decision.TargetCode, StringComparison.OrdinalIgnoreCase)))
					throw new DepartmentImportException("import_value_code_invalid");
				if (isChoice && decision.Action == ImportValueMapAction.CreateNew)
					throw new DepartmentImportException("import_value_action_invalid");

				var hash = _validation.ValueHash(decision.SourceValue);
				if (existing.TryGetValue(hash, out var row))
				{
					row.Action = (int)decision.Action;
					row.TargetCode = Clip(decision.TargetCode, 256);
					row.Confidence = decision.Confidence;
					row.DecidedBy = actor;
					row.DecidedByKind = (int)actorKind;
					row.DecidedOn = now;
					await _valueMaps.UpdateAsync(row, cancellationToken, true);
				}
				else
				{
					await _valueMaps.InsertAsync(new ImportValueMap
					{
						DepartmentId = departmentId,
						WorkspaceId = dataset.WorkspaceId,
						DatasetId = datasetId,
						FieldKey = field.Key,
						SourceValueHash = hash,
						SourceValue = Clip(decision.SourceValue.Trim(), 1000),
						Action = (int)decision.Action,
						TargetCode = Clip(decision.TargetCode, 256),
						Confidence = decision.Confidence,
						DecidedBy = actor,
						DecidedByKind = (int)actorKind,
						DecidedOn = now
					}, cancellationToken, true);
				}
				saved++;
			}

			if (saved > 0)
			{
				DepartmentImportStateMachine.InvalidateApproval(dataset);
				dataset.UpdatedOn = now;
				await _datasets.UpdateWithRevisionAsync(dataset, dataset.Revision, cancellationToken);
				await RevalidateAsync(departmentId, dataset.WorkspaceId, new[] { datasetId }, cancellationToken);
				await _timeline.RecordAsync(departmentId, dataset.WorkspaceId, actorKind, actor, "ValueMapsSaved", new { datasetId, field = field.Key, count = saved },
					cancellationToken: cancellationToken);
			}

			return saved;
		}

		public Task<ImportRun> RevalidateAsync(int departmentId, string workspaceId, IReadOnlyCollection<string> datasetIds, CancellationToken cancellationToken = default) =>
			_runQueue.QueueAsync(new ImportRun
			{
				DepartmentId = departmentId,
				WorkspaceId = workspaceId,
				Kind = (int)ImportRunKind.Analysis,
				DatasetIds = JsonConvert.SerializeObject(datasetIds),
				RequestedBy = "system"
			}, cancellationToken);

		public async Task<ImportColumnMap> SetDateFormatAsync(int departmentId, string actor, ImportActorKind actorKind, string datasetId, string sourceColumn,
			string format, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(sourceColumn))
				throw new DepartmentImportException("import_unknown_column");
			if (!string.IsNullOrWhiteSpace(format) && !ImportFieldConverter.DateFormats.Contains(format))
				throw new DepartmentImportException("import_date_format_unknown");

			var view = await GetMappingAsync(departmentId, datasetId, cancellationToken);
			var body = view.Body ?? new ImportColumnMapBody();
			if (!body.Columns.Any(c => string.Equals(c.SourceColumn, sourceColumn, StringComparison.Ordinal)))
				throw new DepartmentImportException("import_unknown_column");

			body.Formats ??= new Dictionary<string, string>();
			if (string.IsNullOrWhiteSpace(format))
				body.Formats.Remove(sourceColumn);
			else
				body.Formats[sourceColumn] = format;
			return await SaveColumnMapAsync(departmentId, actor, actorKind, datasetId, body, view.Dataset.Revision, cancellationToken);
		}

		public async Task<ImportColumnMap> SetConstantAsync(int departmentId, string actor, ImportActorKind actorKind, string datasetId, string fieldKey, string value,
			CancellationToken cancellationToken = default)
		{
			var view = await GetMappingAsync(departmentId, datasetId, cancellationToken);
			if (view.Contract?.Field(fieldKey) == null)
				throw new DepartmentImportException("import_unknown_field");

			var body = view.Body ?? new ImportColumnMapBody();
			body.Constants ??= new Dictionary<string, string>();
			if (string.IsNullOrWhiteSpace(value))
				body.Constants.Remove(fieldKey);
			else
				body.Constants[fieldKey] = value.Trim();
			return await SaveColumnMapAsync(departmentId, actor, actorKind, datasetId, body, view.Dataset.Revision, cancellationToken);
		}

		/// <summary>Keeps only bindings the contract allows (section 8.3 allowlist) and catalog transforms.</summary>
		internal static ImportColumnMapBody Sanitize(ImportColumnMapBody body, ImportEntityContract contract)
		{
			body ??= new ImportColumnMapBody();
			var clean = new ImportColumnMapBody
			{
				HeaderRow = Math.Max(1, body.HeaderRow),
				Formats = (body.Formats ?? new Dictionary<string, string>()).Where(f => ImportFieldConverter.DateFormats.Contains(f.Value))
					.ToDictionary(f => f.Key, f => f.Value)
			};

			// Same value for every row: a value the field cannot hold is refused here rather than silently left out of every row.
			foreach (var (key, value) in body.Constants ?? new Dictionary<string, string>())
			{
				var field = contract.Field(key);
				if (field == null || string.IsNullOrWhiteSpace(value))
					continue;
				if (!field.AllowsConstant)
					throw new DepartmentImportException("import_constant_not_allowed");
				if (value.Length > 1000 || ImportFieldConverter.Convert(field, value, null).Invalid)
					throw new DepartmentImportException("import_constant_invalid");
				clean.Constants[field.Key] = value.Trim();
			}

			foreach (var binding in body.Columns ?? new List<ImportColumnBinding>())
			{
				var kind = (ImportColumnBindingKind)binding.Kind;
				if (!Enum.IsDefined(typeof(ImportColumnBindingKind), kind) || string.IsNullOrEmpty(binding.SourceColumn))
					continue;

				if (kind == ImportColumnBindingKind.Ignore)
				{
					clean.Columns.Add(new ImportColumnBinding { SourceColumn = binding.SourceColumn, Kind = binding.Kind });
					continue;
				}

				if (kind == ImportColumnBindingKind.KeepAsCustomField || kind == ImportColumnBindingKind.RetainAsSourceLabel)
				{
					if (kind == ImportColumnBindingKind.KeepAsCustomField && !contract.SupportsCustomFields)
						throw new DepartmentImportException("import_custom_fields_unsupported");
					clean.Columns.Add(new ImportColumnBinding
					{
						SourceColumn = binding.SourceColumn,
						Kind = binding.Kind,
						CustomFieldLabel = Clip(string.IsNullOrWhiteSpace(binding.CustomFieldLabel) ? binding.SourceColumn : binding.CustomFieldLabel.Trim(), 100),
						Transforms = binding.Transforms?.Where(ImportTransforms.IsKnown).ToList() ?? new List<string>()
					});
					continue;
				}

				var field = contract.Field(binding.FieldKey);
				if (field == null)
					throw new DepartmentImportException("import_unknown_field");

				var bound = ImportColumnMatcher.Bind(binding.SourceColumn, field, binding.Confidence);
				bound.Transforms = binding.Transforms?.Where(ImportTransforms.IsKnown).Distinct().Take(5).ToList() ?? new List<string>();
				clean.Columns.Add(bound);
				if (bound.Kind == (int)ImportColumnBindingKind.SourceIdentity)
					clean.SourceIdentityColumn = bound.SourceColumn;
			}

			return clean;
		}

		private async Task<Dictionary<string, int>> DistinctValuesAsync(ImportDataset dataset, IReadOnlyCollection<string> columns, ImportFieldContract field,
			CancellationToken cancellationToken)
		{
			var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			if (columns.Count == 0)
				return counts;

			var artifact = await _artifacts.GetByIdAsync(dataset.DepartmentId, dataset.ArtifactId, cancellationToken) ?? throw new ImportArtifactIntegrityException("IMP-ARTIFACT-MISSING");
			var key = await _keys.UnwrapAsync(dataset.DepartmentId, artifact.WrappedDataKey);
			var codec = new ImportStagingCodec(_cipher);
			int[] indexes = null;
			var after = 0;

			while (true)
			{
				var page = await _rows.GetPageAsync(dataset.DepartmentId, dataset.Id, after, 1000, cancellationToken);
				if (page.Count == 0)
					break;

				foreach (var row in page)
				{
					after = row.RowNumber;
					var cells = codec.Open<string[]>(key, ImportStagingCodec.Binding(dataset.Id, row.RowNumber, "source"), row.SourcePayload);
					if (cells == null)
						continue;
					if (indexes == null)
					{
						if (cells.All(string.IsNullOrWhiteSpace))
							continue;
						var headers = cells.Select(c => c?.Trim() ?? string.Empty).ToList();
						indexes = columns.Select(c => headers.FindIndex(h => string.Equals(h, c?.Trim(), StringComparison.Ordinal))).Where(i => i >= 0).ToArray();
						continue;
					}
					if (row.IsExample || row.Excluded)
						continue;

					foreach (var index in indexes.Where(i => i < cells.Length))
					{
						var text = ImportFieldConverter.Clean(cells[index]);
						if (text == null)
							continue;
						var values = field.Type == ImportFieldTypes.Multi
							? text.Split(new[] { string.IsNullOrEmpty(field.Separator) ? ";" : field.Separator }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim())
							: new[] { text };
						foreach (var value in values.Where(v => v.Length > 0))
							counts[value] = counts.TryGetValue(value, out var n) ? n + 1 : 1;
					}
				}
			}

			return counts;
		}

		private async Task<IImportCatalog> CatalogForAsync(ImportDataset dataset, CancellationToken cancellationToken)
		{
			var workspace = await _workspaces.GetByIdAsync(dataset.DepartmentId, dataset.WorkspaceId, cancellationToken);
			return _catalog.GetVersion(workspace?.CatalogVersion) ?? _catalog;
		}

		private async Task<ImportDataset> RequireEditableAsync(int departmentId, string datasetId, CancellationToken cancellationToken)
		{
			var dataset = await _datasets.GetByIdAsync(departmentId, datasetId, cancellationToken) ?? throw new DepartmentImportException("import_dataset_not_found", 404);
			var status = (ImportDatasetStatus)dataset.Status;
			if (status == ImportDatasetStatus.Importing || status == ImportDatasetStatus.Imported || status == ImportDatasetStatus.Verified ||
				status == ImportDatasetStatus.Excluded || status == ImportDatasetStatus.Superseded)
				throw new DepartmentImportException("import_dataset_locked", 409);
			return dataset;
		}

		private static string Clip(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}

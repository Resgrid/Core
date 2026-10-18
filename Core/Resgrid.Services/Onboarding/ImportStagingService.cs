using System;
using System.Collections.Generic;
using System.IO;
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
	/// Reads an artifact's sheets into staged rows (wizard plan section 10.1 "Dataset split" and "Stage"). One sheet (or the
	/// single table of a CSV/JSON file) becomes one dataset; every physical row becomes one sealed ImportStagedRow, header
	/// included, so row numbers stay the ones the administrator sees. Columns that look like excluded data are blanked
	/// before staging and never stored. Resumable: a crash between pages re-reads the sheet and replaces rows past the cursor.
	/// </summary>
	public class ImportStagingService
	{
		public const int PageSize = 500;
		private const int ProfileExamples = 3;
		private const int DistinctTrackingLimit = 5000;
		/// <summary>How many values of a column are judged for the shape of excluded data (profiling keeps as many).</summary>
		private const int ValueSampleSize = 50;
		/// <summary>At most this many rows (header included) are held back unsealed while the value check collects its sample.</summary>
		private const int ValueCheckRows = 1000;
		private const string ExcludedCode = "IMP-EXCLUDED-SENSITIVE";

		private readonly IImportArtifactStore _store;
		private readonly IImportArtifactCipher _cipher;
		private readonly IImportFileReader _reader;
		private readonly IImportKeyService _keys;
		private readonly IImportDatasetsRepository _datasets;
		private readonly IImportStagedRowsRepository _rows;
		private readonly IImportIssuesRepository _issues;

		public ImportStagingService(IImportArtifactStore store, IImportArtifactCipher cipher, IImportFileReader reader, IImportKeyService keys,
			IImportDatasetsRepository datasets, IImportStagedRowsRepository rows, IImportIssuesRepository issues)
		{
			_store = store;
			_cipher = cipher;
			_reader = reader;
			_keys = keys;
			_datasets = datasets;
			_rows = rows;
			_issues = issues;
		}

		/// <summary>Opens the artifact's plaintext as a seekable stream (decrypting segment by segment).</summary>
		public async Task<Stream> OpenAsync(ImportArtifact artifact, CancellationToken cancellationToken)
		{
			var key = await _keys.UnwrapAsync(artifact.DepartmentId, artifact.WrappedDataKey);
			return await _cipher.OpenReadAsync(_store, artifact.ObjectKey, key, artifact.Id, cancellationToken);
		}

		/// <summary>The datasets an artifact splits into (creating missing ones; visible data sheets only).</summary>
		public async Task<List<ImportDataset>> EnsureDatasetsAsync(ImportArtifact artifact, Stream content, CancellationToken cancellationToken)
		{
			var existing = await _datasets.GetForArtifactAsync(artifact.DepartmentId, artifact.Id, cancellationToken);
			if (existing.Count > 0)
				return existing;

			var format = (ImportFileFormat)artifact.DetectedFormat;
			var sheets = _reader.ListSheets(content, format)
				.Where(s => !s.Hidden && !string.Equals(s.Name, ImportTemplateMarkers.InstructionsSheet, StringComparison.OrdinalIgnoreCase)
					&& !string.Equals(s.Name, ImportTemplateMarkers.ListsSheet, StringComparison.OrdinalIgnoreCase))
				.Take(Config.ImportConfig.MaxWorksheets)
				.ToList();

			var now = DateTime.UtcNow;
			var created = sheets.Select(sheet => new ImportDataset
			{
				DepartmentId = artifact.DepartmentId,
				WorkspaceId = artifact.WorkspaceId,
				ArtifactId = artifact.Id,
				SourceId = artifact.SourceId,
				SheetIndex = sheet.Index,
				SheetName = sheet.Name,
				Name = sheet.Name ?? artifact.OriginalName,
				Status = (int)ImportDatasetStatus.Analyzing,
				StaffAuthored = artifact.StaffAuthored,
				CreatedOn = now,
				UpdatedOn = now
			}).ToList();

			foreach (var dataset in created)
				await _datasets.InsertAsync(dataset, cancellationToken, true);

			return created;
		}

		/// <summary>
		/// Stages one sheet from <paramref name="afterRow"/> on. Returns the last staged row number and whether the sheet is
		/// finished; stops early (not finished) when <paramref name="deadline"/> passes so the run can checkpoint.
		/// </summary>
		public async Task<(int LastRow, bool Finished, ImportSheetReadResult Read)> StageSheetAsync(ImportArtifact artifact, ImportDataset dataset, Stream content,
			int afterRow, int maxRows, DateTime deadline, Func<CancellationToken, Task<bool>> heartbeat, CancellationToken cancellationToken)
		{
			var key = await _keys.UnwrapAsync(artifact.DepartmentId, artifact.WrappedDataKey);
			var codec = new ImportStagingCodec(_cipher);

			// Rows past the cursor may have been written by a slice that crashed before saving it: replace them.
			await DeleteRowsAfterAsync(dataset, afterRow, cancellationToken);

			// The header is the first non-empty row; columns whose header names excluded data are blanked in every row, and so
			// are the columns an earlier pass over this sheet recorded as excluded by their values.
			var recorded = await RecordedExclusionsAsync(dataset, cancellationToken);
			List<string> headers = afterRow > 0 ? await HeadersAsync(dataset, afterRow, key, codec, cancellationToken) : null;
			var blanked = headers == null ? Array.Empty<int>() : ExcludedColumns(dataset, headers, recorded);
			var headerRowNumber = -1;

			// Values shaped like card numbers or national identifiers are excluded whatever the header says (section 8.2). From
			// the header on, rows are held back unsealed until every column has the sample profiling judges it by (or
			// ValueCheckRows rows were read); the decision is recorded before any of them is written, so a later slice of this
			// sheet, or a retry, reads it back above.
			var decided = afterRow > 0;
			var samples = new List<List<string>>();
			var held = new List<(int Row, string[] Values)>();

			var page = new List<ImportStagedRow>(PageSize);
			var lastRow = afterRow;
			var stoppedEarly = false;
			var now = DateTime.UtcNow;

			void Flush()
			{
				if (page.Count == 0)
					return;
				// The readers are synchronous (CsvHelper, OpenXml SAX); the worker thread blocks on the page insert.
				_rows.InsertManyAsync(page.ToList(), cancellationToken).GetAwaiter().GetResult();
				page.Clear();
				if (heartbeat != null && !heartbeat(cancellationToken).GetAwaiter().GetResult())
					throw new OperationCanceledException("Import run lease lost.");
			}

			ImportStagedRow Sealed(int row, string[] values) => new ImportStagedRow
			{
				DepartmentId = dataset.DepartmentId,
				WorkspaceId = dataset.WorkspaceId,
				DatasetId = dataset.Id,
				RowNumber = row,
				SourcePayload = codec.Seal(key, ImportStagingCodec.Binding(dataset.Id, row, "source"), values),
				IsEncrypted = true,
				ValidationState = (int)ImportRowValidationState.Pending,
				CreatedOn = now,
				UpdatedOn = now
			};

			void Decide()
			{
				decided = true;
				var byValue = headers == null ? Array.Empty<int>() : Enumerable.Range(0, Math.Min(headers.Count, samples.Count))
					.Where(i => !blanked.Contains(i) && ImportColumnMatcher.LooksLikeExcludedValues(samples[i])).ToArray();
				if (byValue.Length > 0)
				{
					// Like Flush, the worker thread blocks on the write.
					RaiseExcludedColumnsAsync(dataset, headers, byValue, cancellationToken).GetAwaiter().GetResult();
					blanked = blanked.Concat(byValue).ToArray();
				}

				foreach (var (row, values) in held)
					page.Add(Sealed(row, row > headerRowNumber ? Blank(values, byValue) : values));
				held.Clear();
			}

			ImportSheetReadResult read;
			try
			{
				read = _reader.ReadSheet(content, (ImportFileFormat)artifact.DetectedFormat, dataset.SheetIndex, new ImportReadOptions
				{
					MaxRows = maxRows,
					MaxColumns = Config.ImportConfig.MaxColumns,
					MaxFieldLength = Config.ImportConfig.MaxFieldLength,
					Delimiter = artifact.DetectedDelimiter,
					TimeBudget = TimeSpan.FromSeconds(Config.ImportConfig.ParseTimeBudgetSeconds)
				}, (row, cells) =>
				{
					if (row <= afterRow)
						return;

					cancellationToken.ThrowIfCancellationRequested();
					var values = cells;
					if (headers == null && cells.Any(c => !string.IsNullOrWhiteSpace(c)))
					{
						headers = cells.Select(c => c ?? string.Empty).ToList();
						blanked = ExcludedColumns(dataset, headers, recorded);
						headerRowNumber = row;
					}
					else if (headers != null)
					{
						if (!decided)
							AddSamples(samples, cells, headers.Count);
						values = Blank(cells, blanked);
					}
					lastRow = row;

					if (decided || headers == null)
						page.Add(Sealed(row, values));
					else
					{
						held.Add((row, values));
						if (held.Count < ValueCheckRows && !SamplesComplete(samples, headers.Count, blanked))
							return;
						Decide();
					}

					if (page.Count >= PageSize)
					{
						Flush();
						if (DateTime.UtcNow >= deadline)
							throw new StopReading();
					}
				}, cancellationToken);
			}
			catch (StopReading)
			{
				stoppedEarly = true;
				read = new ImportSheetReadResult();
			}

			if (!decided)
				Decide();
			Flush();

			if (!stoppedEarly && blanked.Length > 0 && headers != null)
				await RaiseExcludedColumnsAsync(dataset, headers, blanked, cancellationToken);

			return (lastRow, !stoppedEarly, read);
		}

		private static int[] ExcludedColumns(ImportDataset dataset, IReadOnlyList<string> headers, ISet<string> recorded) =>
			headers.Select((h, i) => ImportColumnMatcher.IsExcludedHeader(h) || recorded.Contains(ExcludedIdentity(dataset, h)) ? i : -1)
				.Where(i => i >= 0).ToArray();

		private static string ExcludedIdentity(ImportDataset dataset, string header) => ImportIssueFactory.IdentityKey(dataset.Id, null, header, ExcludedCode);

		/// <summary>Identity keys of the columns recorded as excluded data for this dataset, by their header or by their values.</summary>
		private async Task<HashSet<string>> RecordedExclusionsAsync(ImportDataset dataset, CancellationToken cancellationToken) =>
			new HashSet<string>((await _issues.GetForDatasetAsync(dataset.DepartmentId, dataset.Id, cancellationToken))
				.Where(i => i.Code == ExcludedCode).Select(i => i.IdentityKey), StringComparer.Ordinal);

		/// <summary>The sample a column's values are judged by: its first non-empty values in row order, header excluded.</summary>
		private static void AddSamples(List<List<string>> samples, string[] cells, int columns)
		{
			while (samples.Count < columns)
				samples.Add(new List<string>());
			for (var i = 0; i < columns && i < cells.Length; i++)
			{
				if (samples[i].Count >= ValueSampleSize)
					continue;
				var value = ImportFieldConverter.Clean(cells[i]);
				if (value != null)
					samples[i].Add(value);
			}
		}

		private static bool SamplesComplete(List<List<string>> samples, int columns, int[] blanked) =>
			Enumerable.Range(0, columns).All(i => blanked.Contains(i) || i < samples.Count && samples[i].Count >= ValueSampleSize);

		private static string[] Blank(string[] cells, IReadOnlyCollection<int> columns)
		{
			if (columns.Count == 0)
				return cells;
			var values = (string[])cells.Clone();
			foreach (var index in columns.Where(i => i < values.Length))
				values[index] = null;
			return values;
		}

		private sealed class StopReading : Exception { }

		/// <summary>Builds the column profile from the staged rows (fill rate, distinct count, inferred type, safe examples).</summary>
		public async Task<(ImportDatasetProfile Profile, List<string> Headers, int HeaderRow)> ProfileAsync(ImportArtifact artifact, ImportDataset dataset,
			CancellationToken cancellationToken)
		{
			var key = await _keys.UnwrapAsync(artifact.DepartmentId, artifact.WrappedDataKey);
			var codec = new ImportStagingCodec(_cipher);
			var profile = new ImportDatasetProfile();
			var recorded = await RecordedExclusionsAsync(dataset, cancellationToken);
			List<string> headers = null;
			var excludedByRecord = new List<bool>();
			var headerRow = 0;
			var distinct = new List<HashSet<string>>();
			var samples = new List<List<string>>();

			var after = 0;
			while (true)
			{
				var page = await _rows.GetPageAsync(dataset.DepartmentId, dataset.Id, after, 1000, cancellationToken);
				if (page.Count == 0)
					break;

				foreach (var row in page)
				{
					after = row.RowNumber;
					var cells = codec.Open<string[]>(key, ImportStagingCodec.Binding(dataset.Id, row.RowNumber, "source"), row.SourcePayload) ?? Array.Empty<string>();
					if (headers == null)
					{
						if (cells.All(string.IsNullOrWhiteSpace))
							continue;
						headers = cells.Select(c => c?.Trim() ?? string.Empty).ToList();
						// Staging records exclusions against the header as read; a column blanked for its values has none left to judge.
						excludedByRecord = cells.Select(c => recorded.Contains(ExcludedIdentity(dataset, c ?? string.Empty))).ToList();
						headerRow = row.RowNumber;
						for (var i = 0; i < headers.Count; i++)
						{
							profile.Columns.Add(new ImportColumnProfile { Index = i, Header = headers[i] });
							distinct.Add(new HashSet<string>(StringComparer.Ordinal));
							samples.Add(new List<string>());
						}
						continue;
					}

					if (cells.All(string.IsNullOrWhiteSpace))
						continue;

					profile.RowCount++;
					for (var i = 0; i < profile.Columns.Count && i < cells.Length; i++)
					{
						var value = ImportFieldConverter.Clean(cells[i]);
						if (value == null)
							continue;
						profile.Columns[i].FilledCount++;
						if (distinct[i].Count < DistinctTrackingLimit)
							distinct[i].Add(value);
						if (samples[i].Count < ValueSampleSize)
							samples[i].Add(value);
					}
				}
			}

			for (var i = 0; i < profile.Columns.Count; i++)
			{
				var column = profile.Columns[i];
				column.DistinctCount = distinct[i].Count;
				column.InferredType = InferType(samples[i]);
				column.LooksExcluded = ImportColumnMatcher.IsExcludedHeader(column.Header) || ImportColumnMatcher.LooksLikeExcludedValues(samples[i]) ||
					excludedByRecord[i];
				// Examples are kept only for code-like columns: personal values (emails, phones, names) stay in the sealed rows.
				column.LooksSensitive = column.InferredType == ImportFieldTypes.Email || column.InferredType == ImportFieldTypes.Phone ||
					(column.InferredType == ImportFieldTypes.Text && column.DistinctCount > 20);
				if (!column.LooksSensitive && !column.LooksExcluded)
					column.Examples = distinct[i].Take(ProfileExamples).ToList();
			}

			return (profile, headers ?? new List<string>(), headerRow);
		}

		private async Task<List<string>> HeadersAsync(ImportDataset dataset, int afterRow, string key, ImportStagingCodec codec, CancellationToken cancellationToken)
		{
			// Resuming past the header: read it back from the first staged row that has content.
			var after = 0;
			while (after < afterRow)
			{
				var page = await _rows.GetPageAsync(dataset.DepartmentId, dataset.Id, after, 50, cancellationToken);
				if (page.Count == 0)
					break;
				foreach (var row in page)
				{
					after = row.RowNumber;
					var cells = codec.Open<string[]>(key, ImportStagingCodec.Binding(dataset.Id, row.RowNumber, "source"), row.SourcePayload);
					if (cells != null && cells.Any(c => !string.IsNullOrWhiteSpace(c)))
						return cells.Select(c => c ?? string.Empty).ToList();
				}
			}

			return null;
		}

		private async Task DeleteRowsAfterAsync(ImportDataset dataset, int afterRow, CancellationToken cancellationToken)
		{
			if (afterRow <= 0)
			{
				await _rows.DeleteForDatasetAsync(dataset.DepartmentId, dataset.Id, cancellationToken);
				return;
			}

			var stale = new List<string>();
			var after = afterRow;
			while (true)
			{
				var page = await _rows.GetPageAsync(dataset.DepartmentId, dataset.Id, after, 1000, cancellationToken);
				if (page.Count == 0)
					break;
				stale.AddRange(page.Select(r => r.Id));
				after = page[page.Count - 1].RowNumber;
			}

			foreach (var id in stale)
				await _rows.DeleteAsync(new ImportStagedRow { Id = id, DepartmentId = dataset.DepartmentId }, cancellationToken);
		}

		private async Task RaiseExcludedColumnsAsync(ImportDataset dataset, IReadOnlyList<string> headers, IEnumerable<int> columns, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			var issues = columns.Where(i => i < headers.Count).Select(i => new ImportIssue
			{
				DepartmentId = dataset.DepartmentId,
				WorkspaceId = dataset.WorkspaceId,
				DatasetId = dataset.Id,
				ArtifactId = dataset.ArtifactId,
				ColumnName = Truncate(headers[i], 255),
				Code = ExcludedCode,
				Severity = (int)ImportIssueSeverity.Info,
				Level = (int)ImportLadderLevel.L4RowRules,
				IdentityKey = ExcludedIdentity(dataset, headers[i]),
				Parameters = JsonConvert.SerializeObject(new { column = Truncate(headers[i], 255) }),
				Status = (int)ImportIssueStatus.Resolved,
				ResolutionKind = (int)ImportIssueResolutionKind.ResolvedBySystem,
				ResolvedByKind = (int)ImportActorKind.System,
				ResolvedOn = now,
				CreatedOn = now,
				UpdatedOn = now
			}).ToList();

			var existing = new HashSet<string>((await _issues.GetForDatasetAsync(dataset.DepartmentId, dataset.Id, cancellationToken)).Select(i => i.IdentityKey));
			await _issues.InsertManyAsync(issues.Where(i => !existing.Contains(i.IdentityKey)).ToList(), cancellationToken);
		}

		internal static string InferType(IReadOnlyCollection<string> samples)
		{
			if (samples.Count == 0)
				return ImportFieldTypes.Text;

			bool All(Func<string, bool> test) => samples.Count(test) * 10 >= samples.Count * 9;
			var context = new ImportConversionContext();

			if (All(s => !ImportFieldConverter.Convert(new ImportFieldContract { Type = ImportFieldTypes.Email }, s, context).Invalid))
				return ImportFieldTypes.Email;
			if (All(s => !ImportFieldConverter.Convert(new ImportFieldContract { Type = ImportFieldTypes.Boolean }, s, context).Invalid))
				return ImportFieldTypes.Boolean;
			if (All(s => !ImportFieldConverter.Convert(new ImportFieldContract { Type = ImportFieldTypes.Integer }, s, context).Invalid))
				return samples.Any(s => s.StartsWith("+", StringComparison.Ordinal) || s.Length >= 10) ? ImportFieldTypes.Phone : ImportFieldTypes.Integer;
			if (All(s => !ImportFieldConverter.Convert(new ImportFieldContract { Type = ImportFieldTypes.Decimal }, s, context).Invalid))
				return ImportFieldTypes.Decimal;
			if (All(s => { var c = ImportFieldConverter.Convert(new ImportFieldContract { Type = ImportFieldTypes.Date }, s, context); return !c.Invalid || c.IssueCode == "IMP-TYPE-DATE-AMBIGUOUS"; }))
				return ImportFieldTypes.Date;
			if (All(s => s.Count(char.IsDigit) >= 7 && s.All(c => char.IsDigit(c) || " +-().".IndexOf(c) >= 0)))
				return ImportFieldTypes.Phone;
			return ImportFieldTypes.Text;
		}

		private static string Truncate(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}

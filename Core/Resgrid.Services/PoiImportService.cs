using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// POI uploads. Departments usually start from a spreadsheet of street addresses: uploaded as CSV, or converted to KML by
	/// Google My Maps, which writes an &lt;address&gt; and the address columns but no &lt;Point&gt; for a layer it placed
	/// "by address". The upload request only reads the file and queues it (<see cref="PoiImport"/>); worker 79 geocodes the
	/// addresses and saves the POIs, which for a few thousand rows takes longer than a web request may run behind a proxy.
	/// </summary>
	public class PoiImportService : IPoiImportService
	{
		/// <summary>Distinct addresses looked up per import; each is a paid geocoder call unless it is already cached.</summary>
		public const int MaximumGeocodedAddresses = 5000;

		/// <summary>Failed runs before an import is given up (a stopped worker, a database error).</summary>
		public const int MaximumAttempts = 3;

		/// <summary>Unlocated rows of each kind kept for the import report.</summary>
		public const int ReportedUnlocatedRows = 100;

		/// <summary>
		/// The waits before asking again for addresses no geocoder answered for (an outage, a rate limit): a blip clears
		/// on the first retry, an outage gets the better part of an hour. An address a geocoder answered for and could not
		/// find is never asked again.
		/// </summary>
		public static readonly TimeSpan[] LookupRetryDelays = { TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30) };

		/// <summary>Lookups per address before it is reported as unavailable: the first and one per retry delay.</summary>
		public static int MaximumLookupAttempts => LookupRetryDelays.Length + 1;

		/// <summary>A running import whose heartbeat is older than this is resumed by the next worker tick.</summary>
		public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

		/// <summary>Finished imports (the report) are kept this long.</summary>
		public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

		/// <summary>
		/// How often a run writes its heartbeat; while locating, the rows' lookup results go with it (a checkpoint). Tests shorten it.
		/// </summary>
		public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

		/// <summary>Lookups in flight at once: well inside the geocoders' per-second limits.</summary>
		private const int GeocodingConcurrency = 4;

		/// <summary>
		/// Lookups between heartbeats. The geocoding runs in parallel and the database writes must not, so the heartbeat is
		/// written between chunks: at most 6 rounds of <see cref="LookupTimeout"/>, well inside <see cref="StaleAfter"/>.
		/// </summary>
		private const int GeocodingChunk = 24;

		private readonly IKmlProvider _kmlProvider;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IPoiImportsRepository _poiImportsRepository;
		private readonly IMappingService _mappingService;

		public PoiImportService(IKmlProvider kmlProvider, IGeoLocationProvider geoLocationProvider, IPoiImportsRepository poiImportsRepository, IMappingService mappingService)
		{
			_kmlProvider = kmlProvider;
			_geoLocationProvider = geoLocationProvider;
			_poiImportsRepository = poiImportsRepository;
			_mappingService = mappingService;
		}

		/// <summary>
		/// One address lookup's wait. The chain's own geocoders time out sooner, but one that ignores its timeout is abandoned
		/// after this and counted as unanswered (retried). Tests shorten it.
		/// </summary>
		public TimeSpan LookupTimeout { get; set; } = TimeSpan.FromSeconds(30);

		public PoiImportFile ReadFile(Stream input, string fileName)
		{
			var result = new PoiImportFile();
			var extension = (Path.GetExtension(fileName ?? string.Empty) ?? string.Empty).ToLowerInvariant();

			if (extension == ".csv")
			{
				var csv = PoiImportFileReader.ReadCsv(input);
				result.Error = csv.Error;
				result.SkippedCount = csv.SkippedCount;

				if (string.IsNullOrEmpty(csv.Error))
					result.Rows.AddRange(csv.Rows);

				return result;
			}

			var kml = _kmlProvider.ImportFile(input, extension == ".kmz");
			if (!string.IsNullOrEmpty(kml.Error))
			{
				result.Error = kml.Error;
				return result;
			}

			// A placemark's ExtendedData can name its capabilities ("Capabilities" column of a My Maps or QGIS export).
			result.Rows.AddRange(kml.Coordinates.Select(c => new PoiImportRow
			{
				Name = c.Name,
				Latitude = c.Latitude,
				Longitude = c.Longitude,
				Capabilities = c is KmlPositionedPlacemark placemark ? PoiImportFileReader.FromFields(placemark.Fields).Capabilities : null
			}));
			result.SkippedCount = kml.SkippedCount;

			foreach (var placemark in kml.AddressOnly)
			{
				var row = PoiImportFileReader.FromFields(placemark.Fields);
				row.Name = placemark.Name ?? row.Name;

				// Address columns ("1 E Erie St, Chicago, Illinois, 60611") geocode better than <address>, which My Maps builds
				// from every column the user picked for the location, IDs and names included.
				if (string.IsNullOrWhiteSpace(row.Address))
					row.Address = placemark.Address;

				if (string.IsNullOrWhiteSpace(row.Name))
					row.Name = row.Address;

				if (row.HasPosition || !string.IsNullOrWhiteSpace(row.Address))
					result.Rows.Add(row);
				else
					result.SkippedCount++;
			}

			return result;
		}

		public async Task<PoiImport> QueueImportAsync(int departmentId, int poiTypeId, string poiTypeName, string userId, string fileName, PoiImportFile file, CancellationToken cancellationToken = default(CancellationToken))
		{
			var import = new PoiImport
			{
				DepartmentId = departmentId,
				PoiTypeId = poiTypeId,
				PoiTypeName = poiTypeName?.Truncate(250),
				UserId = userId,
				// GetSafeFileName, not Path.GetFileName: on Linux hosts Path.GetFileName keeps a Windows client path
				// ("C:\fakepath\pois.kml") whole because '\' is not a separator there.
				FileName = FileHelper.GetSafeFileName(fileName).Truncate(260),
				Status = (int)PoiImportStatus.Queued,
				TotalRows = file.Rows.Count,
				SkippedCount = file.SkippedCount,
				RowsJson = JsonConvert.SerializeObject(file.Rows),
				AddedOn = DateTime.UtcNow
			};

			return await _poiImportsRepository.SaveOrUpdateAsync(import, cancellationToken);
		}

		public async Task<List<PoiImport>> GetRecentImportsAsync(int departmentId, int max = 10, string userId = null)
		{
			return await _poiImportsRepository.GetRecentForDepartmentAsync(departmentId, max, userId);
		}

		public List<PoiImportRow> GetUnlocatedRows(PoiImport import)
		{
			return Rows(import?.UnlocatedJson);
		}

		public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default(CancellationToken))
		{
			var staleBefore = DateTime.UtcNow - StaleAfter;

			foreach (var candidate in await _poiImportsRepository.GetRunnableAsync(staleBefore, 5))
			{
				// A queued import starts by locating; a stopped one resumes where it was (saving resumes after the POIs it saved).
				var resumeAt = candidate.Status == (int)PoiImportStatus.Saving ? PoiImportStatus.Saving : PoiImportStatus.Locating;
				var claimToken = Guid.NewGuid().ToString();
				if (!await _poiImportsRepository.TryClaimAsync(candidate.PoiImportId, candidate.Status, (int)resumeAt, staleBefore, claimToken, cancellationToken))
					continue;

				var import = await _poiImportsRepository.GetByIdAsync(candidate.PoiImportId);
				if (import == null)
					continue;

				await RunAsync(import, claimToken, cancellationToken);
				return true;
			}

			return false;
		}

		public async Task<int> CleanupAsync(CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _poiImportsRepository.DeleteFinishedBeforeAsync(DateTime.UtcNow - Retention, cancellationToken);
		}

		/// <summary>Thrown inside a run whose claim another run took over (this one stalled past <see cref="StaleAfter"/>).</summary>
		private sealed class ClaimLostException : Exception
		{
		}

		private async Task RunAsync(PoiImport import, string claimToken, CancellationToken cancellationToken)
		{
			// Every write to the import names this run's claim: a run that stalled past the stale period and was replaced stops
			// at its next write instead of overwriting (or releasing, or failing) the run that took the import over.
			async Task SaveAsync()
			{
				if (!await _poiImportsRepository.SaveRunAsync(import, claimToken, CancellationToken.None))
					throw new ClaimLostException();
			}

			try
			{
				if (import.Attempts > MaximumAttempts)
				{
					await FailAsync(import, claimToken, string.Format("The import stopped {0} times before it finished and was given up.", MaximumAttempts));
					return;
				}

				// The type can be deleted while the import waits; its POIs would have nowhere to go.
				var type = await _mappingService.GetTypeByIdAsync(import.PoiTypeId);
				if (type == null || type.DepartmentId != import.DepartmentId)
				{
					await FailAsync(import, claimToken, "The POI type was deleted before the import ran.");
					return;
				}

				var rows = Rows(import.RowsJson);
				var lastBeat = DateTime.UtcNow;

				// Writes stay sequential (one unit of work), so the heartbeat is written in between, never alongside.
				async Task BeatAsync()
				{
					if (DateTime.UtcNow - lastBeat < HeartbeatInterval)
						return;

					if (!await _poiImportsRepository.HeartbeatAsync(import.PoiImportId, claimToken, CancellationToken.None))
						throw new ClaimLostException();

					lastBeat = DateTime.UtcNow;
				}

				// While locating, the heartbeat also saves the lookups made so far (through the claim-guarded write), so a run that
				// stops part way resumes without asking the geocoders again for the addresses it already asked about.
				async Task CheckpointAsync()
				{
					if (DateTime.UtcNow - lastBeat < HeartbeatInterval)
						return;

					import.RowsJson = JsonConvert.SerializeObject(rows);
					import.HeartbeatOn = DateTime.UtcNow;
					await SaveAsync();
					lastBeat = DateTime.UtcNow;
				}

				if (import.Status == (int)PoiImportStatus.Locating)
				{
					await LocateAsync(import, rows, CheckpointAsync, cancellationToken);
					import.RowsJson = JsonConvert.SerializeObject(rows);

					// Lookups no geocoder answered wait out a back-off; the run ended cleanly, so it does not count as a failure.
					var waiting = rows.Where(IsRetryable).ToList();
					if (waiting.Count > 0)
					{
						import.PendingRetryCount = waiting.Count;
						import.RetryAfter = DateTime.UtcNow + LookupRetryDelays[Math.Min(waiting.Max(r => r.LookupAttempts), LookupRetryDelays.Length) - 1];
						import.HeartbeatOn = null;
						import.Attempts = 0;
						await SaveAsync();
						return;
					}

					// The positions are stored before any POI is saved, so a resumed import saves exactly the same rows in the same order.
					import.PendingRetryCount = 0;
					import.RetryAfter = null;
					import.Status = (int)PoiImportStatus.Saving;
					import.HeartbeatOn = DateTime.UtcNow;
					await SaveAsync();
				}

				// Each POI records its row (Pois.PoiImportRow, unique per import), so a resumed import carries on after the last row
				// it saved. Counting the saved POIs instead would go wrong once one of them is deleted while the import runs.
				var positioned = rows.Where(r => r.HasPosition).ToList();
				var lastSaved = await _poiImportsRepository.GetLastSavedRowAsync(import.PoiImportId, CancellationToken.None);
				var firstToSave = (lastSaved ?? -1) + 1;

				// A run that stopped between a POI and its capabilities resumes after that POI, so the row saved last gets its
				// capabilities set again (a replace: the same result however often it runs).
				var resumedRow = lastSaved.HasValue && lastSaved.Value >= 0 && lastSaved.Value < positioned.Count ? lastSaved : null;

				// Capability names (a "Capabilities" column) are matched to the department's capabilities, adding the missing ones,
				// once per run rather than per row. A file without the column (or queued before it was read) makes no query at all.
				var capabilityIds = await GetCapabilityIdsAsync(import.DepartmentId, positioned.Skip(resumedRow ?? firstToSave));

				if (resumedRow.HasValue && HasCapabilities(positioned[resumedRow.Value]))
				{
					var resumedPoi = type.Pois?.FirstOrDefault(p => p.PoiImportId == import.PoiImportId && p.PoiImportRow == resumedRow.Value);
					if (resumedPoi != null)
						await SetCapabilitiesAsync(import.DepartmentId, resumedPoi.PoiId, positioned[resumedRow.Value], capabilityIds);
				}

				for (var i = firstToSave; i < positioned.Count; i++)
				{
					cancellationToken.ThrowIfCancellationRequested();

					var row = positioned[i];
					var saved = await _mappingService.SavePOIAsync(new Poi
					{
						PoiTypeId = import.PoiTypeId,
						// Pois.Name is nvarchar(250) and Pois.Address nvarchar(500); a longer value would fail the import.
						Name = row.Name?.Truncate(250),
						Address = row.Address?.Truncate(500),
						Note = row.Note,
						Latitude = row.Latitude.Value,
						Longitude = row.Longitude.Value,
						PoiImportId = import.PoiImportId,
						PoiImportRow = i
					}, CancellationToken.None);

					if (saved != null && saved.PoiId > 0 && HasCapabilities(row))
						await SetCapabilitiesAsync(import.DepartmentId, saved.PoiId, row, capabilityIds);

					await BeatAsync();
				}

				var unlocated = rows.Where(r => !r.HasPosition).ToList();
				var notFound = unlocated.Where(r => r.Lookup == (int)PoiImportRowLookup.NotFound).ToList();
				var failed = unlocated.Where(r => r.Lookup == (int)PoiImportRowLookup.Unavailable || r.Lookup == (int)PoiImportRowLookup.NotConfigured).ToList();
				var notTried = unlocated.Where(r => r.Lookup == (int)PoiImportRowLookup.OverLimit).ToList();

				import.ImportedCount = positioned.Count;
				import.GeocodedCount = rows.Count(r => r.Lookup == (int)PoiImportRowLookup.Found);
				import.UnlocatedCount = notFound.Count;
				import.LookupFailedCount = failed.Count;
				import.LookupNotConfigured = failed.Any(r => r.Lookup == (int)PoiImportRowLookup.NotConfigured);
				import.NotTriedCount = notTried.Count;
				import.UnlocatedJson = unlocated.Count > 0
					? JsonConvert.SerializeObject(notFound.Take(ReportedUnlocatedRows).Concat(failed.Take(ReportedUnlocatedRows)).Concat(notTried.Take(ReportedUnlocatedRows)))
					: null;
				import.RowsJson = null;
				import.Error = null;
				import.PendingRetryCount = 0;
				import.RetryAfter = null;
				import.Status = (int)PoiImportStatus.Complete;
				import.CompletedOn = DateTime.UtcNow;
				import.HeartbeatOn = null;
				await SaveAsync();
			}
			catch (ClaimLostException)
			{
				// Another run owns the import now and finishes it; this one has nothing left to write.
				Logging.LogInfo($"PoiImportService stopped POI import {import.PoiImportId} for department {import.DepartmentId}: another worker run took it over.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// The worker is stopping: the next one resumes the import straight away instead of waiting out the stale period.
				await _poiImportsRepository.ReleaseAsync(import.PoiImportId, claimToken, CancellationToken.None);
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"PoiImportService could not run POI import {import.PoiImportId} for department {import.DepartmentId}.");

				if (import.Attempts >= MaximumAttempts)
					await FailAsync(import, claimToken, string.Format("The import failed {0} times and was given up. POIs it saved before failing were kept.", MaximumAttempts));
				else
					await _poiImportsRepository.ReleaseAsync(import.PoiImportId, claimToken, CancellationToken.None);
			}
		}

		private static bool HasCapabilities(PoiImportRow row) => row?.Capabilities != null && row.Capabilities.Count > 0;

		/// <summary>
		/// The department's capability ids by name (ignoring case) for every capability the rows name, adding the names the
		/// department does not have yet. Empty, without a query, when no row names any.
		/// </summary>
		private async Task<Dictionary<string, string>> GetCapabilityIdsAsync(int departmentId, IEnumerable<PoiImportRow> rows)
		{
			var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var names = rows.Where(HasCapabilities).SelectMany(r => r.Capabilities).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (names.Count == 0)
				return result;

			// A write (it may add capabilities), so it finishes even when the worker is stopping, like the POI saves.
			var capabilities = await _mappingService.GetOrAddPoiCapabilitiesByNameAsync(departmentId, names, CancellationToken.None);

			foreach (var capability in capabilities ?? new List<PoiCapability>())
			{
				var name = PoiImportFileReader.CapabilityName(capability?.Name);
				if (name != null && !string.IsNullOrWhiteSpace(capability.PoiCapabilityId) && !result.ContainsKey(name))
					result[name] = capability.PoiCapabilityId;
			}

			return result;
		}

		/// <summary>Makes the row's named capabilities the saved POI's.</summary>
		private async Task SetCapabilitiesAsync(int departmentId, int poiId, PoiImportRow row, Dictionary<string, string> capabilityIds)
		{
			var ids = row.Capabilities
				.Select(name => capabilityIds.TryGetValue(PoiImportFileReader.CapabilityName(name) ?? string.Empty, out var id) ? id : null)
				.Where(id => id != null)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (ids.Count > 0)
				await _mappingService.SetPoiCapabilitiesAsync(departmentId, poiId, ids, CancellationToken.None);
		}

		/// <summary>A row whose lookup no geocoder answered and that has retries left.</summary>
		private static bool IsRetryable(PoiImportRow row) =>
			!row.HasPosition && row.Lookup == (int)PoiImportRowLookup.Unavailable && row.LookupAttempts < MaximumLookupAttempts;

		private async Task LocateAsync(PoiImport import, List<PoiImportRow> rows, Func<Task> checkpointAsync, CancellationToken cancellationToken)
		{
			// The per-import limit applies once, to the addresses never looked up, in file order.
			var fresh = rows.Where(r => !r.HasPosition && !string.IsNullOrWhiteSpace(r.Address) && r.Lookup == (int)PoiImportRowLookup.None)
				.GroupBy(r => r.Address.Trim(), StringComparer.OrdinalIgnoreCase).ToList();
			var alreadyTried = rows.Where(r => r.LookupAttempts > 0).Select(r => r.Address.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count();

			foreach (var over in fresh.Skip(Math.Max(0, MaximumGeocodedAddresses - alreadyTried)))
			{
				foreach (var row in over)
					row.Lookup = (int)PoiImportRowLookup.OverLimit;

				import.GeocodingLimitReached = true;
			}

			// First lookups and retries of lookups no geocoder answered; one lookup per distinct address.
			var lookups = rows.Where(r => !r.HasPosition && !string.IsNullOrWhiteSpace(r.Address) &&
			                              (r.Lookup == (int)PoiImportRowLookup.None || IsRetryable(r)))
				.GroupBy(r => r.Address.Trim(), StringComparer.OrdinalIgnoreCase).ToList();

			// The checkpoint (heartbeat plus the rows' lookups so far) runs between chunks, never alongside the parallel lookups.
			foreach (var chunk in lookups.Chunk(GeocodingChunk))
			{
				await Parallel.ForEachAsync(chunk, new ParallelOptions { MaxDegreeOfParallelism = GeocodingConcurrency, CancellationToken = cancellationToken }, async (lookup, token) =>
				{
					var result = await GeocodeAsync(lookup.Key, token);

					foreach (var row in lookup)
					{
						row.LookupAttempts++;
						row.Lookup = (int)ToRowLookup(result.Outcome);

						if (result.Outcome == GeocodeOutcome.Found)
						{
							row.Latitude = result.Latitude;
							row.Longitude = result.Longitude;
						}
					}
				});

				await checkpointAsync();
			}

			import.GeocodedCount = rows.Count(r => r.Lookup == (int)PoiImportRowLookup.Found);
		}

		private static PoiImportRowLookup ToRowLookup(GeocodeOutcome outcome)
		{
			switch (outcome)
			{
				case GeocodeOutcome.Found:
					return PoiImportRowLookup.Found;
				case GeocodeOutcome.NotFound:
					return PoiImportRowLookup.NotFound;
				case GeocodeOutcome.NotConfigured:
					return PoiImportRowLookup.NotConfigured;
				default:
					return PoiImportRowLookup.Unavailable;
			}
		}

		private async Task<GeocodeResult> GeocodeAsync(string address, CancellationToken cancellationToken)
		{
			try
			{
				var lookup = _geoLocationProvider.GeocodeAddressAsync(address, cancellationToken);

				if (await Task.WhenAny(lookup, Task.Delay(LookupTimeout, cancellationToken)) != lookup)
				{
					_ = lookup.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
					cancellationToken.ThrowIfCancellationRequested();
					return GeocodeResult.Of(GeocodeOutcome.Unavailable);
				}

				var result = await lookup ?? GeocodeResult.Of(GeocodeOutcome.Unavailable);

				// A "match" outside the valid range (or the 0,0 placeholder) is no answer at all.
				if (result.Outcome == GeocodeOutcome.Found &&
				    (!result.Latitude.HasValue || !result.Longitude.HasValue || !PoiImportFileReader.IsValidPosition(result.Latitude.Value, result.Longitude.Value)))
					return GeocodeResult.Of(GeocodeOutcome.Unavailable);

				return result;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "PoiImportService could not geocode an imported POI address.");
				return GeocodeResult.Of(GeocodeOutcome.Unavailable);
			}
		}

		/// <summary>Records the import as given up; nothing when another run has taken it over since.</summary>
		private async Task FailAsync(PoiImport import, string claimToken, string error)
		{
			import.ImportedCount = await _poiImportsRepository.CountSavedPoisAsync(import.PoiImportId, CancellationToken.None);
			import.Status = (int)PoiImportStatus.Failed;
			import.Error = error;
			import.RowsJson = null;
			import.PendingRetryCount = 0;
			import.RetryAfter = null;
			import.CompletedOn = DateTime.UtcNow;
			import.HeartbeatOn = null;

			if (!await _poiImportsRepository.SaveRunAsync(import, claimToken, CancellationToken.None))
				Logging.LogInfo($"PoiImportService did not fail POI import {import.PoiImportId}: another worker run took it over.");
		}

		private static List<PoiImportRow> Rows(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new List<PoiImportRow>();

			return JsonConvert.DeserializeObject<List<PoiImportRow>>(json) ?? new List<PoiImportRow>();
		}
	}
}

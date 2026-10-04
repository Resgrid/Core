using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class CallLocationHistoryService : ICallLocationHistoryService
	{
		/// <summary>An address match whose coordinates are further apart than this is the same street address in another town.</summary>
		public const double GeoVetoMeters = 1500;
		/// <summary>Upper bound on the proximity radius a caller may ask for.</summary>
		public const double MaxNearbyMeters = 500;
		public const int BackfillBatchSize = 1000;
		private const int DepartmentsPerFetch = 50;
		private const int MaxCandidates = 1000;
		public const int MaxOccupancyCountCandidates = 10000;

		private readonly ICallLocationKeysRepository _keys;
		private readonly ICallContactsRepository _callContacts;
		private readonly IDepartmentDataProtectionService _dataProtection;
		private readonly IDispatchScopeService _dispatchScope;
		private readonly IDepartmentsService _departments;
		// Lazy: the occupancy service graph (Records protection, grants) is only needed by contact and occupancy reads.
		private readonly Lazy<IOccupancyLocationLookup> _occupancies;

		public CallLocationHistoryService(ICallLocationKeysRepository keys, ICallContactsRepository callContacts, IDepartmentDataProtectionService dataProtection,
			IDispatchScopeService dispatchScope, IDepartmentsService departments, Lazy<IOccupancyLocationLookup> occupancies)
		{
			_keys = keys;
			_callContacts = callContacts;
			_dataProtection = dataProtection;
			_dispatchScope = dispatchScope;
			_departments = departments;
			_occupancies = occupancies;
		}

		#region Index

		public async Task IndexCallAsync(Call call, CancellationToken cancellationToken = default)
		{
			if (call == null || call.CallId <= 0)
				return;

			try
			{
				// Keys are derived from protected call fields: none are written while protection is not Disabled, and
				// worker 73 purges any that exist when a department enrolls.
				if (!await IsAddressMatchingAvailableAsync(call.DepartmentId, bypassCache: true))
					return;

				var key = BuildKey(call.CallId, call.DepartmentId, call.Address, call.GeoLocationData, call.LoggedOn, DateTime.UtcNow);
				if (key == null)
					await _keys.DeleteForCallAsync(call.CallId, cancellationToken);
				else
				{
					await _keys.UpsertAsync(new[] { key }, cancellationToken);
					// Enrollment can purge the index while this write is in flight.
					if (!await IsAddressMatchingAvailableAsync(call.DepartmentId, bypassCache: true) ||
						(await _keys.GetStateAsync(call.DepartmentId))?.IsSuppressed == true)
						await _keys.DeleteForCallAsync(call.CallId, cancellationToken);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Indexing the location of call {call.CallId} failed; its location history entry is stale until the call is saved again.");
			}
		}

		/// <summary>The index row for a call, or null when it has neither a street location nor usable coordinates.</summary>
		public static CallLocationKey BuildKey(int callId, int departmentId, string address, string geoLocationData, DateTime loggedOn, DateTime utcNow)
		{
			var parsed = ProtectedDataEnvelope.HasEnvelopePrefix(address) ? null : StreetAddressParser.Parse(address);
			var point = ProtectedDataEnvelope.HasEnvelopePrefix(geoLocationData) ? null : ValidPoint(GeoMath.ParseLatLonString(geoLocationData));
			var street = parsed?.HasStreetLocation == true ? parsed : null;
			if (street == null && point == null)
				return null;

			var canonical = street?.ToCanonical();
			return new CallLocationKey
			{
				CallId = callId,
				DepartmentId = departmentId,
				AddressKey = street?.IndexKey,
				AddressCanonical = canonical != null && canonical.Length > 500 ? null : canonical,
				Latitude = point.HasValue ? Math.Round((decimal)point.Value.Latitude, 6) : (decimal?)null,
				Longitude = point.HasValue ? Math.Round((decimal)point.Value.Longitude, 6) : (decimal?)null,
				LoggedOn = loggedOn,
				KeyVersion = ParsedStreetAddress.Version,
				IndexedOn = utcNow
			};
		}

		public async Task<CallLocationIndexSweepResult> RunIndexSweepAsync(TimeSpan budget, CancellationToken cancellationToken = default)
		{
			var result = new CallLocationIndexSweepResult();
			var clock = Stopwatch.StartNew();
			bool InBudget() => clock.Elapsed < budget && !cancellationToken.IsCancellationRequested;

			foreach (var departmentId in await _keys.GetDepartmentsToSuppressAsync(100))
			{
				if (!InBudget()) return result;
				try
				{
					await SuppressAsync(departmentId, await _keys.GetStateAsync(departmentId), cancellationToken);
					result.DepartmentsSuppressed++;
				}
				catch (Exception ex)
				{
					result.Errors++;
					Logging.LogException(ex, $"Purging the call location index for protected department {departmentId} failed.");
				}
			}

			foreach (var departmentId in await _keys.GetDepartmentsToResumeAsync(100))
			{
				if (!InBudget()) return result;
				try
				{
					await _keys.SaveStateAsync(new CallLocationIndexState { DepartmentId = departmentId, KeyVersion = ParsedStreetAddress.Version, ModifiedOn = DateTime.UtcNow }, cancellationToken);
					result.DepartmentsReset++;
				}
				catch (Exception ex)
				{
					result.Errors++;
					Logging.LogException(ex, $"Resuming the call location index for department {departmentId} failed.");
				}
			}

			var visited = new HashSet<int>();
			while (InBudget())
			{
				var pending = (await _keys.GetDepartmentsNeedingIndexAsync(ParsedStreetAddress.Version, DepartmentsPerFetch)).Where(visited.Add).ToList();
				if (pending.Count == 0)
					break;

				foreach (var departmentId in pending)
				{
					if (!InBudget()) break;
					result.DepartmentsVisited++;
					try
					{
						await IndexDepartmentAsync(departmentId, result, InBudget, cancellationToken);
					}
					catch (Exception ex)
					{
						result.Errors++;
						Logging.LogException(ex, $"Backfilling the call location index for department {departmentId} failed; it resumes from its cursor on the next run.");
					}
				}
			}

			return result;
		}

		private async Task IndexDepartmentAsync(int departmentId, CallLocationIndexSweepResult result, Func<bool> inBudget, CancellationToken cancellationToken)
		{
			var state = await _keys.GetStateAsync(departmentId) ?? new CallLocationIndexState { DepartmentId = departmentId, KeyVersion = ParsedStreetAddress.Version };
			if (state.KeyVersion < ParsedStreetAddress.Version || state.IsSuppressed)
			{
				state.KeyVersion = ParsedStreetAddress.Version;
				state.NextCallId = null;
				state.CompletedOn = null;
				state.IsSuppressed = false;
			}

			if (!await IsAddressMatchingAvailableAsync(departmentId, bypassCache: true))
			{
				await SuppressAsync(departmentId, state, cancellationToken);
				result.DepartmentsSuppressed++;
				return;
			}

			while (inBudget())
			{
				var sources = await _keys.GetSourcesAsync(departmentId, state.NextCallId, BackfillBatchSize);
				var now = DateTime.UtcNow;
				var keys = sources.Select(s => BuildKey(s.CallId, s.DepartmentId, s.Address, s.GeoLocationData, s.LoggedOn, now)).Where(k => k != null).ToList();
				await _keys.UpsertAsync(keys, cancellationToken);
				result.CallsIndexed += keys.Count;

				if (!await IsAddressMatchingAvailableAsync(departmentId, bypassCache: true))
				{
					await SuppressAsync(departmentId, state, cancellationToken);
					result.DepartmentsSuppressed++;
					return;
				}

				if (sources.Count > 0)
					state.NextCallId = sources[^1].CallId;
				state.ModifiedOn = now;

				if (sources.Count < BackfillBatchSize)
				{
					state.CompletedOn = now;
					await _keys.SaveStateAsync(state, cancellationToken);
					result.DepartmentsCompleted++;
					return;
				}

				await _keys.SaveStateAsync(state, cancellationToken);
			}
		}

		private async Task SuppressAsync(int departmentId, CallLocationIndexState state, CancellationToken cancellationToken)
		{
			await _keys.DeleteForDepartmentAsync(departmentId, cancellationToken);
			state ??= new CallLocationIndexState { DepartmentId = departmentId };
			state.KeyVersion = ParsedStreetAddress.Version;
			state.NextCallId = null;
			state.CompletedOn = null;
			state.IsSuppressed = true;
			state.ModifiedOn = DateTime.UtcNow;
			await _keys.SaveStateAsync(state, cancellationToken);
		}

		#endregion

		#region History

		public async Task<CallLocationHistoryResult> GetHistoryForCallAsync(int departmentId, string userId, int callId, int limit = CallLocationQuery.DefaultLimit)
		{
			if (!await CanReadAsync(departmentId, userId))
				return new CallLocationHistoryResult();

			var call = (await _keys.GetCallsAsync(departmentId, new[] { callId })).FirstOrDefault();
			if (call == null || !await _dispatchScope.CanUserAccessCallAsync(departmentId, userId, call))
				return new CallLocationHistoryResult();

			var contactIds = ((await _callContacts.GetCallContactsByCallIdAsync(callId)) ?? Enumerable.Empty<CallContact>())
				.Where(c => c.DepartmentId == departmentId && !string.IsNullOrWhiteSpace(c.ContactId)).Select(c => c.ContactId).Distinct().ToList();
			var point = ProtectedDataEnvelope.HasEnvelopePrefix(call.GeoLocationData) ? null : ValidPoint(GeoMath.ParseLatLonString(call.GeoLocationData));
			var location = new CallLocationQuery
			{
				Address = ProtectedDataEnvelope.HasEnvelopePrefix(call.Address) ? null : call.Address,
				Latitude = point?.Latitude,
				Longitude = point?.Longitude
			};

			return await BuildAsync(departmentId, userId, new[] { location }, contactIds, false, callId, limit, true);
		}

		public async Task<CallLocationHistoryResult> GetHistoryForContactAsync(int departmentId, string userId, string contactId, int limit = CallLocationQuery.DefaultLimit)
		{
			if (string.IsNullOrWhiteSpace(contactId) || !await CanReadAsync(departmentId, userId))
				return new CallLocationHistoryResult();

			var locations = (await _occupancies.Value.GetOccupanciesForContactAsync(departmentId, contactId)).Select(o => o.ToLocationQuery()).ToList();
			return await BuildAsync(departmentId, userId, locations, new[] { contactId }, false, null, limit, true);
		}

		public async Task<CallLocationHistoryResult> GetHistoryForOccupancyAsync(int departmentId, string userId, string occupancyId, int limit = CallLocationQuery.DefaultLimit)
		{
			if (string.IsNullOrWhiteSpace(occupancyId) || !await CanReadAsync(departmentId, userId))
				return new CallLocationHistoryResult();

			var occupancy = await _occupancies.Value.GetOccupancyLocationAsync(departmentId, occupancyId);
			if (occupancy == null)
				return new CallLocationHistoryResult();

			return await BuildAsync(departmentId, userId, new[] { occupancy.ToLocationQuery() }, occupancy.ContactIds, true, null, limit, true);
		}

		public async Task<CallLocationHistoryResult> GetHistoryAsync(int departmentId, string userId, CallLocationQuery query)
		{
			if (query == null)
				return new CallLocationHistoryResult();

			return await BuildAsync(departmentId, userId, new[] { query }, query.ContactIds, query.ContactCallsOnlyWithoutLocation, query.ExcludeCallId, query.Limit, query.IncludeNotes);
		}

		public async Task<Dictionary<string, int>> GetCallCountsForContactsAsync(int departmentId, string userId)
		{
			if (!await CanCountAsync(departmentId, userId))
				return null;

			try
			{
				return await _keys.GetCallCountsByContactAsync(departmentId);
			}
			catch (Exception ex)
			{
				// A list page must not fail over a count column.
				Logging.LogException(ex, $"Contact call counts failed for department {departmentId}; the column is left off.");
				return null;
			}
		}

		public async Task<Dictionary<string, CallLocationCount>> GetCallCountsForOccupanciesAsync(int departmentId, string userId, IEnumerable<string> occupancyIds)
		{
			if (!await CanCountAsync(departmentId, userId))
				return null;

			var result = new Dictionary<string, CallLocationCount>(StringComparer.Ordinal);
			var ids = (occupancyIds ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
			if (ids.Count == 0)
				return result;

			try
			{
				var addressMatching = await IsAddressMatchingAvailableAsync(departmentId);
				foreach (var pair in await _occupancies.Value.GetOccupancyLocationsAsync(departmentId, ids))
				{
					// Probe one past the cap so a bounded candidate window is never presented as an exact total.
					var candidatesTruncated = false;
					var matches = await GatherAsync(departmentId, new[] { pair.Value.ToLocationQuery() }, pair.Value.ContactIds, true, addressMatching,
						MaxOccupancyCountCandidates + 1, null, () => candidatesTruncated = true);
					result[pair.Key] = new CallLocationCount { Count = Math.Min(matches.Count, MaxOccupancyCountCandidates),
						IsLowerBound = candidatesTruncated || matches.Count > MaxOccupancyCountCandidates };
				}

				return result;
			}
			catch (Exception ex)
			{
				// A list page must not fail over a count column.
				Logging.LogException(ex, $"Occupancy call counts failed for department {departmentId}; the column is left off.");
				return null;
			}
		}

		private async Task<CallLocationHistoryResult> BuildAsync(int departmentId, string userId, IReadOnlyList<CallLocationQuery> locations, IReadOnlyCollection<string> contactIds,
			bool contactCallsOnlyWithoutLocation, int? excludeCallId, int limit, bool includeNotes)
		{
			var result = new CallLocationHistoryResult();
			if (!await CanReadAsync(departmentId, userId))
				return result;

			limit = Math.Clamp(limit, 1, CallLocationQuery.MaxLimit);
			result.AddressMatchingAvailable = await IsAddressMatchingAvailableAsync(departmentId);
			if (result.AddressMatchingAvailable)
			{
				var state = await _keys.GetStateAsync(departmentId);
				result.IndexComplete = state != null && !state.IsSuppressed && state.CompletedOn.HasValue && state.KeyVersion >= ParsedStreetAddress.Version;
			}

			string interpreted = null;
			var fetch = Math.Min(MaxCandidates, limit * 4 + 50);
			var matches = await GatherAsync(departmentId, locations, contactIds, contactCallsOnlyWithoutLocation, result.AddressMatchingAvailable, fetch, s => interpreted ??= s);
			result.InterpretedAddress = interpreted;
			if (excludeCallId.HasValue)
				matches.Remove(excludeCallId.Value);

			var ordered = matches.Values.OrderByDescending(m => m.LoggedOn).ThenByDescending(m => m.CallId).ToList();
			// Load a little past the limit so calls outside the user's dispatch scope do not leave the page short.
			var window = ordered.Take(limit * 2).ToList();
			var calls = await _keys.GetCallsAsync(departmentId, window.Select(m => m.CallId));
			calls = (await _dispatchScope.FilterCallsForUserAsync(departmentId, userId, calls)) ?? new List<Call>();
			var byId = calls.GroupBy(c => c.CallId).ToDictionary(g => g.Key, g => g.First());

			var entries = window.Where(m => byId.ContainsKey(m.CallId))
				.Select(m => new CallLocationHistoryEntry { Call = byId[m.CallId], Match = m.Match, DistanceMeters = m.DistanceMeters })
				.ToList();
			result.HasMore = entries.Count > limit || ordered.Count > window.Count;
			entries = entries.Take(limit).ToList();

			if (includeNotes && entries.Count > 0)
			{
				var notes = (await _keys.GetNotesForCallsAsync(entries.Select(e => e.Call.CallId)))
					.GroupBy(n => n.CallId).ToDictionary(g => g.Key, g => g.OrderBy(n => n.Timestamp).ToList());
				foreach (var entry in entries)
					if (notes.TryGetValue(entry.Call.CallId, out var list))
						entry.Notes = list;
			}

			result.Entries = entries;
			return result;
		}

		private sealed class MatchState
		{
			public int CallId;
			public DateTime LoggedOn;
			public CallLocationMatch Match;
			public double? DistanceMeters;
		}

		private async Task<Dictionary<int, MatchState>> GatherAsync(int departmentId, IReadOnlyList<CallLocationQuery> locations, IReadOnlyCollection<string> contactIds,
			bool contactCallsOnlyWithoutLocation, bool addressMatching, int fetch, Action<string> interpreted, Action candidatesTruncated = null)
		{
			var matches = new Dictionary<int, MatchState>();
			void Add(CallLocationCandidate row, CallLocationMatch match, double? meters)
			{
				if (!matches.TryGetValue(row.CallId, out var state))
					matches[row.CallId] = state = new MatchState { CallId = row.CallId, LoggedOn = row.LoggedOn };
				state.Match |= match;
				if (meters.HasValue && (!state.DistanceMeters.HasValue || meters < state.DistanceMeters))
					state.DistanceMeters = meters;
			}

			var parsedLocations = (locations ?? Array.Empty<CallLocationQuery>()).Where(l => l != null)
				.Select(l => new { Location = l, Street = addressMatching ? StreetAddressParser.Parse(l.Address, l.Locality, l.PostalCode) : null }).ToList();
			var addressKeys = parsedLocations.Where(l => l.Street?.HasStreetLocation == true && l.Street.IndexKey != null)
				.Select(l => l.Street.IndexKey).Distinct().ToList();
			var addressRows = addressKeys.Count == 0
				? new List<CallLocationCandidate>()
				: await _keys.GetByAddressKeysAsync(departmentId, addressKeys, fetch);
			var byAddress = addressRows.ToLookup(r => r.AddressKey);
			if (byAddress.Any(group => group.Count() >= fetch)) candidatesTruncated?.Invoke();
			var points = new List<GeoMath.GeoPoint>();
			foreach (var parsedLocation in parsedLocations)
			{
				var location = parsedLocation.Location;
				var point = location.Latitude.HasValue && location.Longitude.HasValue ? ValidPoint(new GeoMath.GeoPoint(location.Latitude.Value, location.Longitude.Value)) : null;
				if (point.HasValue)
					points.Add(point.Value);
				if (!addressMatching)
					continue;

				var parsed = parsedLocation.Street;
				var street = parsed?.HasStreetLocation == true ? parsed : null;
				if (street != null)
					interpreted?.Invoke(street.ToString());

				if (street?.IndexKey != null)
				{
					foreach (var row in byAddress[street.IndexKey])
					{
						var match = StreetAddressMatcher.Compare(street, ParsedStreetAddress.FromCanonical(row.AddressCanonical));
						if (match == StreetAddressMatch.None)
							continue;
						var meters = Meters(point, row);
						if (meters > GeoVetoMeters)
							continue;
						Add(row, match == StreetAddressMatch.Same ? CallLocationMatch.SameAddress : CallLocationMatch.SimilarAddress, meters);
					}
				}

				if (point.HasValue)
				{
					var radius = location.NearbyMeters > 0 ? Math.Min(location.NearbyMeters, MaxNearbyMeters) : CallLocationQuery.DefaultNearbyMeters;
					var (minLat, maxLat, minLng, maxLng) = Bounds(point.Value, radius);
					var nearbyRows = await _keys.GetWithinBoundsAsync(departmentId, minLat, maxLat, minLng, maxLng, fetch);
					if (nearbyRows.Count >= fetch) candidatesTruncated?.Invoke();
					foreach (var row in nearbyRows)
					{
						var meters = Meters(point, row);
						if (!meters.HasValue || meters > radius)
							continue;
						// Two different street addresses side by side are different places; proximity only decides when one side has none.
						var other = ParsedStreetAddress.FromCanonical(row.AddressCanonical);
						if (street != null && other?.HasStreetLocation == true && StreetAddressMatcher.Compare(street, other) == StreetAddressMatch.None)
							continue;
						Add(row, CallLocationMatch.Nearby, meters);
					}
				}
			}

			var contacts = (contactIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (contacts.Count > 0)
			{
				var contactRows = await _keys.GetContactCallCandidatesAsync(departmentId, contacts, fetch);
				if (contactRows.Count >= fetch) candidatesTruncated?.Invoke();
				foreach (var row in contactRows)
				{
					var hasOwnLocation = row.Indexed && (row.AddressKey != null || row.Latitude.HasValue);
					if (contactCallsOnlyWithoutLocation && addressMatching && hasOwnLocation)
					{
						// A call with its own location belongs here only if that location matched above (a multi-site
						// business contact's calls at its other sites stay out).
						if (matches.TryGetValue(row.CallId, out var existing))
							existing.Match |= CallLocationMatch.SameContact;
						continue;
					}

					double? meters = null;
					foreach (var p in points)
					{
						var m = Meters(p, row);
						if (m.HasValue && (!meters.HasValue || m < meters))
							meters = m;
					}
					Add(row, CallLocationMatch.SameContact, meters);
				}
			}

			// Report the strongest location reason only.
			foreach (var state in matches.Values)
			{
				if (state.Match.HasFlag(CallLocationMatch.SameAddress))
					state.Match &= ~(CallLocationMatch.SimilarAddress | CallLocationMatch.Nearby);
				else if (state.Match.HasFlag(CallLocationMatch.SimilarAddress))
					state.Match &= ~CallLocationMatch.Nearby;
			}

			return matches;
		}

		#endregion

		#region Helpers

		private async Task<bool> IsAddressMatchingAvailableAsync(int departmentId, bool bypassCache = false)
		{
			var policy = await _dataProtection.GetPolicyByDepartmentIdAsync(departmentId, bypassCache);
			return policy == null || policy.State == (int)DepartmentDataProtectionState.Disabled;
		}

		private async Task<bool> CanReadAsync(int departmentId, string userId)
			=> departmentId > 0 && !string.IsNullOrWhiteSpace(userId) && await _departments.IsMemberOfDepartmentAsync(departmentId, userId);

		/// <summary>Counts are not filtered call by call, so they are only shown to users who can see every call in the department.</summary>
		private async Task<bool> CanCountAsync(int departmentId, string userId)
			=> await CanReadAsync(departmentId, userId) && (await _dispatchScope.GetScopeForUserAsync(departmentId, userId))?.IsDepartmentWide == true;

		private static GeoMath.GeoPoint? ValidPoint(GeoMath.GeoPoint? point)
		{
			if (!point.HasValue)
				return null;
			var p = point.Value;
			if (!double.IsFinite(p.Latitude) || !double.IsFinite(p.Longitude) || Math.Abs(p.Latitude) > 90 || Math.Abs(p.Longitude) > 180 || (p.Latitude == 0 && p.Longitude == 0))
				return null;
			return p;
		}

		private static double? Meters(GeoMath.GeoPoint? point, CallLocationCandidate row)
		{
			if (!point.HasValue || !row.Latitude.HasValue || !row.Longitude.HasValue)
				return null;
			return GeoMath.HaversineMeters(point.Value.Latitude, point.Value.Longitude, (double)row.Latitude.Value, (double)row.Longitude.Value);
		}

		private static (decimal minLat, decimal maxLat, decimal minLng, decimal maxLng) Bounds(GeoMath.GeoPoint point, double meters)
		{
			const double metersPerDegree = 111320d;
			var dLat = meters / metersPerDegree;
			var cos = Math.Max(0.01, Math.Cos(point.Latitude * Math.PI / 180d));
			var dLng = meters / (metersPerDegree * cos);
			decimal Clamp(double value, double limit) => (decimal)Math.Round(Math.Max(-limit, Math.Min(limit, value)), 6);
			return (Clamp(point.Latitude - dLat, 90), Clamp(point.Latitude + dLat, 90), Clamp(point.Longitude - dLng, 180), Clamp(point.Longitude + dLng, 180));
		}

		#endregion
	}
}

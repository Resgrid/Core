using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;

namespace Resgrid.Services.Search
{
	public partial class UnifiedSearchService
	{
		/// <summary>The occupancy repository's page cap: a longer list of house-number candidates is treated as capped.</summary>
		private const int MaxOccupancyAddressCandidates = 500;

		private readonly ICallLocationKeysRepository _callLocationKeys;

		/// <summary>Street address hits for one query, strongest match first.</summary>
		private sealed class AddressMatches
		{
			public List<UnifiedSearchHit> Hits = new List<UnifiedSearchHit>();
			/// <summary>False when a candidate list was capped, a candidate could not be shown, or the walk stopped once the page was filled: no total can be proven then.</summary>
			public bool Complete = true;
		}

		/// <summary>
		/// Text that reads as a street address ("110 Main Street") is also compared the way occupancies compare addresses
		/// (<see cref="StreetAddressMatcher"/>), so it finds the call logged at "110 Main St" or "110 Main" and the occupancy at
		/// "110 Main St", which the word-by-word index match misses. Calls come from the call location index (its keys exist only
		/// while the department's protected text is in the clear); occupancies are narrowed by house number in the database.
		/// Every candidate still needs a live projection and passes its family's own authorization rule.
		/// </summary>
		private async Task<AddressMatches> MatchAddressesAsync(string text, IReadOnlyCollection<string> types, SearchAccess access, UnifiedSearchRequest request,
			int window, int needed, IReadOnlyList<string> snippetTerms, CancellationToken cancellationToken)
		{
			var matches = new AddressMatches();
			var calls = _callLocationKeys != null && access.ProtectedTextAllowed && types.Contains(SearchEntityTypes.Call);
			// WithoutClosedModulesAsync has already removed Occupancy when the module is closed for the caller.
			var occupancies = _occupancies != null && types.Contains(SearchEntityTypes.Occupancy);
			if (!calls && !occupancies)
				return matches;

			// Quotes mark a phrase for the index; they mean nothing to an address.
			var address = StreetAddressParser.Parse(text.Replace("\"", " "));
			if (address?.HasStreetLocation != true || address.IndexKey == null)
				return matches;

			var departmentId = access.Principal.DepartmentId;
			var candidates = new List<(string EntityType, string EntityId, StreetAddressMatch Match)>();
			if (calls)
			{
				var rows = await _callLocationKeys.GetByAddressKeysAsync(departmentId, new[] { address.IndexKey }, window) ?? new List<CallLocationCandidate>();
				if (rows.Count >= window)
					matches.Complete = false;
				foreach (var row in rows)
				{
					var match = StreetAddressMatcher.Compare(address, ParsedStreetAddress.FromCanonical(row.AddressCanonical));
					if (match != StreetAddressMatch.None)
						candidates.Add((SearchEntityTypes.Call, row.CallId.ToString(), match));
				}
			}

			if (occupancies && !address.IsIntersection)
			{
				var take = Math.Min(window, MaxOccupancyAddressCandidates);
				var rows = (await _occupancies.QueryAsync(departmentId, new RmsOccupancyQuery { Search = address.HouseNumber.Split(' ')[0], Take = take }))?.ToList()
					?? new List<RmsOccupancy>();
				if (rows.Count >= take)
					matches.Complete = false;
				foreach (var row in rows.Where(o => o != null))
				{
					var match = StreetAddressMatcher.Compare(address, StreetAddressParser.Parse(row.AddressText, row.City, row.PostalCode));
					if (match != StreetAddressMatch.None)
						candidates.Add((SearchEntityTypes.Occupancy, row.RmsOccupancyId, match));
				}
			}

			if (candidates.Count == 0)
				return matches;

			var projections = new Dictionary<(string, string), SearchProjection>();
			foreach (var family in candidates.GroupBy(c => c.EntityType))
				foreach (var projection in await _projections.GetByEntityIdsAsync(departmentId, family.Key, family.Select(c => c.EntityId)) ?? Enumerable.Empty<SearchProjection>())
					if (projection != null && projection.EntityType == family.Key)
						projections[(projection.EntityType, projection.EntityId)] = projection;

			// Same address before similar, newest first within each.
			var ordered = candidates
				.Select(c => (c.EntityType, c.EntityId, c.Match, Projection: projections.TryGetValue((c.EntityType, c.EntityId), out var p) ? p : null))
				.OrderByDescending(c => c.Match).ThenByDescending(c => c.Projection?.OccurredOn ?? DateTime.MinValue);
			foreach (var candidate in ordered)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!request.CountTotal && matches.Hits.Count >= needed)
				{
					matches.Complete = false;
					break;
				}
				if (candidate.Projection != null && !InRange(candidate.Projection.OccurredOn, request.FromUtc, request.ToUtc))
					continue;
				// AuthorizeAsync reads only the family and the id of a hit.
				if (ProjectionIsLive(candidate.Projection, access) &&
					await AuthorizeAsync(new GlobalSearchHit { EntityType = candidate.EntityType, EntityId = candidate.EntityId }, access))
					matches.Hits.Add(Map(candidate.Projection, 0f, snippetTerms));
				else
					matches.Complete = false;
			}

			return matches;
		}
	}
}

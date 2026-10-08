using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.Services
{
	/// <summary>In-memory DocumentNumberSequences with the repository's semantics; issued numbers per kind come from the test.</summary>
	public sealed class FakeDocumentNumberSequences : IDocumentNumberSequencesRepository
	{
		public Dictionary<(string Kind, string ScopeKey), DocumentNumberSequence> Rows { get; } = new Dictionary<(string, string), DocumentNumberSequence>();

		/// <summary>The numbers already written for a kind (built-in and custom), as the kind's number column holds them.</summary>
		public Func<string, IEnumerable<string>> Issued { get; set; } = kind => Enumerable.Empty<string>();

		public int SeedScans { get; private set; }

		public Task<DocumentNumberSequence> GetSequenceAsync(int departmentId, string kind, string scopeKey) =>
			Task.FromResult(Rows.TryGetValue((kind, scopeKey), out var row) ? row : null);

		public Task<int> TakeNextAsync(int departmentId, string kind, string scopeKey, int seed, CancellationToken cancellationToken = default)
		{
			if (!Rows.TryGetValue((kind, scopeKey), out var row))
				Rows[(kind, scopeKey)] = row = new DocumentNumberSequence { DepartmentId = departmentId, Kind = kind, ScopeKey = scopeKey, LastSequence = Math.Max(0, seed) + 1 };
			else
				row.LastSequence++;
			row.ModifiedOn = DateTime.UtcNow;
			return Task.FromResult(row.LastSequence);
		}

		public Task RaiseFloorAsync(int departmentId, string kind, string scopeKey, int nextSequence, string userId, DateTime now, CancellationToken cancellationToken = default)
		{
			if (!Rows.TryGetValue((kind, scopeKey), out var row))
				Rows[(kind, scopeKey)] = row = new DocumentNumberSequence { DepartmentId = departmentId, Kind = kind, ScopeKey = scopeKey, LastSequence = nextSequence - 1, ModifiedOn = now };
			row.LastSequence = Math.Max(row.LastSequence, nextSequence - 1);
			row.FloorSequence = Math.Max(row.FloorSequence, nextSequence);
			row.FloorSetByUserId = userId;
			row.FloorSetOn = now;
			return Task.CompletedTask;
		}

		public Task<int> GetHighestIssuedAsync(int departmentId, string kind, string numberPrefix, string numberSuffix)
		{
			SeedScans++;
			var highest = 0;
			foreach (var n in (Issued(kind) ?? Enumerable.Empty<string>()).Where(n => n != null))
			{
				// Case-insensitive like the citext and SQL Server collations the real columns use.
				if (n.Length <= numberPrefix.Length + numberSuffix.Length || !n.StartsWith(numberPrefix, StringComparison.OrdinalIgnoreCase) || !n.EndsWith(numberSuffix, StringComparison.OrdinalIgnoreCase))
					continue;
				var mid = n.Substring(numberPrefix.Length, n.Length - numberPrefix.Length - numberSuffix.Length);
				if (mid.All(char.IsDigit) && int.TryParse(mid, out var value))
					highest = Math.Max(highest, value);
			}
			return Task.FromResult(highest);
		}
	}
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Value-free ADP evidence. Never put PINs, tokens, reasons or protected content in these fields.</summary>
	public sealed class AdpAuditEvent
	{
		public string EventId { get; set; } = Guid.NewGuid().ToString("N");
		public int DepartmentId { get; set; }
		public long Sequence { get; set; }
		public string Layer { get; set; }
		public string Operation { get; set; }
		public string Outcome { get; set; }
		public string ActorId { get; set; }
		public string CorrelationId { get; set; }
		public string ResourceId { get; set; }
		public long PolicyEpoch { get; set; }
		public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
		public string PreviousHash { get; set; }
		public string Hash { get; set; }
	}

	public static class AdpAuditChain
	{
		public const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";

		public static string ComputeHash(AdpAuditEvent row)
		{
			// Explicit, versioned ordering and millisecond precision survive both database engines.
			var canonical = JsonConvert.SerializeObject(new object[] { 1, row.PreviousHash, row.EventId,
				row.DepartmentId, row.Sequence, row.Layer, row.Operation, row.Outcome, row.ActorId,
				row.CorrelationId, row.ResourceId, row.PolicyEpoch,
				row.OccurredUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture) });
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
		}

		public static void Link(AdpAuditEvent row, AdpAuditEvent tail)
		{
			row.OccurredUtc = new DateTime(row.OccurredUtc.Ticks - row.OccurredUtc.Ticks % TimeSpan.TicksPerMillisecond);
			row.Sequence = (tail?.Sequence ?? 0) + 1;
			row.PreviousHash = tail?.Hash ?? Genesis;
			row.Hash = ComputeHash(row);
		}

		/// <summary>Verify in stored sequence order; an external tail checkpoint also detects suffix removal.</summary>
		public static bool Verify(IEnumerable<AdpAuditEvent> rows, long expectedCount, string expectedTailHash) =>
			Walk(rows, 0, Genesis, out var sequence, out var previous) && sequence == expectedCount && previous == expectedTailHash;

		/// <summary>
		/// Verify one page that must continue the chain from an anchor the caller already holds (0 and
		/// <see cref="Genesis"/> for the first page, otherwise the previous page's tail sequence and hash).
		/// </summary>
		public static bool VerifySegment(IEnumerable<AdpAuditEvent> rows, long anchorSequence, string anchorHash) =>
			anchorSequence >= 0 && !string.IsNullOrEmpty(anchorHash) && Walk(rows, anchorSequence, anchorHash, out _, out _);

		private static bool Walk(IEnumerable<AdpAuditEvent> rows, long anchorSequence, string anchorHash, out long sequence, out string previous)
		{
			sequence = anchorSequence;
			previous = anchorHash;
			int? department = null;
			foreach (var row in rows)
			{
				department ??= row.DepartmentId;
				if (row.DepartmentId != department || row.Sequence != ++sequence || row.PreviousHash != previous ||
					row.Hash != ComputeHash(row)) return false;
				previous = row.Hash;
			}
			return true;
		}
	}
}

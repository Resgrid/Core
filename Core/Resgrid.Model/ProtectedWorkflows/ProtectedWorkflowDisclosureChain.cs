using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// The per-department hash chain over <see cref="ProtectedWorkflowDisclosure"/> rows. Pure functions, so the
	/// repository, the verifier and the tests all compute the exact same bytes.
	///
	/// Hash = lowercase hex SHA-256 over UTF-8 (PrevHash + canonical row). The canonical row is compact JSON with a
	/// FIXED property order covering every column except Hash itself; PrevHash of the first row is
	/// <see cref="GenesisHash"/>. OccurredOn is formatted to the millisecond with no zone conversion, because the
	/// value read back from the database has an unspecified Kind and PostgreSQL keeps only microseconds.
	/// </summary>
	public static class ProtectedWorkflowDisclosureChain
	{
		public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

		/// <summary>Truncates to the millisecond (the precision the hash is computed at).</summary>
		public static DateTime NormalizeTimestamp(DateTime value) =>
			new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

		public static string CanonicalRow(ProtectedWorkflowDisclosure row)
		{
			if (row == null)
				throw new ArgumentNullException(nameof(row));

			var json = new JObject
			{
				["id"] = row.ProtectedWorkflowDisclosureId,
				["departmentId"] = row.DepartmentId,
				["sequence"] = row.ChainSequence,
				["recordType"] = row.RecordType,
				["eventType"] = row.EventType,
				["actorUserId"] = row.ActorUserId,
				["workflowId"] = row.WorkflowId,
				["workflowRunId"] = row.WorkflowRunId,
				["workflowStepId"] = row.WorkflowStepId,
				["releaseId"] = row.WorkflowProtectedReleaseId,
				["entityType"] = row.EntityType,
				["entityId"] = row.EntityId,
				["fieldIds"] = row.FieldIds,
				["destinationHost"] = row.DestinationHost,
				["payloadSha256"] = row.PayloadSha256,
				["payloadBytes"] = row.PayloadBytes,
				["httpStatus"] = row.HttpStatus,
				["outcome"] = row.Outcome,
				["isTest"] = row.IsTest,
				["brokerRequestId"] = row.BrokerRequestId,
				["detail"] = row.Detail,
				["contentType"] = row.ContentType,
				["capturedKeys"] = row.CapturedKeys,
				["occurredOn"] = FormatTimestamp(row.OccurredOn)
			};

			return json.ToString(Formatting.None);
		}

		public static string ComputeHash(string prevHash, ProtectedWorkflowDisclosure row)
		{
			var input = (prevHash ?? string.Empty) + CanonicalRow(row);
			return ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
		}

		/// <summary>Stamps Sequence, PrevHash and Hash onto a new row that follows <paramref name="previous"/> (null for the first row).</summary>
		public static void Link(ProtectedWorkflowDisclosure row, ProtectedWorkflowDisclosure previous)
		{
			row.OccurredOn = NormalizeTimestamp(row.OccurredOn == default ? DateTime.UtcNow : row.OccurredOn);
			row.ChainSequence = (previous?.ChainSequence ?? 0) + 1;
			row.PrevHash = previous?.Hash ?? GenesisHash;
			row.Hash = ComputeHash(row.PrevHash, row);
		}

		/// <summary>
		/// Verifies a department's chain in sequence order. Returns the first sequence number that fails (a changed
		/// row, a broken link, a gap or a duplicate), or null when the whole chain verifies.
		/// </summary>
		public static long? Verify(IEnumerable<ProtectedWorkflowDisclosure> rows)
		{
			var expectedPrev = GenesisHash;
			long expectedSequence = 1;
			foreach (var row in (rows ?? Enumerable.Empty<ProtectedWorkflowDisclosure>()).OrderBy(r => r.ChainSequence))
			{
				if (row.ChainSequence != expectedSequence ||
					!string.Equals(row.PrevHash, expectedPrev, StringComparison.Ordinal) ||
					!string.Equals(row.Hash, ComputeHash(row.PrevHash, row), StringComparison.Ordinal))
					return row.ChainSequence;

				expectedPrev = row.Hash;
				expectedSequence++;
			}

			return null;
		}

		public static string Sha256Hex(byte[] data) => ToHex(SHA256.HashData(data ?? Array.Empty<byte>()));

		private static string FormatTimestamp(DateTime value) =>
			new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond)
				.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);

		private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
	}
}

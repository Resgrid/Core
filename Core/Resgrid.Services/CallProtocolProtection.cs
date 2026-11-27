using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// ADP catalog 39 for a protocol on a call (<see cref="ContentProtectedFields.CallProtocols"/>): the case record, the questionnaire in
	/// words and the override and removal reasons are caller content. Writes go through the workload encrypt-only lane (like the call's own
	/// net) and only those columns are written back; reads resolve for an attended reader with a grant and are REDACTED otherwise. Unattended
	/// paths never decrypt: they read the determinant, which stays plaintext.
	/// </summary>
	public static class CallProtocolProtection
	{
		/// <summary>True when a protected column holds plaintext (or a round-tripped placeholder) that still has to be enveloped.</summary>
		public static bool HasPlaintext(CallProtocol row) =>
			row != null && ContentProtectedFields.CallProtocols.Values.Any(a => !string.IsNullOrEmpty(a.Get(row)) && !ProtectedDataEnvelope.HasEnvelopePrefix(a.Get(row)));

		/// <summary>True when a protected column carries the REDACTED placeholder a reader without a grant was shown.</summary>
		public static bool HasPlaceholder(CallProtocol row) =>
			row != null && ContentProtectedFields.CallProtocols.Values.Any(a => a.Get(row) == ProtectedDataEnvelope.RedactionValue);

		/// <summary>True when a protected column is enveloped (the department's ADP sealed it).</summary>
		public static bool IsSealed(CallProtocol row) =>
			row != null && ContentProtectedFields.CallProtocols.Values.Any(a => ProtectedDataEnvelope.HasEnvelopePrefix(a.Get(row)));

		/// <summary>True when a protected column is enveloped or REDACTED: the case content cannot be read as it stands.</summary>
		public static bool IsWithheld(CallProtocol row) =>
			IsSealed(row) || HasPlaceholder(row);

		/// <summary>
		/// Envelopes the row's plaintext protected columns for a department that encrypts new writes (no-op otherwise) and writes them back
		/// alone. A REDACTED placeholder restores the stored value when <paramref name="existing"/> is given. Throws when the write is blocked
		/// (fails closed: plaintext is never left behind silently).
		/// </summary>
		public static async Task<bool> ProtectAsync(IProtectedWriteService write, ICallProtocolsRepository repository, int departmentId, CallProtocol row, CallProtocol existing,
			CancellationToken cancellationToken = default)
		{
			if (write == null || repository == null || row == null || row.CallProtocolId <= 0 || departmentId <= 0)
				return false;

			var result = await write.PrepareRecordsEntityWriteAsync(departmentId, row, existing, row.CallProtocolId.ToString(CultureInfo.InvariantCulture),
				ContentProtectedFields.CallProtocols, null, null, null, workloadCaller: true, cancellationToken);
			if (result != null && !result.Success)
				throw new InvalidOperationException($"Protected write blocked ({result.Reason}); call protocol {row.CallProtocolId} has transient plaintext pending re-encryption.");
			if ((result == null || !result.Changed) && existing == null)
				return false;

			await repository.UpdateProtectedContentAsync(departmentId, row, cancellationToken);
			return true;
		}

		/// <summary>
		/// Resolves the protected columns of the rows for an attended reader: plaintext with a valid grant, REDACTED otherwise (never an
		/// envelope). A broker fault redacts rather than failing the page.
		/// </summary>
		public static async Task<ProtectedReadResult> ResolveAsync(IProtectedReadService read, int departmentId, IEnumerable<CallProtocol> rows, string grantToken, string userId,
			CancellationToken cancellationToken = default)
		{
			var sealedRows = (rows ?? Enumerable.Empty<CallProtocol>()).Where(IsSealed).ToList();
			if (sealedRows.Count == 0)
				return new ProtectedReadResult();

			try
			{
				if (read != null)
					return await read.ResolveRecordsEntitiesForReadAsync(departmentId, sealedRows.Select(r => (r, r.CallProtocolId.ToString(CultureInfo.InvariantCulture))).ToList(),
						ContentProtectedFields.CallProtocols, grantToken, userId, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, $"Protected call protocol content could not be resolved for department {departmentId}; it is withheld.");
			}

			Redact(sealedRows);
			return new ProtectedReadResult { IsProtected = true, RedactedFields = ContentProtectedFields.CallProtocols.Keys.ToList() };
		}

		/// <summary>Replaces every enveloped protected column with the REDACTED placeholder (unattended readers, fault paths).</summary>
		public static void Redact(IEnumerable<CallProtocol> rows)
		{
			foreach (var row in (rows ?? Enumerable.Empty<CallProtocol>()).Where(r => r != null))
				foreach (var accessor in ContentProtectedFields.CallProtocols.Values)
					if (ProtectedDataEnvelope.HasEnvelopePrefix(accessor.Get(row)))
						accessor.Set(row, ProtectedDataEnvelope.RedactionValue);
		}
	}
}

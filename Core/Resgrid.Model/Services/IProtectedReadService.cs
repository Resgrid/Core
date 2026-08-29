using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Attended protected-read pipeline for calls (ADP plan sections 3.1 steps 7-9 and 7.1).
	/// For a protection-enforced department it validates the caller's Protected Data Grant, sends
	/// enveloped field values to the Protected Data Broker in ONE batch, and substitutes plaintext
	/// into the call instances; without a valid grant (or on any broker fault) every enveloped
	/// value becomes the exact REDACTED placeholder with its catalog field id reported — a client
	/// never receives ciphertext as content, and a fault never widens disclosure. Unprotected
	/// departments pass through untouched.
	///
	/// APP-TIER (web host) ONLY: the implementation depends on the broker client and is registered
	/// in web-host composition roots, never in ServicesModule — workers and unattended paths use
	/// the safe projections instead.
	/// </summary>
	public interface IProtectedReadService
	{
		/// <summary>
		/// Resolves a batch of calls for one attended read. The call instances are the per-request
		/// entities the controller fetched (mutated in place; Dapper/cache reads hand each request
		/// its own instances). Order of results matches the input order.
		/// </summary>
		Task<IReadOnlyList<ProtectedReadResult>> ResolveForReadAsync(int departmentId,
			IReadOnlyList<Call> calls, string grantToken, string userId, CancellationToken cancellationToken = default);

		/// <summary>Single-call convenience over the batch overload.</summary>
		Task<ProtectedReadResult> ResolveForReadAsync(int departmentId, Call call,
			string grantToken, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Resolves standalone call-note lists (text fields plus the latitude/longitude companion
		/// envelopes). Returns one batch-level outcome; the note instances are mutated in place.
		/// </summary>
		Task<ProtectedReadResult> ResolveNotesForReadAsync(int departmentId,
			IReadOnlyList<CallNote> notes, string grantToken, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Resolves standalone attachment lists. includeData additionally decrypts the rgdpb binary
		/// payload (base64 over the broker) — expensive, so only file-serving endpoints opt in; a
		/// redacted binary payload becomes null, never ciphertext bytes.
		/// </summary>
		Task<ProtectedReadResult> ResolveAttachmentsForReadAsync(int departmentId,
			IReadOnlyList<CallAttachment> attachments, string grantToken, string userId,
			bool includeData = false, CancellationToken cancellationToken = default);

		/// <summary>
		/// Resolves contact batches (all 21 cataloged text columns). The enveloped Image blob is
		/// always STRIPPED (nulled) on reads — no v4 endpoint serves it, and ciphertext bytes must
		/// never ride out through a serializer.
		/// </summary>
		Task<ProtectedReadResult> ResolveContactsForReadAsync(int departmentId,
			IReadOnlyList<Contact> contacts, string grantToken, string userId, CancellationToken cancellationToken = default);

		/// <summary>Resolves standalone contact-note lists (contactnotes.note).</summary>
		Task<ProtectedReadResult> ResolveContactNotesForReadAsync(int departmentId,
			IReadOnlyList<ContactNote> notes, string grantToken, string userId, CancellationToken cancellationToken = default);
	}
}

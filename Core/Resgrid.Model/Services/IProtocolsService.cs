using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IProtocolsService
	{
		/// <summary>
		/// The department's current protocols (deleted ones left out, disabled ones kept), each with its triggers, questions and
		/// answers, and its attachments' names and sizes but never their bytes. Loaded in a handful of queries for the whole
		/// department, not three per protocol. Same as <see cref="GetProtocolSummariesForDepartmentAsync"/>.
		/// </summary>
		Task<List<DispatchProtocol>> GetAllProtocolsForDepartmentAsync(int departmentId);

		/// <summary>
		/// The department's protocols without attachment bytes; questions, answers, triggers and attachment info batch loaded.
		/// Deleted protocols are left out; disabled ones are kept unless <paramref name="enabledOnly"/> (the apps' lists, the
		/// offline reference data and every attach path pass true).
		/// </summary>
		Task<List<DispatchProtocol>> GetProtocolSummariesForDepartmentAsync(int departmentId, bool enabledOnly = false);

		/// <summary>
		/// The department's enabled, not deleted protocols as the matcher needs them (triggers, questions and answers; no text,
		/// no attachments). Cached per department and dropped whenever a protocol is saved, disabled or deleted.
		/// </summary>
		Task<List<DispatchProtocol>> GetMatchingSetAsync(int departmentId, bool bypassCache = false);

		/// <summary>
		/// Of the given protocol ids, returns the ones that can be attached to one of the department's calls: the department's own,
		/// not deleted and not disabled, without duplicates. A protocol id that arrives from a client must pass through this (or
		/// <see cref="PrepareCallProtocolsAsync"/>) before it is attached to a call.
		/// </summary>
		Task<List<int>> GetProtocolIdsOwnedByDepartmentAsync(int departmentId, IEnumerable<int> protocolIds);

		/// <summary>
		/// The call a protocol is matched against, resolved for the department: the call type id from <paramref name="callTypeId"/>
		/// or, failing that, from the type name (no match, a blank name or a translated "No Type" label all mean "no type"), and
		/// the time to compare trigger windows with (the call's logged time, now when it has none).
		/// </summary>
		Task<ProtocolCallContext> BuildCallContextAsync(int departmentId, int priority, int? callTypeId, string callTypeName, DateTime? loggedOnUtc);

		/// <summary>
		/// Every enabled protocol of the department evaluated against the call: Active (a trigger matched, no questions), Pending (a
		/// trigger matched, questions to answer) or Inactive (nothing matched; it can still be picked by hand). The one matcher the
		/// web forms and the v4 API share.
		/// </summary>
		Task<List<ProtocolMatch>> GetProtocolsForCallAsync(int departmentId, ProtocolCallContext call);

		/// <summary>
		/// Builds the CallProtocol rows for the protocols a client asked to attach. The server decides: it refuses protocols that
		/// are another department's, deleted or disabled, or already on the call; it re-evaluates the triggers for the call's type,
		/// priority and logged time (a match is recorded as Trigger, anything else as Manual); it recomputes the score from the
		/// chosen answer ids; it sets Data from the protocol's own code; and it records the answers in words. When
		/// <paramref name="storeSnapshots"/> the text is stored once per content hash for "text as dispatched".
		/// </summary>
		Task<ProtocolAttachResult> PrepareCallProtocolsAsync(int departmentId, Call call, IEnumerable<ProtocolAttachRequest> requests, string userId,
			IEnumerable<CallProtocol> alreadyOnCall = null, bool storeSnapshots = true, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Takes a protocol off a call. The row stays, marked removed with who, when and why; returns null when the row is not that
		/// call's or the protocol is not the department's. Removing an already removed row changes nothing.
		/// </summary>
		Task<CallProtocol> RemoveProtocolFromCallAsync(int departmentId, int callId, int callProtocolId, string reason, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>The stored "text as dispatched" for the given content hashes, the department's only.</summary>
		Task<List<DispatchProtocolSnapshot>> GetSnapshotsAsync(int departmentId, IEnumerable<string> contentHashes);

		/// <summary>How many of the department's calls logged since <paramref name="sinceUtc"/> carry each protocol.</summary>
		Task<Dictionary<int, int>> GetUsageCountsAsync(int departmentId, DateTime sinceUtc);

		/// <summary>
		/// Follows a call type rename. Triggers match the type by id, so this only refreshes the name they display, and links
		/// triggers written before M0336 that still match by the old name. Returns how many triggers changed.
		/// </summary>
		Task<int> RenameCallTypeInTriggersAsync(int departmentId, string oldName, string newName, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Follows a call type rename for the type with <paramref name="callTypeId"/>: its triggers keep matching (by id) and show
		/// the new name. Returns how many triggers changed.
		/// </summary>
		Task<int> RenameCallTypeInTriggersAsync(int departmentId, int callTypeId, string oldName, string newName, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>The triggers (of protocols not deleted) that match a call type, by id or, for older triggers, by name.</summary>
		Task<List<ProtocolTriggerImpact>> GetTriggersForCallTypeAsync(int departmentId, int callTypeId, string callTypeName);

		/// <summary>The triggers (of protocols not deleted) that need a call priority.</summary>
		Task<List<ProtocolTriggerImpact>> GetTriggersForPriorityAsync(int departmentId, int priorityId);

		/// <summary>
		/// Saves a protocol. A new one (id 0) is inserted with its triggers, questions, answers and attachments. An existing one is
		/// updated in place by id (<see cref="UpdateProtocolAsync"/>).
		/// </summary>
		Task<DispatchProtocol> SaveProtocolAsync(DispatchProtocol protocol, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Updates an existing protocol in place. Its fields are copied from <paramref name="edited"/>; triggers, questions and
		/// answers are matched by id: rows with an id are changed, rows without one are added, and the protocol's rows the edit no
		/// longer has are removed, so ids stay stable for anything that recorded them. New attachments (id 0) are added and the
		/// ones in <paramref name="removeAttachmentIds"/> removed. Throws <see cref="UnauthorizedAccessException"/> when the edit
		/// names a child id of another protocol or the protocol is another department's or deleted.
		/// </summary>
		Task<DispatchProtocol> UpdateProtocolAsync(DispatchProtocol edited, IEnumerable<int> removeAttachmentIds, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Disables or enables a protocol (a disabled protocol is never suggested or attached). Null when it is gone.</summary>
		Task<DispatchProtocol> SetProtocolDisabledAsync(int protocolId, bool disabled, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// A protocol with its triggers, questions and answers, and its attachments' names and sizes (never their bytes). Deleted
		/// protocols are returned too: the calls that cite them keep showing them.
		/// </summary>
		Task<DispatchProtocol> GetProtocolByIdAsync(int id);

		/// <summary>The protocol's own row only (no children): enough for ownership and permission checks.</summary>
		Task<DispatchProtocol> GetProtocolHeaderByIdAsync(int id);

		/// <summary>Deletes (soft) the protocol.</summary>
		Task<bool> DeleteProtocol(int id, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Evaluates the protocols against a call by its priority, type and logged time.</summary>
		List<DispatchProtocol> ProcessTriggers(List<DispatchProtocol> protocols, Call call);

		/// <summary>An attachment with its bytes. Only for the download endpoints, which check the protocol's department.</summary>
		Task<DispatchProtocolAttachment> GetAttachmentByIdAsync(int protocolAttachmentId);

		/// <summary>
		/// An attachment with its bytes when it belongs to one of the department's protocols, else null (the one check every
		/// download endpoint uses).
		/// </summary>
		Task<DispatchProtocolAttachment> GetAttachmentForDepartmentAsync(int departmentId, int protocolAttachmentId);

		/// <summary>The triggers of the protocol that apply to the call (its priority, type and logged time).</summary>
		List<DispatchProtocolTrigger> DetermineActiveTriggers(DispatchProtocol protocol, Call call);

		/// <summary>Drops the department's cached matching set and offline reference data after a protocol change.</summary>
		Task InvalidateProtocolCachesAsync(int departmentId);
	}
}

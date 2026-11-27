using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// P2 distribution and acknowledgment (plan §5 P2; free unless noted). Acknowledgment follows the audience, not the publish
	/// event (R-P3): a version that requires it is assigned on publish (and on scheduling: a future version can be acknowledged from
	/// publish), and to every member who later enters its audience (worker 83 drains the shared ContentAudienceChanges queue into
	/// <see cref="ReevaluateMemberAsync"/>), due relative to that day; leaving withdraws an open row, a signed one is kept. A minor
	/// version carries a signed acknowledgment forward; a major re-assigns. Read-and-sign is an L1 signature (L2 when the department
	/// asks) bound to the version's content hash; offline acknowledgments are accepted only for the version still in force (R-P11).
	/// Every completed acknowledgment writes a learning record. Automation addresses active members only.
	/// </summary>
	public class ProtocolAcknowledgmentService : IProtocolAcknowledgmentService
	{
		public const int ReminderBatch = 500;
		public const int QuizBatch = 500;

		private readonly IProtocolAcknowledgmentsRepository _acks;
		private readonly IProtocolVersionsRepository _versions;
		private readonly IProtocolVersionFilesRepository _files;
		private readonly IProtocolAudiencesRepository _audiences;
		private readonly IProtocolVersionApprovalsRepository _approvals;
		private readonly IDispatchProtocolRepository _protocolRows;
		private readonly IProtocolsService _protocolsService;
		private readonly IContentAudienceService _audienceService;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly ISignatureService _signatures;
		private readonly ILmsAccessService _lms;
		private readonly IProtocolNotificationService _notifications;
		private readonly IEventAggregator _eventAggregator;
		private readonly Lazy<IProtocolLibraryService> _library;
		private readonly Lazy<ILearningRecordWriter> _learningRecords;
		private readonly Lazy<ITrainingService> _trainings;
		private readonly Lazy<ITrainingAssignmentService> _trainingAssignments;
		private readonly Lazy<ISearchProjectionService> _searchProjections;
		// Lazy: the LMS learning extensions complete a waiting acknowledgment through this service when a bank quiz is passed.
		private readonly Lazy<IProtocolLearningExtensions> _learning;

		public ProtocolAcknowledgmentService(IProtocolAcknowledgmentsRepository acks, IProtocolVersionsRepository versions, IProtocolVersionFilesRepository files,
			IProtocolAudiencesRepository audiences, IProtocolVersionApprovalsRepository approvals, IDispatchProtocolRepository protocolRows, IProtocolsService protocolsService,
			IContentAudienceService audienceService, IDepartmentsService departments, IDepartmentGroupsService groups, ISignatureService signatures, ILmsAccessService lms,
			IProtocolNotificationService notifications, IEventAggregator eventAggregator, Lazy<IProtocolLibraryService> library, Lazy<ILearningRecordWriter> learningRecords,
			Lazy<ITrainingService> trainings, Lazy<ITrainingAssignmentService> trainingAssignments, Lazy<ISearchProjectionService> searchProjections,
			Lazy<IProtocolLearningExtensions> learning)
		{
			_learning = learning;
			_acks = acks;
			_versions = versions;
			_files = files;
			_audiences = audiences;
			_approvals = approvals;
			_protocolRows = protocolRows;
			_protocolsService = protocolsService;
			_audienceService = audienceService;
			_departments = departments;
			_groups = groups;
			_signatures = signatures;
			_lms = lms;
			_notifications = notifications;
			_eventAggregator = eventAggregator;
			_library = library;
			_learningRecords = learningRecords;
			_trainings = trainings;
			_trainingAssignments = trainingAssignments;
			_searchProjections = searchProjections;
		}

		#region Assignment

		public async Task<int> AssignVersionAsync(DispatchProtocol protocol, ProtocolVersion version, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (protocol == null || version == null || !version.RequiresAcknowledgment || !ProtocolAcknowledgmentRules.IsAcknowledgeable(protocol, version))
				return 0;

			var members = await AudienceMembersAsync(protocol);
			var source = protocol.IsBulletin ? ProtocolAcknowledgmentSources.Bulletin : ProtocolAcknowledgmentSources.Publish;
			var (assigned, _, newlyOpen) = await ReconcileVersionAsync(protocol, version, members, source, utcNow, false, cancellationToken);

			if (newlyOpen.Count > 0)
			{
				await NotifySafelyAsync(() => _notifications.NotifyAssignedAsync(protocol.DepartmentId, newlyOpen, protocol, version, cancellationToken));
				await AssignQuizAsync(protocol, version, newlyOpen, utcNow, cancellationToken);
			}

			return assigned;
		}

		public async Task<int> OnVersionActivatedAsync(DispatchProtocol protocol, ProtocolVersion version, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (protocol == null || version == null)
				return 0;

			// The member owes the version in force now, not an earlier one they had not signed yet.
			await _acks.SupersedeOpenAsync(protocol.DepartmentId, protocol.DispatchProtocolId, version.ProtocolVersionId, utcNow, cancellationToken);
			return await AssignVersionAsync(protocol, version, utcNow, cancellationToken);
		}

		public async Task<int> OnProtocolRetiredAsync(DispatchProtocol protocol, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (protocol == null)
				return 0;
			return await _acks.WithdrawOpenForProtocolAsync(protocol.DepartmentId, protocol.DispatchProtocolId, utcNow, cancellationToken);
		}

		public async Task<Tuple<int, int>> ReevaluateMemberAsync(int departmentId, string userId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return Tuple.Create(0, 0);

			var facts = await _audienceService.GetMemberFactsAsync(departmentId, userId);
			var rows = await _acks.GetForUserAsync(departmentId, userId) ?? new List<ProtocolAcknowledgment>();
			var withdrawn = 0;

			// Inactive-member policy: a member who left, was disabled or hidden owes nothing; open rows go, signed rows stay.
			if (facts == null || !facts.IsActive)
			{
				foreach (var open in rows.Where(r => r.IsOpen))
					if (await WithdrawAsync(open, utcNow, cancellationToken))
						withdrawn++;
				return Tuple.Create(0, withdrawn);
			}

			var added = 0;
			var addedVersions = new List<(DispatchProtocol Protocol, ProtocolVersion Version)>();
			var audiences = (await _audiences.GetForDepartmentAsync(departmentId) ?? new List<ProtocolAudience>()).ToLookup(a => a.DispatchProtocolId);
			foreach (var (protocol, versions) in await AcknowledgeableVersionsAsync(departmentId))
			{
				var rules = audiences[protocol.DispatchProtocolId].ToList();
				var inAudience = rules.Count == 0 || ContentAudienceMatcher.Matches(facts, rules);
				if (inAudience)
				{
					var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { userId };
					foreach (var version in versions)
					{
						var (assigned, _, newlyOpen) = await ReconcileVersionAsync(protocol, version, members, ProtocolAcknowledgmentSources.AudienceEntry, utcNow, false, cancellationToken,
							rows.Where(r => r.ProtocolVersionId == version.ProtocolVersionId).ToList());
						added += newlyOpen.Count;
						if (newlyOpen.Count > 0)
							addedVersions.Add((protocol, version));
					}
				}
				else
				{
					// Left the audience: open rows of this protocol are withdrawn; a signed one is kept.
					foreach (var open in rows.Where(r => r.DispatchProtocolId == protocol.DispatchProtocolId && r.IsOpen))
						if (await WithdrawAsync(open, utcNow, cancellationToken))
							withdrawn++;
				}
			}

			if (added > 0)
			{
				// The onboarding bundle: one notice for everything the member entered at once.
				if (addedVersions.Count == 1)
					await NotifySafelyAsync(() => _notifications.NotifyAssignedAsync(departmentId, new[] { userId }, addedVersions[0].Protocol, addedVersions[0].Version, cancellationToken));
				else
					await NotifySafelyAsync(() => _notifications.NotifyBundleAsync(departmentId, userId, addedVersions.Count, cancellationToken));

				foreach (var (protocol, version) in addedVersions.Where(v => v.Version.QuizTrainingId.HasValue))
					await AssignQuizAsync(protocol, version, new[] { userId }, utcNow, cancellationToken);
			}

			return Tuple.Create(added, withdrawn);
		}

		public async Task<Tuple<int, int>> ReevaluateDepartmentAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var added = 0;
			var withdrawn = 0;
			if (departmentId <= 0)
				return Tuple.Create(0, 0);

			foreach (var (protocol, versions) in await AcknowledgeableVersionsAsync(departmentId))
			{
				var members = await AudienceMembersAsync(protocol);
				foreach (var version in versions)
				{
					var (_, removed, newlyOpen) = await ReconcileVersionAsync(protocol, version, members, ProtocolAcknowledgmentSources.AudienceEntry, utcNow, true, cancellationToken);
					added += newlyOpen.Count;
					withdrawn += removed;
					if (newlyOpen.Count > 0)
					{
						await NotifySafelyAsync(() => _notifications.NotifyAssignedAsync(departmentId, newlyOpen, protocol, version, cancellationToken));
						await AssignQuizAsync(protocol, version, newlyOpen, utcNow, cancellationToken);
					}
				}
			}

			return Tuple.Create(added, withdrawn);
		}

		public async Task<Tuple<int, int>> ReevaluateProtocolAsync(int departmentId, int protocolId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(protocolId);
			if (protocol == null || protocol.DepartmentId != departmentId || protocol.IsDeleted)
				return Tuple.Create(0, 0);

			var added = 0;
			var withdrawn = 0;
			var members = await AudienceMembersAsync(protocol);
			foreach (var version in await AcknowledgeableVersionsOfAsync(protocol))
			{
				var (_, removed, newlyOpen) = await ReconcileVersionAsync(protocol, version, members, ProtocolAcknowledgmentSources.AudienceEntry, utcNow, true, cancellationToken);
				added += newlyOpen.Count;
				withdrawn += removed;
				if (newlyOpen.Count > 0)
				{
					await NotifySafelyAsync(() => _notifications.NotifyAssignedAsync(departmentId, newlyOpen, protocol, version, cancellationToken));
					await AssignQuizAsync(protocol, version, newlyOpen, utcNow, cancellationToken);
				}
			}

			return Tuple.Create(added, withdrawn);
		}

		/// <summary>
		/// Brings one version's rows in line with its audience: members who owe it and have no row get one (or carry a signed minor
		/// forward, or reopen a withdrawn row); with <paramref name="withdrawOthers"/>, open rows of members no longer in it are withdrawn.
		/// Returns (rows written, rows withdrawn, members newly owing it).
		/// </summary>
		private async Task<(int Assigned, int Withdrawn, List<string> NewlyOpen)> ReconcileVersionAsync(DispatchProtocol protocol, ProtocolVersion version, ISet<string> members,
			ProtocolAcknowledgmentSources source, DateTime utcNow, bool withdrawOthers, CancellationToken cancellationToken, List<ProtocolAcknowledgment> knownRows = null)
		{
			var newlyOpen = new List<string>();
			var assigned = 0;
			var withdrawn = 0;
			if (!version.RequiresAcknowledgment)
				return (0, 0, newlyOpen);

			var rows = knownRows ?? await _acks.GetForVersionAsync(protocol.DepartmentId, version.ProtocolVersionId) ?? new List<ProtocolAcknowledgment>();
			var byUser = rows.GroupBy(r => r.UserId ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

			var previous = await PreviousVersionAsync(protocol, version);
			var previousRows = previous == null || !ProtocolAcknowledgmentRules.IsMinorOf(version, previous)
				? new Dictionary<string, ProtocolAcknowledgment>(StringComparer.OrdinalIgnoreCase)
				: (await _acks.GetForVersionAsync(protocol.DepartmentId, previous.ProtocolVersionId) ?? new List<ProtocolAcknowledgment>())
					.Where(r => r.Status == (int)ProtocolAcknowledgmentStatuses.Acknowledged)
					.GroupBy(r => r.UserId ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

			var dueDays = ProtocolAcknowledgmentRules.DueDays(protocol.AckDueDays);
			var effective = version.Status == (int)ProtocolVersionStatuses.Approved ? version.EffectiveOn : null;
			var dueOn = ProtocolAcknowledgmentRules.DueOn(utcNow, dueDays, effective, protocol.IsBulletin ? version.ExpiresOn : null);

			foreach (var userId in members.OrderBy(u => u, StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();
				byUser.TryGetValue(userId, out var existing);
				previousRows.TryGetValue(userId, out var previousAck);

				switch (ProtocolAcknowledgmentRules.Plan(version, existing, previous, previousAck))
				{
					case ProtocolAcknowledgmentPlanActions.Assign:
						var assignment = new ProtocolAcknowledgment
						{
							DepartmentId = protocol.DepartmentId,
							DispatchProtocolId = protocol.DispatchProtocolId,
							ProtocolVersionId = version.ProtocolVersionId,
							UserId = userId,
							Status = (int)ProtocolAcknowledgmentStatuses.Open,
							Source = (int)source,
							AssignedOn = utcNow,
							DueOn = dueOn,
							EnteredAudienceOn = source == ProtocolAcknowledgmentSources.AudienceEntry ? utcNow : (DateTime?)null,
							BundleOrder = protocol.AckBundleOrder,
							QuizTrainingId = version.QuizTrainingId
						};
						if (await TryInsertAsync(assignment, cancellationToken))
						{
							assigned++;
							newlyOpen.Add(userId);
							// Once per row: the unique (version, member) index lets only one assignment path insert it.
							RaiseWorkflow(assignment, protocol, version, ProtocolAcknowledgmentWorkflowChanges.Assigned, utcNow);
						}
						break;

					case ProtocolAcknowledgmentPlanActions.CarryForward:
						// A minor version: the member's signature on the version before it stands (it stays bound to that version's hash).
						if (await TryInsertAsync(new ProtocolAcknowledgment
						{
							DepartmentId = protocol.DepartmentId,
							DispatchProtocolId = protocol.DispatchProtocolId,
							ProtocolVersionId = version.ProtocolVersionId,
							UserId = userId,
							Status = (int)ProtocolAcknowledgmentStatuses.Acknowledged,
							Source = (int)ProtocolAcknowledgmentSources.CarriedForward,
							AssignedOn = utcNow,
							DueOn = null,
							BundleOrder = protocol.AckBundleOrder,
							OpenedOn = previousAck.OpenedOn,
							SignedOn = previousAck.SignedOn,
							SignatureRecordId = previousAck.SignatureRecordId,
							CompletedOn = utcNow,
							QuizTrainingId = previousAck.QuizTrainingId,
							CarriedFromAcknowledgmentId = previousAck.ProtocolAcknowledgmentId
						}, cancellationToken))
							assigned++;
						break;

					case ProtocolAcknowledgmentPlanActions.Reopen:
						existing.Status = (int)ProtocolAcknowledgmentStatuses.Open;
						existing.Source = (int)source;
						existing.WithdrawnOn = null;
						existing.DueOn = dueOn;
						existing.EnteredAudienceOn = utcNow;
						existing.ReminderStage = 0;
						if (await _acks.TryUpdateStatusAsync(existing, (int)ProtocolAcknowledgmentStatuses.Withdrawn, cancellationToken))
						{
							assigned++;
							newlyOpen.Add(userId);
							RaiseWorkflow(existing, protocol, version, ProtocolAcknowledgmentWorkflowChanges.Assigned, utcNow);
						}
						break;
				}
			}

			if (withdrawOthers)
			{
				foreach (var open in rows.Where(r => r.IsOpen && !members.Contains(r.UserId ?? string.Empty)))
					if (await WithdrawAsync(open, utcNow, cancellationToken))
						withdrawn++;
			}

			return (assigned, withdrawn, newlyOpen);
		}

		private async Task<bool> TryInsertAsync(ProtocolAcknowledgment row, CancellationToken cancellationToken)
		{
			try
			{
				await _acks.InsertAsync(row, cancellationToken, true);
				return true;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				// Two assignment paths at once (publish and worker 83): the unique (version, member) index lets one row in.
				Logging.LogDebug($"Acknowledgment of version {row.ProtocolVersionId} for {row.UserId} was not inserted: {ex.Message}");
				return false;
			}
		}

		private async Task<bool> WithdrawAsync(ProtocolAcknowledgment row, DateTime utcNow, CancellationToken cancellationToken)
		{
			row.Status = (int)ProtocolAcknowledgmentStatuses.Withdrawn;
			row.WithdrawnOn = utcNow;
			return await _acks.TryUpdateStatusAsync(row, (int)ProtocolAcknowledgmentStatuses.Open, cancellationToken);
		}

		/// <summary>The active members a protocol's audience resolves to (no rules = the whole department).</summary>
		private async Task<HashSet<string>> AudienceMembersAsync(DispatchProtocol protocol)
		{
			var rules = (await _audiences.GetForProtocolAsync(protocol.DepartmentId, protocol.DispatchProtocolId) ?? new List<ProtocolAudience>())
				.Where(a => a.DepartmentId == protocol.DepartmentId).Cast<IContentAudienceRule>().ToList();
			if (rules.Count == 0)
				rules.Add(new ContentAudienceRule(ContentAudienceKinds.Everyone));

			var resolution = await _audienceService.ResolveAsync(protocol.DepartmentId, rules);
			return new HashSet<string>(resolution?.MemberUserIds ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Every protocol of the department with versions that can be acknowledged and need it: the version in force, and an approved
		/// version scheduled for later (acknowledgeable from publish).
		/// </summary>
		private async Task<List<(DispatchProtocol Protocol, List<ProtocolVersion> Versions)>> AcknowledgeableVersionsAsync(int departmentId)
		{
			var result = new List<(DispatchProtocol, List<ProtocolVersion>)>();
			var protocols = (await _protocolsService.GetProtocolSummariesForDepartmentAsync(departmentId) ?? new List<DispatchProtocol>())
				.Where(p => p.DepartmentId == departmentId && !p.IsDeleted).ToList();
			if (protocols.Count == 0)
				return result;

			var headers = (await _versions.GetHeadersForDepartmentAsync(departmentId) ?? new List<ProtocolVersion>())
				.Where(v => v.RequiresAcknowledgment).ToLookup(v => v.DispatchProtocolId);
			foreach (var protocol in protocols)
			{
				var versions = headers[protocol.DispatchProtocolId].Where(v => ProtocolAcknowledgmentRules.IsAcknowledgeable(protocol, v)).ToList();
				if (versions.Count > 0)
					result.Add((protocol, versions));
			}

			return result;
		}

		private async Task<List<ProtocolVersion>> AcknowledgeableVersionsOfAsync(DispatchProtocol protocol) =>
			(await _versions.GetForProtocolAsync(protocol.DepartmentId, protocol.DispatchProtocolId) ?? new List<ProtocolVersion>())
				.Where(v => v.RequiresAcknowledgment && ProtocolAcknowledgmentRules.IsAcknowledgeable(protocol, v)).ToList();

		/// <summary>The numbered version before <paramref name="version"/> (the one a minor carries acknowledgments from).</summary>
		private async Task<ProtocolVersion> PreviousVersionAsync(DispatchProtocol protocol, ProtocolVersion version)
		{
			if (version.Major <= 0)
				return null;
			return (await _versions.GetForProtocolAsync(protocol.DepartmentId, protocol.DispatchProtocolId) ?? new List<ProtocolVersion>())
				.Where(v => v.ProtocolVersionId != version.ProtocolVersionId && v.Major > 0 &&
					(v.Status == (int)ProtocolVersionStatuses.Published || v.Status == (int)ProtocolVersionStatuses.Superseded || v.Status == (int)ProtocolVersionStatuses.Retired) &&
					(v.Major < version.Major || (v.Major == version.Major && v.Minor < version.Minor)))
				.OrderByDescending(v => v.Major).ThenByDescending(v => v.Minor).FirstOrDefault();
		}

		/// <summary>Quiz-gated acknowledgment: the members who owe the version are assigned its baseline training (free).</summary>
		private async Task AssignQuizAsync(DispatchProtocol protocol, ProtocolVersion version, IReadOnlyCollection<string> userIds, DateTime utcNow, CancellationToken cancellationToken)
		{
			if (!version.QuizTrainingId.HasValue || userIds == null || userIds.Count == 0)
				return;

			try
			{
				var training = await _trainings.Value.GetTrainingByIdAsync(version.QuizTrainingId.Value);
				if (training == null || training.DepartmentId != protocol.DepartmentId || training.IsArchived)
					return;
				await _trainingAssignments.Value.AssignAsync(training, userIds, null, TrainingAssignmentSources.Manual, null, utcNow, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				// The acknowledgment stands; the member can still be assigned the training by an officer.
				Logging.LogException(ex, $"Quiz training {version.QuizTrainingId} could not be assigned for protocol version {version.ProtocolVersionId}.");
			}
		}

		private static async Task NotifySafelyAsync(Func<Task<int>> send)
		{
			try
			{
				await send();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Protocol acknowledgment notification failed.");
			}
		}

		#endregion Assignment

		#region Read-and-sign

		public async Task<ProtocolAcknowledgment> MarkOpenedAsync(int departmentId, string userId, int protocolVersionId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var row = await _acks.GetForVersionAndUserAsync(departmentId, protocolVersionId, userId);
			if (row == null || row.DepartmentId != departmentId)
				return null;

			if (!row.OpenedOn.HasValue)
			{
				row.OpenedOn = utcNow;
				await _acks.TryUpdateStatusAsync(row, row.Status, cancellationToken);
			}

			return row;
		}

		public Task<ProtocolAcknowledgment> GetForVersionAndUserAsync(int departmentId, int protocolVersionId, string userId) =>
			_acks.GetForVersionAndUserAsync(departmentId, protocolVersionId, userId);

		public async Task<ProtocolAcknowledgeResult> AcknowledgeAsync(ProtocolReader reader, int protocolVersionId, string contentHashRead, SignatureProof proof, bool offline,
			string statementText = null, CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.IsCurrentMember || reader.DepartmentId <= 0 || proof == null ||
				!string.Equals(proof.UserId, reader.UserId, StringComparison.OrdinalIgnoreCase))
				return ProtocolAcknowledgeResult.Of(ProtocolAcknowledgeOutcomes.InvalidRequest);

			var version = await _versions.GetByIdForDepartmentAsync(reader.DepartmentId, protocolVersionId);
			var protocol = version == null ? null : await _protocolsService.GetProtocolHeaderByIdAsync(version.DispatchProtocolId);
			if (version == null || protocol == null || protocol.DepartmentId != reader.DepartmentId || version.DepartmentId != reader.DepartmentId)
				return ProtocolAcknowledgeResult.Of(ProtocolAcknowledgeOutcomes.InvalidRequest);

			ProtocolAcknowledgeResult Newer() => new ProtocolAcknowledgeResult
			{
				Outcome = ProtocolAcknowledgeOutcomes.ReadNewerVersion,
				CurrentVersionId = protocol.IsPublished ? protocol.CurrentVersionId : null
			};

			var row = await _acks.GetForVersionAndUserAsync(reader.DepartmentId, protocolVersionId, reader.UserId);
			if (row == null)
				return ProtocolAcknowledgmentRules.IsAcknowledgeable(protocol, version) ? ProtocolAcknowledgeResult.Of(ProtocolAcknowledgeOutcomes.NotAssigned) : Newer();

			if (row.IsSigned)
				return new ProtocolAcknowledgeResult { Outcome = ProtocolAcknowledgeOutcomes.AlreadyAcknowledged, Acknowledgment = row };

			if (row.Status == (int)ProtocolAcknowledgmentStatuses.Superseded)
				return Newer();
			if (row.Status != (int)ProtocolAcknowledgmentStatuses.Open)
				return ProtocolAcknowledgeResult.Of(ProtocolAcknowledgeOutcomes.NotAssigned);

			if (offline)
			{
				// R-P11: accepted only for the version still in force, read with this very content.
				var check = ProtocolAcknowledgmentRules.CheckOffline(protocol, version, contentHashRead);
				if (check == ProtocolAcknowledgeOutcomes.ReadNewerVersion)
					return Newer();
				if (check != ProtocolAcknowledgeOutcomes.Acknowledged)
					return new ProtocolAcknowledgeResult { Outcome = check, CurrentVersionId = protocol.CurrentVersionId, CurrentContentHash = version.ContentHash };
			}
			else
			{
				// The web requires the member to have opened the version before signing it.
				if (!row.OpenedOn.HasValue)
					return ProtocolAcknowledgeResult.Of(ProtocolAcknowledgeOutcomes.NotOpened);
				if (!ProtocolAcknowledgmentRules.IsAcknowledgeable(protocol, version))
					return Newer();
				if (!string.IsNullOrWhiteSpace(contentHashRead) && !string.Equals(contentHashRead.Trim(), version.ContentHash, StringComparison.OrdinalIgnoreCase))
					return new ProtocolAcknowledgeResult { Outcome = ProtocolAcknowledgeOutcomes.ContentChanged, CurrentContentHash = version.ContentHash };
			}

			// The signature binds to the content as stored now (a changed text or a swapped file never gets signed).
			var current = await ProtocolVersionHashing.CurrentHashAsync(version, _files);
			if (!string.Equals(current, version.ContentHash, StringComparison.Ordinal))
				return ProtocolAcknowledgeResult.Of(ProtocolAcknowledgeOutcomes.ContentChanged);

			var settings = await _library.Value.GetSettingsAsync(reader.DepartmentId);
			var signed = await _signatures.SignAsync(new SignatureRequest
			{
				DepartmentId = reader.DepartmentId,
				Use = SignatureUse.ReadAndSign,
				SubjectId = version.ProtocolVersionId.ToString(CultureInfo.InvariantCulture),
				RevisionId = "ack-" + row.ProtocolAcknowledgmentId.ToString(CultureInfo.InvariantCulture),
				ArtifactSha256 = version.ContentHash,
				StatementText = string.IsNullOrWhiteSpace(statementText) ? ProtocolSignatureStatements.ReadAndSign(protocol, version) : statementText,
				DepartmentLevel = settings.ReadAndSignRequiresStepUp ? (int)SignatureLevels.StepUp : (int?)null
			}, proof, cancellationToken);

			if (!signed.Succeeded)
				return ProtocolAcknowledgeResult.Of(signed.Outcome == SignatureOutcomes.StepUpRequired || signed.Outcome == SignatureOutcomes.PasskeyRequired
					? ProtocolAcknowledgeOutcomes.StepUpRequired
					: signed.Outcome == SignatureOutcomes.ArtifactMismatch ? ProtocolAcknowledgeOutcomes.ContentChanged : ProtocolAcknowledgeOutcomes.InvalidRequest);

			var now = DateTime.UtcNow;
			var quizPassed = row.QuizTrainingId.HasValue && await QuizPassedAsync(reader.DepartmentId, row.QuizTrainingId.Value, reader.UserId);
			// LMS (§17.2): a version whose protocol asks for a question-bank quiz completes only when that quiz is passed. Asked only while
			// the add-on is active (fails closed: without it the signature completes as in the free tier).
			var bank = await BankQuizAsync(reader.DepartmentId, protocol.DispatchProtocolId, version.ProtocolVersionId, reader.UserId);
			var bankRequired = bank.Configured && await _lms.CanAuthorAndManageAsync(reader.DepartmentId);
			var status = ProtocolAcknowledgmentRules.StatusAfterSigning(row.QuizTrainingId, quizPassed, bankRequired, bank.Passed);
			row.OpenedOn ??= now;
			row.SignedOn = now;
			row.SignatureRecordId = signed.Record.SignatureRecordId;
			row.Status = (int)status;
			row.CompletedOn = status == ProtocolAcknowledgmentStatuses.Acknowledged ? now : (DateTime?)null;
			row.ViaOffline = offline;

			if (!await _acks.TryUpdateStatusAsync(row, (int)ProtocolAcknowledgmentStatuses.Open, cancellationToken))
			{
				// Signed twice at once (web and an offline replay): the other one stands; this signature is voided, not left dangling.
				await _signatures.VoidAsync(reader.DepartmentId, signed.Record.SignatureRecordId, reader.UserId, "The acknowledgment was signed by another request first.", cancellationToken);
				var stored = await _acks.GetForVersionAndUserAsync(reader.DepartmentId, protocolVersionId, reader.UserId);
				return stored != null && stored.IsSigned
					? new ProtocolAcknowledgeResult { Outcome = ProtocolAcknowledgeOutcomes.AlreadyAcknowledged, Acknowledgment = stored }
					: ProtocolAcknowledgeResult.Of(ProtocolAcknowledgeOutcomes.InvalidRequest);
			}

			if (status == ProtocolAcknowledgmentStatuses.Acknowledged)
			{
				await WriteLearningRecordAsync(protocol, version, row, cancellationToken);
				// Signed and complete (a quiz still owed completes later, from CompleteAwaitingAsync).
				RaiseWorkflow(row, protocol, version, ProtocolAcknowledgmentWorkflowChanges.Completed, now);
			}

			Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolAcknowledged, JsonConvert.SerializeObject(new
			{
				ProtocolId = protocol.DispatchProtocolId,
				VersionId = version.ProtocolVersionId,
				AcknowledgmentId = row.ProtocolAcknowledgmentId,
				Status = status.ToString(),
				Offline = offline,
				SignatureId = signed.Record.SignatureRecordId
			}));

			return new ProtocolAcknowledgeResult
			{
				Outcome = status == ProtocolAcknowledgmentStatuses.Acknowledged ? ProtocolAcknowledgeOutcomes.Acknowledged : ProtocolAcknowledgeOutcomes.AwaitingQuiz,
				Acknowledgment = row
			};
		}

		/// <summary>The linked baseline training is passed (T0 Passed: the latest attempt's stored result), not merely complete.</summary>
		private async Task<bool> QuizPassedAsync(int departmentId, int trainingId, string userId)
		{
			try
			{
				var training = await _trainings.Value.GetTrainingByIdAsync(trainingId);
				if (training == null || training.DepartmentId != departmentId)
					return false;
				var rows = await _trainings.Value.GetTrainingUsersForUserAsync(userId) ?? new List<TrainingUser>();
				return rows.Any(r => r != null && r.TrainingId == trainingId && r.Passed == true && !r.RemovedOn.HasValue);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Quiz result for training {trainingId} could not be read.");
				return false;
			}
		}

		/// <summary>
		/// The member's state on the version's LMS question-bank quiz (database only; no billing call). Unknown is "not configured": a failed
		/// read never blocks a signature and never completes one either (the worker asks again).
		/// </summary>
		private async Task<ProtocolBankQuizState> BankQuizAsync(int departmentId, int protocolId, int protocolVersionId, string userId)
		{
			try
			{
				return await _learning.Value.GetBankQuizStateAsync(departmentId, protocolId, protocolVersionId, userId) ?? ProtocolBankQuizState.None;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Bank quiz state for protocol version {protocolVersionId} could not be read.");
				return ProtocolBankQuizState.None;
			}
		}

		/// <summary>Whether every quiz a waiting acknowledgment asks for is now passed: the linked training and, when configured, the bank quiz.</summary>
		private async Task<bool> QuizzesPassedAsync(ProtocolAcknowledgment row)
		{
			if (row.QuizTrainingId.HasValue && !await QuizPassedAsync(row.DepartmentId, row.QuizTrainingId.Value, row.UserId))
				return false;
			var bank = await _learning.Value.GetBankQuizStateAsync(row.DepartmentId, row.DispatchProtocolId, row.ProtocolVersionId, row.UserId) ?? ProtocolBankQuizState.None;
			return !bank.Configured || bank.Passed;
		}

		/// <summary>Moves a waiting acknowledgment to Acknowledged (compare-and-set on its status) and writes the transcript record.</summary>
		private async Task<bool> CompleteAwaitingAsync(ProtocolAcknowledgment row, DateTime utcNow, CancellationToken cancellationToken)
		{
			row.Status = (int)ProtocolAcknowledgmentStatuses.Acknowledged;
			row.CompletedOn = utcNow;
			if (!await _acks.TryUpdateStatusAsync(row, (int)ProtocolAcknowledgmentStatuses.AwaitingQuiz, cancellationToken))
				return false;

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(row.DispatchProtocolId);
			var version = await _versions.GetByIdForDepartmentAsync(row.DepartmentId, row.ProtocolVersionId);
			if (protocol != null && version != null && protocol.DepartmentId == row.DepartmentId)
				await WriteLearningRecordAsync(protocol, version, row, cancellationToken);
			// The status compare-and-set above let only this call complete it.
			RaiseWorkflow(row, protocol != null && protocol.DepartmentId == row.DepartmentId ? protocol : null, version, ProtocolAcknowledgmentWorkflowChanges.Completed, utcNow);
			return true;
		}

		/// <summary>Workflow triggers 251-253 for one acknowledgment row: ids, the protocol's code and name and dates; never fatal.</summary>
		private void RaiseWorkflow(ProtocolAcknowledgment row, DispatchProtocol protocol, ProtocolVersion version, ProtocolAcknowledgmentWorkflowChanges change, DateTime utcNow,
			ProtocolAcknowledgmentReminderStages? stage = null)
		{
			if (row == null || row.DepartmentId <= 0 || string.IsNullOrWhiteSpace(row.UserId))
				return;

			var evt = ProtocolAcknowledgmentWorkflowEvent.For(row, protocol, version, change, utcNow);
			if (change == ProtocolAcknowledgmentWorkflowChanges.Overdue)
			{
				evt.ReminderStage = (stage ?? ProtocolAcknowledgmentReminderStages.Overdue).ToString();
				evt.DaysOverdue = row.DueOn.HasValue ? Math.Max(0, (int)Math.Floor((utcNow - row.DueOn.Value).TotalDays)) : (int?)null;
			}

			ContentWorkflowContext.Publish(_eventAggregator, evt);
		}

		public async Task<bool> CompleteIfQuizPassedAsync(int departmentId, int protocolVersionId, string userId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return false;
			var row = await _acks.GetForVersionAndUserAsync(departmentId, protocolVersionId, userId);
			if (row == null || row.DepartmentId != departmentId || row.Status != (int)ProtocolAcknowledgmentStatuses.AwaitingQuiz)
				return false;
			if (!await QuizzesPassedAsync(row))
				return false;
			return await CompleteAwaitingAsync(row, utcNow, cancellationToken);
		}

		/// <summary>Every learning-relevant acknowledgment reaches the transcript (T1's writer; source ProtocolAcknowledgment). Never fatal.</summary>
		private async Task WriteLearningRecordAsync(DispatchProtocol protocol, ProtocolVersion version, ProtocolAcknowledgment row, CancellationToken cancellationToken)
		{
			if (protocol.IsBulletin)
				return; // a bulletin is a notice, not learning

			try
			{
				var label = string.IsNullOrWhiteSpace(version.Label) ? string.Empty : " v" + version.Label;
				await _learningRecords.Value.WriteSourceAsync(protocol.DepartmentId, LearningRecordSourceKinds.ProtocolAcknowledgment,
					row.ProtocolAcknowledgmentId.ToString(CultureInfo.InvariantCulture), new[]
					{
						new LearningRecordDraft
						{
							UserId = row.UserId,
							Title = ("Read and signed: " + (protocol.Code + " " + protocol.Name).Trim() + label).Trim(),
							CourseCode = protocol.Number ?? protocol.Code,
							CompletedOn = row.CompletedOn ?? row.SignedOn ?? DateTime.UtcNow,
							Hours = 0,
							Tags = protocol.StandardsTags,
							Status = LearningRecordStatuses.Verified
						}
					}, row.UserId, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Learning record for protocol acknowledgment {row.ProtocolAcknowledgmentId} could not be written.");
			}
		}

		public async Task<List<ProtocolAcknowledgmentItem>> GetMyAcknowledgmentsAsync(int departmentId, string userId, bool includeCompleted, DateTime utcNow)
		{
			var rows = (await _acks.GetForUserAsync(departmentId, userId) ?? new List<ProtocolAcknowledgment>())
				.Where(r => r.DepartmentId == departmentId && (ProtocolAcknowledgmentRules.IsOutstanding(r) || (includeCompleted && r.Status == (int)ProtocolAcknowledgmentStatuses.Acknowledged)))
				.ToList();
			return await ItemsAsync(departmentId, userId, rows, utcNow);
		}

		public async Task<List<ProtocolAcknowledgmentItem>> GetMustReadAsync(int departmentId, string userId, DateTime utcNow)
		{
			var rows = await _acks.GetOpenBulletinsForUserAsync(departmentId, userId) ?? new List<ProtocolAcknowledgment>();
			return (await ItemsAsync(departmentId, userId, rows, utcNow)).Where(i => i.IsBulletin && (!i.ExpiresOn.HasValue || i.ExpiresOn.Value > utcNow)).ToList();
		}

		private async Task<List<ProtocolAcknowledgmentItem>> ItemsAsync(int departmentId, string userId, List<ProtocolAcknowledgment> rows, DateTime utcNow)
		{
			var items = new List<ProtocolAcknowledgmentItem>();
			if (rows.Count == 0)
				return items;

			var versions = (await _versions.GetByIdsAsync(departmentId, rows.Select(r => r.ProtocolVersionId)) ?? new List<ProtocolVersion>()).ToDictionary(v => v.ProtocolVersionId);
			var protocols = new Dictionary<int, DispatchProtocol>();
			var trainingRows = rows.Any(r => r.QuizTrainingId.HasValue) ? await _trainings.Value.GetTrainingUsersForUserAsync(userId) ?? new List<TrainingUser>() : new List<TrainingUser>();
			var trainingNames = new Dictionary<int, string>();

			foreach (var row in rows)
			{
				if (!versions.TryGetValue(row.ProtocolVersionId, out var version))
					continue;
				if (!protocols.TryGetValue(row.DispatchProtocolId, out var protocol))
				{
					protocol = await _protocolsService.GetProtocolHeaderByIdAsync(row.DispatchProtocolId);
					protocols[row.DispatchProtocolId] = protocol;
				}
				if (protocol == null || protocol.DepartmentId != departmentId || protocol.IsDeleted)
					continue;

				string trainingName = null;
				if (row.QuizTrainingId.HasValue && !trainingNames.TryGetValue(row.QuizTrainingId.Value, out trainingName))
				{
					var training = await _trainings.Value.GetTrainingByIdAsync(row.QuizTrainingId.Value);
					trainingName = training != null && training.DepartmentId == departmentId ? training.Name : null;
					trainingNames[row.QuizTrainingId.Value] = trainingName;
				}

				var quizPassed = row.QuizTrainingId.HasValue && trainingRows.Any(t => t.TrainingId == row.QuizTrainingId.Value && t.Passed == true && !t.RemovedOn.HasValue);
				items.Add(new ProtocolAcknowledgmentItem
				{
					// Waiting for something other than the linked training: the LMS question-bank quiz.
					BankQuizOwed = row.Status == (int)ProtocolAcknowledgmentStatuses.AwaitingQuiz && (!row.QuizTrainingId.HasValue || quizPassed),
					Acknowledgment = row,
					ProtocolId = protocol.DispatchProtocolId,
					Code = protocol.Code,
					Name = protocol.Name,
					Kind = protocol.Kind,
					IsBulletin = protocol.IsBulletin,
					VersionLabel = version.Label,
					ContentHash = version.ContentHash,
					EffectiveOn = version.EffectiveOn,
					ExpiresOn = version.ExpiresOn,
					IsOverdue = ProtocolAcknowledgmentRules.IsOverdue(row, utcNow),
					QuizTrainingId = row.QuizTrainingId,
					QuizTrainingName = trainingName,
					QuizPassed = quizPassed
				});
			}

			// Outstanding first, bulletins first among them, then the onboarding bundle's order, then due date.
			return items
				.OrderBy(i => ProtocolAcknowledgmentRules.IsOutstanding(i.Acknowledgment) ? 0 : 1)
				.ThenBy(i => i.IsBulletin ? 0 : 1)
				.ThenBy(i => i.Acknowledgment.BundleOrder)
				.ThenBy(i => i.Acknowledgment.DueOn ?? DateTime.MaxValue)
				.ThenBy(i => i.Code, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		#endregion Read-and-sign

		#region Officers

		private static bool CanManage(ProtocolReader reader) =>
			reader != null && reader.IsCurrentMember && (reader.IsDepartmentAdmin || reader.CanManageAcknowledgments || reader.CanApprove);

		public async Task<ProtocolComplianceGrid> GetComplianceAsync(ProtocolReader reader, int? protocolId, int? protocolVersionId, int? groupId, DateTime utcNow)
		{
			var grid = new ProtocolComplianceGrid { GroupId = groupId };
			if (!CanManage(reader))
				return grid;

			var departmentId = reader.DepartmentId;
			var groupOf = await _groups.GetGroupIdsForAllUsersInDepartmentAsync(departmentId) ?? new Dictionary<string, int>();
			var groupOfUser = new Dictionary<string, int>(groupOf, StringComparer.OrdinalIgnoreCase);
			bool InGroup(string userId) => !groupId.HasValue || (groupOfUser.TryGetValue(userId ?? string.Empty, out var g) && g == groupId.Value);

			if (!protocolId.HasValue)
			{
				// The overview: one line per protocol with a version in force that needs acknowledgment.
				var all = await _acks.GetForDepartmentAsync(departmentId) ?? new List<ProtocolAcknowledgment>();
				var byVersion = all.Where(a => InGroup(a.UserId)).ToLookup(a => a.ProtocolVersionId);
				var headers = (await _versions.GetHeadersForDepartmentAsync(departmentId) ?? new List<ProtocolVersion>()).ToDictionary(v => v.ProtocolVersionId);
				foreach (var protocol in (await _protocolsService.GetProtocolSummariesForDepartmentAsync(departmentId) ?? new List<DispatchProtocol>())
					.Where(p => p.DepartmentId == departmentId && !p.IsDeleted && p.IsPublished).OrderBy(p => p.Code, StringComparer.OrdinalIgnoreCase))
				{
					if (!headers.TryGetValue(protocol.CurrentVersionId.Value, out var current) || !current.RequiresAcknowledgment)
						continue;
					var rows = byVersion[current.ProtocolVersionId].Where(r => r.Status != (int)ProtocolAcknowledgmentStatuses.Withdrawn && r.Status != (int)ProtocolAcknowledgmentStatuses.Superseded).ToList();
					grid.Summaries.Add(new ProtocolComplianceSummary
					{
						ProtocolId = protocol.DispatchProtocolId,
						Code = protocol.Code,
						Name = protocol.Name,
						Kind = protocol.Kind,
						VersionId = current.ProtocolVersionId,
						VersionLabel = current.Label,
						Assigned = rows.Count,
						Acknowledged = rows.Count(ProtocolAcknowledgmentRules.IsComplete),
						Overdue = rows.Count(r => ProtocolAcknowledgmentRules.IsOverdue(r, utcNow)),
						StandardsTags = protocol.StandardsTags
					});
				}

				return grid;
			}

			var target = await _protocolsService.GetProtocolHeaderByIdAsync(protocolId.Value);
			if (target == null || target.DepartmentId != departmentId)
				return grid;

			grid.Protocol = target;
			grid.Versions = (await _versions.GetForProtocolAsync(departmentId, target.DispatchProtocolId) ?? new List<ProtocolVersion>())
				.Where(v => v.Major > 0 && v.RequiresAcknowledgment).OrderByDescending(v => v.Major).ThenByDescending(v => v.Minor).ToList();
			grid.Version = protocolVersionId.HasValue
				? grid.Versions.FirstOrDefault(v => v.ProtocolVersionId == protocolVersionId.Value)
				: grid.Versions.FirstOrDefault(v => v.ProtocolVersionId == target.CurrentVersionId) ?? grid.Versions.FirstOrDefault();
			if (grid.Version == null)
				return grid;

			var names = (await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
				.Where(n => n != null && !string.IsNullOrWhiteSpace(n.UserId))
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name?.Trim(), StringComparer.OrdinalIgnoreCase);
			var groups = (await _groups.GetAllGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>()).ToDictionary(g => g.DepartmentGroupId, g => g.Name);

			foreach (var row in (await _acks.GetForVersionAsync(departmentId, grid.Version.ProtocolVersionId) ?? new List<ProtocolAcknowledgment>()).Where(r => InGroup(r.UserId)))
			{
				var group = groupOfUser.TryGetValue(row.UserId ?? string.Empty, out var gid) ? gid : (int?)null;
				grid.Rows.Add(new ProtocolComplianceRow
				{
					UserId = row.UserId,
					Name = names.TryGetValue(row.UserId ?? string.Empty, out var name) && !string.IsNullOrWhiteSpace(name) ? name : row.UserId,
					GroupId = group,
					GroupName = group.HasValue && groups.TryGetValue(group.Value, out var groupName) ? groupName : null,
					Acknowledgment = row,
					StateKey = StateKey(row, utcNow)
				});
			}

			grid.Rows = grid.Rows.OrderBy(r => StateOrder(r.StateKey)).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
			return grid;
		}

		public static string StateKey(ProtocolAcknowledgment row, DateTime utcNow)
		{
			switch ((ProtocolAcknowledgmentStatuses)row.Status)
			{
				case ProtocolAcknowledgmentStatuses.Acknowledged: return "acknowledged";
				case ProtocolAcknowledgmentStatuses.AwaitingQuiz: return ProtocolAcknowledgmentRules.IsOverdue(row, utcNow) ? "overdue" : "awaiting_quiz";
				case ProtocolAcknowledgmentStatuses.Withdrawn: return "withdrawn";
				case ProtocolAcknowledgmentStatuses.Superseded: return "superseded";
				default: return ProtocolAcknowledgmentRules.IsOverdue(row, utcNow) ? "overdue" : "open";
			}
		}

		private static int StateOrder(string key) => key switch
		{
			"overdue" => 0,
			"open" => 1,
			"awaiting_quiz" => 2,
			"acknowledged" => 3,
			_ => 4
		};

		public string ToCsv(ProtocolComplianceGrid grid)
		{
			var builder = new StringBuilder();
			if (grid == null)
				return string.Empty;

			if (grid.Protocol == null)
			{
				builder.AppendLine("Code,Name,Version,Assigned,Acknowledged,Overdue,Percent,Standards");
				foreach (var s in grid.Summaries)
					builder.AppendLine(string.Join(",", Csv(s.Code), Csv(s.Name), Csv(s.VersionLabel), s.Assigned.ToString(CultureInfo.InvariantCulture),
						s.Acknowledged.ToString(CultureInfo.InvariantCulture), s.Overdue.ToString(CultureInfo.InvariantCulture), s.Percent.ToString(CultureInfo.InvariantCulture), Csv(s.StandardsTags)));
				return builder.ToString();
			}

			builder.AppendLine("Protocol,Version,Member,Group,State,Assigned (UTC),Due (UTC),Opened (UTC),Signed (UTC),Completed (UTC),Offline");
			foreach (var r in grid.Rows)
			{
				var a = r.Acknowledgment;
				builder.AppendLine(string.Join(",", Csv(grid.Protocol.Code + " " + grid.Protocol.Name), Csv(grid.Version?.Label), Csv(r.Name), Csv(r.GroupName), Csv(r.StateKey),
					Stamp(a.AssignedOn), Stamp(a.DueOn), Stamp(a.OpenedOn), Stamp(a.SignedOn), Stamp(a.CompletedOn), a.ViaOffline ? "yes" : "no"));
			}

			return builder.ToString();
		}

		private static string Stamp(DateTime? value) => value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : string.Empty;

		/// <summary>A CSV cell: quoted, inner quotes doubled, and formula-leading characters neutralized (CSV injection).</summary>
		public static string Csv(string value)
		{
			var text = value ?? string.Empty;
			if (text.Length > 0 && "=+-@\t\r".IndexOf(text[0]) >= 0)
				text = "'" + text;
			return "\"" + text.Replace("\"", "\"\"") + "\"";
		}

		public async Task<ProtocolLifecycleResult> SaveSettingsAsync(ProtocolReader reader, int protocolId, int? dueDays, int bundleOrder, int? quizTrainingId, string standardsTags,
			CancellationToken cancellationToken = default)
		{
			if (!CanManage(reader) && !(reader?.MayAuthor ?? false))
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotAuthorized);

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(protocolId);
			if (protocol == null || protocol.DepartmentId != reader.DepartmentId || protocol.IsDeleted)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotFound);

			if (quizTrainingId.HasValue)
			{
				var training = await _trainings.Value.GetTrainingByIdAsync(quizTrainingId.Value);
				// Only a baseline training of this department with a quiz can gate an acknowledgment.
				if (training == null || training.DepartmentId != reader.DepartmentId || training.IsArchived || !training.HasQuiz)
					return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.InvalidInput);
			}

			var before = JsonConvert.SerializeObject(new { protocol.AckDueDays, protocol.AckBundleOrder, protocol.AckTrainingId, protocol.StandardsTags });
			protocol.AckDueDays = dueDays.HasValue && dueDays.Value > 0 ? Math.Min(dueDays.Value, ProtocolAcknowledgmentRules.MaxDueDays) : (int?)null;
			protocol.AckBundleOrder = Math.Clamp(bundleOrder, -10000, 10000);
			protocol.AckTrainingId = quizTrainingId;
			// Standards mapping is the LMS add-on's (§17.2); without it the tags are left as they are (fails closed).
			if (await _lms.CanAuthorAndManageAsync(reader.DepartmentId))
				protocol.StandardsTags = ProtocolAcknowledgmentRules.NormalizeTags(standardsTags);
			protocol.RowVersion += 1;
			protocol.UpdatedOn = DateTime.UtcNow;
			protocol.UpdatedByUserId = reader.UserId;
			await _protocolRows.SaveOrUpdateAsync(protocol, cancellationToken, true);
			await _protocolsService.InvalidateProtocolCachesAsync(reader.DepartmentId);

			Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolAcknowledgmentSettingsChanged,
				JsonConvert.SerializeObject(new { ProtocolId = protocolId, Before = before, protocol.AckDueDays, protocol.AckBundleOrder, protocol.AckTrainingId, protocol.StandardsTags }));
			return new ProtocolLifecycleResult { Protocol = protocol };
		}

		public async Task<ProtocolStandardsReport> GetStandardsReportAsync(ProtocolReader reader, DateTime utcNow)
		{
			if (!CanManage(reader) || !await _lms.CanAuthorAndManageAsync(reader.DepartmentId))
				return null;

			var overview = await GetComplianceAsync(reader, null, null, null, utcNow);
			var report = new ProtocolStandardsReport { GeneratedOn = utcNow };
			var tagged = overview.Summaries.Where(s => !string.IsNullOrWhiteSpace(s.StandardsTags))
				.SelectMany(s => s.StandardsTags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).Select(t => (Tag: t, Summary: s)))
				.GroupBy(x => x.Tag, StringComparer.OrdinalIgnoreCase)
				.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

			foreach (var group in tagged)
			{
				var section = new ProtocolStandardsSection { Standard = group.Key };
				foreach (var (_, summary) in group)
				{
					section.Protocols.Add(summary);
					// Proof: the approvals of the version in force that still verify against the content as stored now.
					if (summary.VersionId.HasValue)
						foreach (var verification in await _signatures.GetVerifiedForSubjectAsync(reader.DepartmentId, SignatureSubjectKinds.ProtocolVersion,
							summary.VersionId.Value.ToString(CultureInfo.InvariantCulture)) ?? new List<SignatureVerification>())
							if (verification?.IsValid == true)
								section.VerifiedApprovals++;
				}
				report.Sections.Add(section);
			}

			return report;
		}

		#endregion Officers

		#region Priority bulletins

		public async Task<ProtocolLifecycleResult> IssueBulletinAsync(ProtocolReader reader, ProtocolBulletinRequest request, CancellationToken cancellationToken = default)
		{
			// Any holder of the publish right (Approve Protocols, or a department admin) may issue one.
			if (reader == null || !reader.MayApprove)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotAuthorized);

			var now = DateTime.UtcNow;
			var title = request?.Title?.Trim();
			// The composer posts plain text (paragraphs by line); rich text from the API is sanitized for storage like any protocol.
			var source = request?.ContentHtml ?? string.Empty;
			if (!StringHelpers.LooksLikeHtml(source))
				source = string.Concat(source.Replace("\r\n", "\n").Split('\n').Where(l => !string.IsNullOrWhiteSpace(l))
					.Select(l => "<p>" + System.Net.WebUtility.HtmlEncode(l.Trim()) + "</p>"));
			var html = StringHelpers.SanitizeHtmlForStorage(source);
			if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(StringHelpers.HtmlToPlainText(html ?? string.Empty)))
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.EmptyContent);
			if (StringHelpers.HtmlToPlainText(html).Length > ProtocolAcknowledgmentRules.BulletinTextMaxLength)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.InvalidInput);

			var expires = DateTime.SpecifyKind(request.ExpiresOnUtc, DateTimeKind.Utc);
			if (expires <= now.AddMinutes(5) || expires > now.AddDays(ProtocolAcknowledgmentRules.MaxBulletinDays))
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.InvalidInput);

			var created = await _library.Value.CreateAsync(new ProtocolCreateRequest
			{
				DepartmentId = reader.DepartmentId,
				UserId = reader.UserId,
				ContentHtml = html,
				RequiresAcknowledgment = request.MustRead,
				Audience = request.Audience ?? new List<ContentAudienceRule>(),
				Protocol = new DispatchProtocol
				{
					Name = title.Length > ProtocolAcknowledgmentRules.BulletinTitleMaxLength ? title.Substring(0, ProtocolAcknowledgmentRules.BulletinTitleMaxLength) : title,
					Code = "BUL-" + now.ToString("yyMMddHHmm", CultureInfo.InvariantCulture),
					// Its own number: a bulletin never takes one from the department's protocol numbering.
					Number = "BUL-" + now.ToString("yyMMddHHmm", CultureInfo.InvariantCulture),
					Kind = (int)ProtocolKinds.Bulletin,
					Triggers = new List<DispatchProtocolTrigger>(),
					Questions = new List<DispatchProtocolQuestion>(),
					Attachments = new List<DispatchProtocolAttachment>()
				}
			}, cancellationToken);
			if (!created.Succeeded)
				return created;

			var protocol = created.Protocol;
			var version = created.Version;

			// Straight into force (R-P7): the version is published now, numbered 1.0, expiring on its own; the approval is recorded
			// afterwards as one pending countersign step.
			var expected = version.RowVersion;
			version.Status = (int)ProtocolVersionStatuses.Published;
			version.Major = 1;
			version.Minor = 0;
			version.EffectiveOn = now;
			version.ExpiresOn = expires;
			version.PublishedOn = now;
			version.PublishedByUserId = reader.UserId;
			version.SubmittedOn = now;
			version.SubmittedByUserId = reader.UserId;
			version.ChangeSummary = "Priority bulletin";
			version.RequiresAcknowledgment = request.MustRead;
			if (!await _versions.TryUpdateAsync(version, expected, cancellationToken))
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.StaleCopy);

			await _approvals.InsertAsync(new ProtocolVersionApproval
			{
				DepartmentId = reader.DepartmentId,
				ProtocolVersionId = version.ProtocolVersionId,
				Round = 1,
				StepOrder = 1,
				Decision = (int)ProtocolApprovalDecisions.Pending,
				CreatedOn = now
			}, cancellationToken, true);

			var fresh = await _protocolsService.GetProtocolHeaderByIdAsync(protocol.DispatchProtocolId) ?? protocol;
			fresh.Status = (int)ProtocolStatuses.Published;
			fresh.CurrentVersionId = version.ProtocolVersionId;
			fresh.ProtocolText = version.ContentHtml;
			fresh.LastReviewedOn = now;
			fresh.NextReviewOn = null;
			fresh.RowVersion += 1;
			fresh.UpdatedOn = now;
			fresh.UpdatedByUserId = reader.UserId;
			await _protocolRows.SaveOrUpdateAsync(fresh, cancellationToken, true);
			await _protocolsService.InvalidateProtocolCachesAsync(reader.DepartmentId);
			if (_searchProjections?.Value != null)
				await _searchProjections.Value.ProjectProtocolAsync(fresh, cancellationToken);

			Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolBulletinIssued, JsonConvert.SerializeObject(new
			{
				ProtocolId = fresh.DispatchProtocolId,
				VersionId = version.ProtocolVersionId,
				fresh.Name,
				ExpiresOn = expires,
				request.MustRead
			}));
			ContentWorkflowContext.Publish(_eventAggregator, ProtocolWorkflowEvent.For(fresh, version, ProtocolWorkflowChanges.BulletinIssued, reader.UserId, now));

			// Must-read: everyone in the audience owes it now, and hears about it by push (non-must-read: the push alone).
			if (request.MustRead)
			{
				await AssignVersionAsync(fresh, version, now, cancellationToken);
			}
			else
			{
				var members = await AudienceMembersAsync(fresh);
				await NotifySafelyAsync(() => _notifications.NotifyAssignedAsync(reader.DepartmentId, members.ToList(), fresh, version, cancellationToken));
			}

			return new ProtocolLifecycleResult { Protocol = fresh, Version = version };
		}

		public async Task<List<ProtocolBulletinItem>> GetBulletinsAsync(ProtocolReader reader, DateTime utcNow)
		{
			var items = new List<ProtocolBulletinItem>();
			if (reader == null || !reader.IsManager)
				return items;

			var bulletins = (await _protocolsService.GetProtocolSummariesForDepartmentAsync(reader.DepartmentId) ?? new List<DispatchProtocol>())
				.Where(p => p.DepartmentId == reader.DepartmentId && p.IsBulletin && !p.IsDeleted && p.CurrentVersionId.HasValue)
				.OrderByDescending(p => p.CreatedOn).Take(100).ToList();
			if (bulletins.Count == 0)
				return items;

			var versions = (await _versions.GetByIdsAsync(reader.DepartmentId, bulletins.Select(b => b.CurrentVersionId.Value)) ?? new List<ProtocolVersion>()).ToDictionary(v => v.ProtocolVersionId);
			var steps = (await _approvals.GetForVersionsAsync(reader.DepartmentId, versions.Keys) ?? new List<ProtocolVersionApproval>()).ToLookup(a => a.ProtocolVersionId);

			foreach (var bulletin in bulletins)
			{
				if (!versions.TryGetValue(bulletin.CurrentVersionId.Value, out var version))
					continue;
				var rows = (await _acks.GetForVersionAsync(reader.DepartmentId, version.ProtocolVersionId) ?? new List<ProtocolAcknowledgment>())
					.Where(r => r.Status != (int)ProtocolAcknowledgmentStatuses.Withdrawn).ToList();
				items.Add(new ProtocolBulletinItem
				{
					Protocol = bulletin,
					Version = version,
					Countersign = steps[version.ProtocolVersionId].OrderByDescending(s => s.ProtocolVersionApprovalId).FirstOrDefault(),
					Assigned = rows.Count,
					Acknowledged = rows.Count(ProtocolAcknowledgmentRules.IsComplete),
					IsExpired = bulletin.Status == (int)ProtocolStatuses.Retired || (version.ExpiresOn.HasValue && version.ExpiresOn.Value <= utcNow)
				});
			}

			return items;
		}

		public async Task<ProtocolLifecycleResult> CountersignBulletinAsync(ProtocolReader reader, int protocolId, SignatureProof proof, CancellationToken cancellationToken = default)
		{
			var (protocol, version, step, error) = await BulletinStepAsync(reader, protocolId);
			if (error != ProtocolLifecycleErrors.None)
				return new ProtocolLifecycleResult { Error = error, Protocol = protocol, Version = version };

			// The one who issued it does not approve it too (the approval afterwards is someone else's check), unless they are the only admin.
			if (string.Equals(version.PublishedByUserId, reader.UserId, StringComparison.OrdinalIgnoreCase) && !reader.IsDepartmentAdmin)
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.AlreadyDecided, Protocol = protocol, Version = version };

			if (proof == null || !string.Equals(proof.UserId, reader.UserId, StringComparison.OrdinalIgnoreCase))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.SignatureRequired, Protocol = protocol, Version = version };

			if (!string.Equals(await ProtocolVersionHashing.CurrentHashAsync(version, _files), version.ContentHash, StringComparison.Ordinal))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.ArtifactChanged, Protocol = protocol, Version = version };

			var settings = await _library.Value.GetSettingsAsync(reader.DepartmentId);
			var signed = await _signatures.SignAsync(new SignatureRequest
			{
				DepartmentId = reader.DepartmentId,
				Use = SignatureUse.ProtocolApproval,
				SubjectId = version.ProtocolVersionId.ToString(CultureInfo.InvariantCulture),
				RevisionId = "bulletin-countersign",
				ArtifactSha256 = version.ContentHash,
				StatementText = ProtocolSignatureStatements.BulletinCountersign(protocol, version),
				RequirePasskey = settings.RequirePasskeyForApproval
			}, proof, cancellationToken);
			if (!signed.Succeeded)
				return new ProtocolLifecycleResult
				{
					Error = signed.Outcome == SignatureOutcomes.PasskeyRequired ? ProtocolLifecycleErrors.PasskeyRequired : ProtocolLifecycleErrors.SignatureRequired,
					Protocol = protocol,
					Version = version
				};

			step.Decision = (int)ProtocolApprovalDecisions.Approved;
			step.DecidedByUserId = reader.UserId;
			step.DecidedOn = DateTime.UtcNow;
			step.SignatureRecordId = signed.Record.SignatureRecordId;
			if (!await _approvals.TryDecideAsync(step, cancellationToken))
			{
				await _signatures.VoidAsync(reader.DepartmentId, signed.Record.SignatureRecordId, reader.UserId, "The bulletin was decided by someone else first.", cancellationToken);
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.AlreadyDecided, Protocol = protocol, Version = version };
			}

			var expected = version.RowVersion;
			version.ApprovedOn = step.DecidedOn;
			version.ApprovedByUserId = reader.UserId;
			await _versions.TryUpdateAsync(version, expected, cancellationToken);

			Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolBulletinCountersigned, JsonConvert.SerializeObject(new
			{
				ProtocolId = protocol.DispatchProtocolId,
				VersionId = version.ProtocolVersionId,
				SignatureId = signed.Record.SignatureRecordId
			}));
			return new ProtocolLifecycleResult { Protocol = protocol, Version = version, Signature = signed.Record };
		}

		public async Task<ProtocolLifecycleResult> WithdrawBulletinAsync(ProtocolReader reader, int protocolId, string reason, CancellationToken cancellationToken = default)
		{
			var (protocol, version, step, error) = await BulletinStepAsync(reader, protocolId, allowDecided: true);
			if (error != ProtocolLifecycleErrors.None && error != ProtocolLifecycleErrors.AlreadyDecided)
				return new ProtocolLifecycleResult { Error = error, Protocol = protocol, Version = version };

			if (step != null && step.Decision == (int)ProtocolApprovalDecisions.Pending)
			{
				step.Decision = (int)ProtocolApprovalDecisions.Rejected;
				step.DecidedByUserId = reader.UserId;
				step.DecidedOn = DateTime.UtcNow;
				step.Comment = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim().Length > 2000 ? reason.Trim().Substring(0, 2000) : reason.Trim();
				await _approvals.TryDecideAsync(step, cancellationToken);
			}

			// Withdrawn: out of force now (the library retires it, which withdraws the open acknowledgments).
			var retired = await _library.Value.RetireAsync(reader, protocolId, cancellationToken);
			if (!retired.Succeeded)
				return retired;

			Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolBulletinWithdrawn, JsonConvert.SerializeObject(new
			{
				ProtocolId = protocolId,
				VersionId = version?.ProtocolVersionId,
				Reason = reason
			}));
			return retired;
		}

		private async Task<(DispatchProtocol, ProtocolVersion, ProtocolVersionApproval, ProtocolLifecycleErrors)> BulletinStepAsync(ProtocolReader reader, int protocolId, bool allowDecided = false)
		{
			if (reader == null || !reader.MayApprove)
				return (null, null, null, ProtocolLifecycleErrors.NotAuthorized);

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(protocolId);
			if (protocol == null || protocol.DepartmentId != reader.DepartmentId || protocol.IsDeleted || !protocol.IsBulletin || !protocol.CurrentVersionId.HasValue)
				return (null, null, null, ProtocolLifecycleErrors.NotFound);

			var version = await _versions.GetByIdForDepartmentAsync(reader.DepartmentId, protocol.CurrentVersionId.Value);
			if (version == null)
				return (protocol, null, null, ProtocolLifecycleErrors.NotFound);
			if (!protocol.IsPublished && !allowDecided)
				return (protocol, version, null, ProtocolLifecycleErrors.NotPublished);

			var step = (await _approvals.GetForVersionAsync(reader.DepartmentId, version.ProtocolVersionId) ?? new List<ProtocolVersionApproval>())
				.OrderByDescending(a => a.ProtocolVersionApprovalId).FirstOrDefault();
			if (step == null || step.Decision != (int)ProtocolApprovalDecisions.Pending)
				return (protocol, version, step, ProtocolLifecycleErrors.AlreadyDecided);

			return (protocol, version, step, ProtocolLifecycleErrors.None);
		}

		#endregion Priority bulletins

		#region Worker 86

		public async Task<int> SendRemindersAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var sent = 0;
			var due = await _acks.GetRemindersDueAsync(utcNow, ReminderBatch) ?? new List<ProtocolAcknowledgmentDue>();
			foreach (var department in due.GroupBy(d => d.DepartmentId))
			{
				try
				{
					var active = await _departments.GetActiveMemberUserIdsAsync(department.Key) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					var names = (await _departments.GetAllPersonnelNamesForDepartmentAsync(department.Key) ?? new List<PersonName>())
						.Where(n => n != null && !string.IsNullOrWhiteSpace(n.UserId))
						.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name?.Trim(), StringComparer.OrdinalIgnoreCase);
					var protocols = new Dictionary<int, DispatchProtocol>();
					var versions = new Dictionary<int, ProtocolVersion>();

					foreach (var item in department)
					{
						cancellationToken.ThrowIfCancellationRequested();
						var stage = ProtocolAcknowledgmentRules.DueStage(item.DueOn, utcNow);
						if ((int)stage <= item.ReminderStage)
							continue;

						// Claim the stage first: a second run (or node) never sends the same reminder twice.
						if (!await _acks.MarkReminderAsync(item.DepartmentId, item.ProtocolAcknowledgmentId, (int)stage, utcNow, cancellationToken))
							continue;

						// Inactive members are not reminded (their rows are withdrawn by worker 83 when the change is queued).
						if (!active.Contains(item.UserId ?? string.Empty))
							continue;

						if (!protocols.TryGetValue(item.DispatchProtocolId, out var protocol))
							protocols[item.DispatchProtocolId] = protocol = await _protocolsService.GetProtocolHeaderByIdAsync(item.DispatchProtocolId);
						if (!versions.TryGetValue(item.ProtocolVersionId, out var version))
							versions[item.ProtocolVersionId] = version = await _versions.GetByIdForDepartmentAsync(item.DepartmentId, item.ProtocolVersionId);
						if (protocol == null || version == null || protocol.DepartmentId != item.DepartmentId)
							continue;

						// Overdue once per row: the first claimed stage at or past Overdue (a row first seen at Escalated raises it then).
						if (stage >= ProtocolAcknowledgmentReminderStages.Overdue && item.ReminderStage < (int)ProtocolAcknowledgmentReminderStages.Overdue)
							RaiseWorkflow(await _acks.GetByIdForDepartmentAsync(item.DepartmentId, item.ProtocolAcknowledgmentId) ?? new ProtocolAcknowledgment
							{
								ProtocolAcknowledgmentId = item.ProtocolAcknowledgmentId,
								DepartmentId = item.DepartmentId,
								DispatchProtocolId = item.DispatchProtocolId,
								ProtocolVersionId = item.ProtocolVersionId,
								UserId = item.UserId,
								DueOn = item.DueOn
							}, protocol, version, ProtocolAcknowledgmentWorkflowChanges.Overdue, utcNow, stage);

						sent += await _notifications.NotifyReminderAsync(item.DepartmentId, item.UserId, protocol, version, stage, item.DueOn, cancellationToken);

						if (stage == ProtocolAcknowledgmentReminderStages.Escalated)
						{
							var supervisors = await SupervisorsAsync(item.DepartmentId, item.UserId);
							var name = names.TryGetValue(item.UserId ?? string.Empty, out var n) && !string.IsNullOrWhiteSpace(n) ? n : "A member";
							sent += await _notifications.NotifyEscalationAsync(item.DepartmentId, supervisors, name, protocol, version, cancellationToken);
						}
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Protocol acknowledgment reminders failed for department {department.Key}.");
				}
			}

			return sent;
		}

		/// <summary>The member's supervisor: the admins of their group (and its parents), else the department's active admins.</summary>
		private async Task<List<string>> SupervisorsAsync(int departmentId, string userId)
		{
			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var group = await _groups.GetGroupForUserAsync(userId, departmentId);
			if (group != null && group.DepartmentId == departmentId)
				foreach (var admin in await _groups.GetAllAdminsForGroupAndAncestorsAsync(group.DepartmentGroupId) ?? new List<DepartmentGroupMember>())
					if (!string.IsNullOrWhiteSpace(admin?.UserId))
						ids.Add(admin.UserId);

			if (ids.Count == 0)
				foreach (var admin in await _departments.GetActiveAdminsForDepartmentAsync(departmentId) ?? new List<Model.Identity.IdentityUser>())
					ids.Add(admin.Id);

			// Nobody escalates to themselves.
			ids.RemoveWhere(id => string.Equals(id, userId, StringComparison.OrdinalIgnoreCase));
			return ids.ToList();
		}

		public async Task<int> CompletePassedQuizzesAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var completed = 0;
			foreach (var row in await _acks.GetAwaitingQuizAsync(QuizBatch) ?? new List<ProtocolAcknowledgment>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					// The linked training and (LMS) the bank quiz: database reads only, no billing gate in this sweep (R-L9).
					if (!await QuizzesPassedAsync(row))
						continue;

					if (await CompleteAwaitingAsync(row, utcNow, cancellationToken))
						completed++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Quiz-gated acknowledgment {row.ProtocolAcknowledgmentId} could not be completed.");
				}
			}

			return completed;
		}

		#endregion Worker 86

		private void Audit(int departmentId, string userId, AuditLogTypes type, string after)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				After = after,
				Successful = true,
				ServerName = Environment.MachineName
			});
		}
	}
}

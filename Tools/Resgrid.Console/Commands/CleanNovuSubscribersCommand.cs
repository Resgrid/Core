using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resgrid.Console.Models;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Messaging;

namespace Resgrid.Console.Commands
{
	/// <summary>
	///     One-off cleanup of the Novu subscribers left behind before deletion and removal started deleting them, which
	///     still hold the phone and browser tokens of people and rigs that should no longer get a department's pushes:
	///     <list type="bullet">
	///     <item>Deleted departments: every {code}_User_, {code}_IC_User_ and {code}_Unit_ subscriber whose code
	///     belongs to no department in this database.</item>
	///     <item>Removed members: {code}_User_ and {code}_IC_User_ subscribers of a department that still exists,
	///     for a user who is not one of its current (non-deleted) members.</item>
	///     <item>Deleted units: {code}_Unit_ subscribers of a department that still exists, for a unit that is not
	///     one of its current (non-deleted) units.</item>
	///     </list>
	///     <para>
	///     Ids NovuProvider did not create are never touched. The subscriber list is read first, in full, before the
	///     departments and members: anything on it was registered by a department and membership that already existed,
	///     so a department, member or unit created during the run is never mistaken for a deleted one. A department whose
	///     members or units cannot be read is skipped, never judged.
	///     </para>
	///     <para>
	///     Dry run by default: it reports what it would delete and changes nothing. Pass --Apply to delete. When more
	///     than half of the matching subscribers would be deleted it stops unless --Force is passed: that usually means
	///     this database and the configured Novu key belong to different environments.
	///     </para>
	/// </summary>
	public sealed class CleanNovuSubscribersCommand(
		ILogger<CleanNovuSubscribersCommand> logger,
		IDepartmentsService departmentsService,
		IUnitsService unitsService,
		INovuProvider novuProvider) : ICommandService
	{
		private const int Parallelism = 4;
		private const double MaxDeleteShareWithoutForce = 0.5;
		private const int Listed = 50;

		public async Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken)
		{
			var apply = args.Any(a => a.Equals("--Apply", StringComparison.OrdinalIgnoreCase));
			var force = args.Any(a => a.Equals("--Force", StringComparison.OrdinalIgnoreCase));

			logger.LogInformation("Resgrid Novu Subscriber Cleanup");
			logger.LogInformation(apply
				? "Mode: APPLY - subscribers of deleted departments, removed members and deleted units will be deleted."
				: "Mode: DRY RUN - nothing will be deleted. Pass --Apply to delete.");

			var subscriberIds = await novuProvider.ListAllSubscriberIdsAsync(cancellationToken);
			if (subscriberIds == null)
			{
				logger.LogError("The Novu subscriber list could not be read in full (see the logged error); nothing was deleted.");
				return ExitCode.Failed;
			}

			var departmentIdsByCode = (await departmentsService.GetAllAsync() ?? new List<Department>())
				.Where(d => !string.IsNullOrWhiteSpace(d.Code))
				.GroupBy(d => d.Code, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.Select(d => d.DepartmentId).ToList(), StringComparer.OrdinalIgnoreCase);

			// With no codes every subscriber would look orphaned.
			if (departmentIdsByCode.Count == 0)
			{
				logger.LogError("No department codes were read from the database; refusing to treat every subscriber as orphaned.");
				return ExitCode.Failed;
			}

			var codes = new HashSet<string>(departmentIdsByCode.Keys, StringComparer.OrdinalIgnoreCase);
			var matching = subscriberIds.Count(id => NovuSubscriberIds.Parse(id) != null);

			logger.LogInformation("{Subscribers} Novu subscribers, {Matching} of them Resgrid user, IC user or unit subscribers; {Departments} department codes in the database.",
				subscriberIds.Count, matching, codes.Count);

			// Deleted departments.
			var departmentOrphans = NovuSubscriberIds.FindOrphans(subscriberIds, codes);
			var orphanCodes = departmentOrphans.Select(o => o.Code).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();

			logger.LogInformation("Deleted departments: {Count} subscribers under {Codes} codes that no longer exist ({UserCount} user, {ICCount} IC user, {UnitCount} unit).",
				departmentOrphans.Count, orphanCodes.Count, CountOf(departmentOrphans, NovuSubscriberKind.User), CountOf(departmentOrphans, NovuSubscriberKind.ICUser),
				CountOf(departmentOrphans, NovuSubscriberKind.Unit));

			if (orphanCodes.Count > 0)
				logger.LogInformation("    Codes: {Codes}{More}", string.Join(", ", orphanCodes.Take(Listed)), orphanCodes.Count > Listed ? ", ..." : "");

			// Removed members.
			var currentMembersByCode = await ReadCurrentMembersAsync(subscriberIds, departmentIdsByCode, cancellationToken);
			var removedMembers = NovuSubscriberIds.FindRemovedMembers(subscriberIds, currentMembersByCode);
			var removedByCode = removedMembers.GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ToList();

			logger.LogInformation("Removed members: {Count} subscribers of {Users} users who are no longer members, across {Departments} departments ({UserCount} user, {ICCount} IC user).",
				removedMembers.Count, removedMembers.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(), removedByCode.Count,
				CountOf(removedMembers, NovuSubscriberKind.User), CountOf(removedMembers, NovuSubscriberKind.ICUser));

			if (removedByCode.Count > 0)
				logger.LogInformation("    By code: {Codes}{More}", string.Join(", ", removedByCode.Take(Listed).Select(g => $"{g.Key} ({g.Count()})")), removedByCode.Count > Listed ? ", ..." : "");

			// Deleted units.
			var currentUnitsByCode = await ReadCurrentUnitsAsync(subscriberIds, departmentIdsByCode, cancellationToken);
			var deletedUnits = NovuSubscriberIds.FindDeletedUnits(subscriberIds, currentUnitsByCode);
			var deletedUnitsByCode = deletedUnits.GroupBy(u => u.Code, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ToList();

			logger.LogInformation("Deleted units: {Count} subscribers of units no longer in use, across {Departments} departments.",
				deletedUnits.Count, deletedUnitsByCode.Count);

			if (deletedUnitsByCode.Count > 0)
				logger.LogInformation("    By code: {Codes}{More}", string.Join(", ", deletedUnitsByCode.Take(Listed).Select(g => $"{g.Key} ({g.Count()})")), deletedUnitsByCode.Count > Listed ? ", ..." : "");

			var toDelete = departmentOrphans.Concat(removedMembers).Concat(deletedUnits).ToList();

			if (toDelete.Count == 0)
			{
				logger.LogInformation("Nothing to clean up.");
				return ExitCode.Success;
			}

			if (!apply)
			{
				logger.LogInformation("Dry run complete. Re-run with --Apply to delete these {Count} subscribers.", toDelete.Count);
				return ExitCode.Success;
			}

			if (toDelete.Count > matching * MaxDeleteShareWithoutForce && !force)
			{
				logger.LogError("{Count} of {Matching} matching subscribers would be deleted. That usually means this database and the Novu key belong to different environments; nothing was deleted. Re-run with --Force if this is expected.",
					toDelete.Count, matching);
				return ExitCode.Failed;
			}

			var failed = new ConcurrentBag<string>();
			var deleted = 0;

			await Parallel.ForEachAsync(toDelete, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken },
				async (subscriber, _) =>
				{
					bool removed;
					try
					{
						removed = subscriber.Kind switch
						{
							NovuSubscriberKind.User => await novuProvider.DeleteUserSubscriber(subscriber.Id, subscriber.Code),
							NovuSubscriberKind.ICUser => await novuProvider.DeleteICUserSubscriber(subscriber.Id, subscriber.Code),
							_ => await novuProvider.DeleteUnitSubscriber(int.Parse(subscriber.Id), subscriber.Code)
						};
					}
					catch (Exception ex)
					{
						logger.LogWarning(ex, "Deleting Novu subscriber {SubscriberId} failed.", subscriber.SubscriberId);
						removed = false;
					}

					if (removed)
						Interlocked.Increment(ref deleted);
					else
						failed.Add(subscriber.SubscriberId);
				});

			logger.LogInformation("Deleted {Deleted} of {Count} Novu subscribers.", deleted, toDelete.Count);

			if (!failed.IsEmpty)
			{
				logger.LogError("{Failed} could not be deleted (re-run to retry them): {SubscriberIds}{More}",
					failed.Count, string.Join(", ", failed.Take(Listed)), failed.Count > Listed ? ", ..." : "");
				return ExitCode.Failed;
			}

			return ExitCode.Success;
		}

		/// <summary>
		/// The current (non-deleted) members of each existing department code that has user or IC user subscribers.
		/// Departments sharing a code pool their members. A code whose members cannot be read, or come back empty, is
		/// left out, so its subscribers are never judged.
		/// </summary>
		private async Task<IReadOnlyDictionary<string, ISet<string>>> ReadCurrentMembersAsync(List<string> subscriberIds,
			Dictionary<string, List<int>> departmentIdsByCode, CancellationToken cancellationToken)
		{
			var codesWithUserSubscribers = subscriberIds
				.Select(NovuSubscriberIds.Parse)
				.Where(s => s != null && s.Kind != NovuSubscriberKind.Unit && departmentIdsByCode.ContainsKey(s.Code))
				.Select(s => s!.Code)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			var currentMembersByCode = new Dictionary<string, ISet<string>>(StringComparer.OrdinalIgnoreCase);

			foreach (var code in codesWithUserSubscribers)
			{
				cancellationToken.ThrowIfCancellationRequested();

				try
				{
					var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

					foreach (var departmentId in departmentIdsByCode[code])
					{
						var members = await departmentsService.GetAllMembersForDepartmentIncludingDeletedAsync(departmentId) ?? new List<DepartmentMember>();
						current.UnionWith(members.Where(m => !m.IsDeleted && !string.IsNullOrWhiteSpace(m.UserId)).Select(m => m.UserId));
					}

					// Every department has at least its managing member; none means the read came back wrong.
					if (current.Count == 0)
					{
						logger.LogWarning("Department code {Code} has no current members on record; its subscribers are skipped.", code);
						continue;
					}

					currentMembersByCode[code] = current;
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					logger.LogWarning(ex, "The members of department code {Code} could not be read; its subscribers are skipped.", code);
				}
			}

			return currentMembersByCode;
		}

		/// <summary>
		/// The current (non-deleted) units of each existing department code that has unit subscribers. Departments sharing
		/// a code pool their units. A department may have no current units (all deleted), but a code with unit
		/// subscribers and no unit rows at all, deleted or not, means the read came back wrong, so it is left out, as is a
		/// code whose units cannot be read.
		/// </summary>
		private async Task<IReadOnlyDictionary<string, ISet<int>>> ReadCurrentUnitsAsync(List<string> subscriberIds,
			Dictionary<string, List<int>> departmentIdsByCode, CancellationToken cancellationToken)
		{
			var codesWithUnitSubscribers = subscriberIds
				.Select(NovuSubscriberIds.Parse)
				.Where(s => s != null && s.Kind == NovuSubscriberKind.Unit && departmentIdsByCode.ContainsKey(s.Code))
				.Select(s => s!.Code)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			var currentUnitsByCode = new Dictionary<string, ISet<int>>(StringComparer.OrdinalIgnoreCase);

			foreach (var code in codesWithUnitSubscribers)
			{
				cancellationToken.ThrowIfCancellationRequested();

				try
				{
					var units = new List<Unit>();

					foreach (var departmentId in departmentIdsByCode[code])
						units.AddRange(await unitsService.GetUnitsForDepartmentIncludingDeletedAsync(departmentId) ?? new List<Unit>());

					if (units.Count == 0)
					{
						logger.LogWarning("Department code {Code} has unit subscribers but no units on record; its unit subscribers are skipped.", code);
						continue;
					}

					currentUnitsByCode[code] = new HashSet<int>(units.Where(u => !u.IsDeleted).Select(u => u.UnitId));
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					logger.LogWarning(ex, "The units of department code {Code} could not be read; its unit subscribers are skipped.", code);
				}
			}

			return currentUnitsByCode;
		}

		private static int CountOf(IEnumerable<NovuSubscriber> subscribers, NovuSubscriberKind kind)
		{
			return subscribers.Count(s => s.Kind == kind);
		}
	}
}

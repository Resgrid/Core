using System;
using System.Collections.Generic;
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
	/// The dispatcher's desk session (M0324) and the desk board. A dispatcher is routable when on duty (shift roster, or
	/// unscheduled where the department allows it), signed in Available, not on break, below capacity and without an offer
	/// outstanding.
	/// </summary>
	public class DispatcherPresenceService : IDispatcherPresenceService
	{
		private readonly IDispatcherPresencesRepository _presencesRepository;
		private readonly IDispatcherBreaksRepository _breaksRepository;
		private readonly IDispatcherQueueItemsRepository _itemsRepository;
		private readonly IShiftDispatchersService _shiftDispatchersService;
		private readonly IDispatcherDeskConfigService _configService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDispatcherDeskNotifier _notifier;
		private readonly Lazy<IDispatcherRoutingService> _routingService;

		public DispatcherPresenceService(IDispatcherPresencesRepository presencesRepository, IDispatcherBreaksRepository breaksRepository,
			IDispatcherQueueItemsRepository itemsRepository, IShiftDispatchersService shiftDispatchersService, IDispatcherDeskConfigService configService,
			IDepartmentsService departmentsService, IUserProfileService userProfileService, IAuthorizationService authorizationService,
			IDispatcherDeskNotifier notifier, Lazy<IDispatcherRoutingService> routingService)
		{
			_presencesRepository = presencesRepository;
			_breaksRepository = breaksRepository;
			_itemsRepository = itemsRepository;
			_shiftDispatchersService = shiftDispatchersService;
			_configService = configService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_authorizationService = authorizationService;
			_notifier = notifier;
			_routingService = routingService;
		}

		public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

		public async Task<DispatcherDeskResult<DispatcherDeskMember>> SignInAsync(int departmentId, string userId, DispatcherSignInRequest request,
			CancellationToken cancellationToken = default)
		{
			request ??= new DispatcherSignInRequest();
			var config = await _configService.GetConfigAsync(departmentId);
			if (!config.Enabled)
				return DispatcherDeskResult<DispatcherDeskMember>.Fail(DispatcherDeskErrors.DeskDisabled);

			if (!await _shiftDispatchersService.IsEligibleDispatcherAsync(departmentId, userId))
				return DispatcherDeskResult<DispatcherDeskMember>.Fail(DispatcherDeskErrors.NotADispatcher);

			var now = UtcNow();
			var duty = await _shiftDispatchersService.GetDutyForUserAsync(departmentId, userId, now);
			var unscheduledAllowed = config.AllowUnscheduledDuty || !config.RequireShift;
			var unscheduled = duty == null || request.Unscheduled && unscheduledAllowed;

			if (duty == null && !unscheduledAllowed)
				return DispatcherDeskResult<DispatcherDeskMember>.Fail(DispatcherDeskErrors.NotOnDuty);

			if (unscheduled && request.DutyEndsOn.HasValue && request.DutyEndsOn.Value <= now)
				return DispatcherDeskResult<DispatcherDeskMember>.Fail(DispatcherDeskErrors.InvalidInput, "The duty end is in the past.");

			var presence = await GetOrCreateAsync(departmentId, userId, now, cancellationToken);
			var activeBreak = await _breaksRepository.GetActiveForUserAsync(departmentId, userId);

			// A break carries on only into a sign-in that refreshes a session already on it. Breaks start only signed in and sign-off
			// ends them, so one still active on a signed-off row is left over from a sign-off whose close failed: it ends now, as ended
			// by that sign-off, instead of putting the new session on break.
			if (activeBreak != null && !presence.IsSignedIn())
			{
				await CloseSignedOffBreakAsync(activeBreak, presence.StateChangedOn, userId, now, cancellationToken);
				activeBreak = null;
			}

			presence.State = activeBreak != null ? (int)DispatcherPresenceStates.OnBreak
				: request.StartAway ? (int)DispatcherPresenceStates.Away : (int)DispatcherPresenceStates.Available;
			presence.ActiveBreakId = activeBreak?.DispatcherBreakId;
			presence.StateChangedOn = now;
			presence.SignedInOn = now;
			presence.LastSeenOn = now;
			presence.DutyMode = unscheduled ? (int)DispatcherDutyModes.Unscheduled : (int)DispatcherDutyModes.Scheduled;
			presence.DutyShiftId = unscheduled ? null : duty?.ShiftId;
			presence.DutyShiftDayId = unscheduled ? null : duty?.ShiftDayId;
			presence.DutyEndsOn = unscheduled ? request.DutyEndsOn : duty?.EndUtc;
			presence.MissedOffers = 0;
			presence.AwayReason = request.StartAway ? "Signed in away" : null;
			presence.DeviceKind = NormalizeDeviceKind(request.DeviceKind) ?? presence.DeviceKind;

			var callback = NormalizePhone(request.CallbackNumber);
			if (callback != null)
				presence.CallbackNumber = callback;

			await _presencesRepository.SaveOrUpdateAsync(presence, cancellationToken, true);
			await _notifier.PublishDeskChangedAsync(departmentId, userId);
			await RouteAsync(departmentId, cancellationToken);

			return DispatcherDeskResult<DispatcherDeskMember>.Ok(await GetMemberAsync(departmentId, userId, now));
		}

		public Task<DispatcherDeskResult<bool>> SignOffAsync(int departmentId, string userId, string byUserId, CancellationToken cancellationToken = default) =>
			SignOffCoreAsync(departmentId, userId, byUserId, null, cancellationToken);

		/// <summary>
		/// Signs the dispatcher off and hands back the work they hold. With <paramref name="evaluated"/> (the sweep), only the
		/// session the caller looked at is signed off: a dispatcher who signed in again meanwhile keeps the new session. A
		/// dispatcher already signed off who still holds work (a release failed last time) gets that work handed back again.
		/// </summary>
		private async Task<DispatcherDeskResult<bool>> SignOffCoreAsync(int departmentId, string userId, string byUserId, DispatcherPresence evaluated,
			CancellationToken cancellationToken)
		{
			var presence = await _presencesRepository.GetForUserAsync(departmentId, userId);
			if (presence == null)
				return DispatcherDeskResult<bool>.Ok(false);

			if (evaluated != null && (presence.SignedInOn != evaluated.SignedInOn || presence.DutyEndsOn != evaluated.DutyEndsOn))
				return DispatcherDeskResult<bool>.Ok(false);

			var now = UtcNow();
			var signedOff = false;

			if (presence.IsSignedIn())
			{
				// Conditional on the session: a sign-in (or another sign-off) since the read above wins, never this write.
				signedOff = await _presencesRepository.TrySignOffAsync(presence.DispatcherPresenceId, presence.SignedInOn, now, cancellationToken);
				if (!signedOff)
					return DispatcherDeskResult<bool>.Ok(false);
			}

			// The session's break ends with it. Also on a row already signed off: a sign-off whose break close failed is finished by
			// signing off again (and otherwise by the next sign-in or the break timer), never by putting the next session on break.
			var activeBreak = await _breaksRepository.GetActiveForUserAsync(departmentId, userId);
			if (activeBreak != null)
				await CloseSignedOffBreakAsync(activeBreak, signedOff ? now : presence.StateChangedOn, byUserId, now, cancellationToken);

			// Offline first, so held and offered work handed back goes to someone else, never straight back to this dispatcher.
			var held = await GetHeldAsync(departmentId, userId);
			if (!signedOff && held.Count == 0)
				return DispatcherDeskResult<bool>.Ok(false);

			var stillHeld = await HandBackAsync(departmentId, userId, held, cancellationToken);
			if (stillHeld > 0)
				return DispatcherDeskResult<bool>.Fail(DispatcherDeskErrors.HoldingWork,
					$"Signed off, but {stillHeld} item(s) could not be handed back. Sign off again to retry.");

			return DispatcherDeskResult<bool>.Ok(true);
		}

		/// <summary>
		/// Ends a break as ended by the session's sign-off at <paramref name="signedOffOn"/> (now when unknown), conditional on it still
		/// being active. A break started after that sign-off belongs to a later session and is left alone.
		/// </summary>
		private async Task<bool> CloseSignedOffBreakAsync(DispatcherBreak activeBreak, DateTime? signedOffOn, string byUserId, DateTime now,
			CancellationToken cancellationToken)
		{
			if (activeBreak.StartedOn.HasValue && signedOffOn.HasValue && activeBreak.StartedOn.Value > signedOffOn.Value)
				return false;

			activeBreak.State = (int)DispatcherBreakStates.Completed;
			activeBreak.EndedOn = signedOffOn.HasValue && signedOffOn.Value <= now ? signedOffOn.Value : now;
			activeBreak.EndReason = (int)DispatcherBreakEndReasons.SignedOff;
			activeBreak.EndedByUserId = byUserId;
			activeBreak.UpdatedByUserId = byUserId;
			activeBreak.UpdatedOn = now;
			return await _breaksRepository.TryTransitionAsync(activeBreak, (int)DispatcherBreakStates.Active, cancellationToken);
		}

		/// <summary>
		/// Hands back work a signed-off dispatcher still holds: each item on its own, so one failing never keeps the rest, and each
		/// only as the assignment read here (a version-fenced release: work the dispatcher accepted again after signing back in, or that
		/// has moved to someone else, is left alone). A dispatcher signed in again by now keeps their work. Returns how many of the read
		/// assignments are still held afterwards (logged; the next sign-off or the worker's sweep retries them).
		/// </summary>
		private async Task<int> HandBackAsync(int departmentId, string userId, List<DispatcherQueueItem> held, CancellationToken cancellationToken)
		{
			if (held.Count == 0)
				return 0;

			var current = await _presencesRepository.GetForUserAsync(departmentId, userId);
			if (current != null && current.IsSignedIn())
				return 0;

			foreach (var item in held)
			{
				try
				{
					await _routingService.Value.ReleaseObservedAsync(item, cancellationToken);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
				{
					// The rest are still handed back; the check below reports what is left.
					Logging.LogException(ex, $"Dispatcher sign-off release of {item.DispatcherQueueItemId} for {userId} in {departmentId}");
				}
			}

			await _notifier.PublishDeskChangedAsync(departmentId, userId);
			await RouteAsync(departmentId, cancellationToken);

			// Still held as read (same Version): an assignment that changed since is someone else's now, or theirs again.
			var observed = held.ToDictionary(x => x.DispatcherQueueItemId, x => x.Version, StringComparer.OrdinalIgnoreCase);
			var stillHeld = (await GetHeldAsync(departmentId, userId))
				.Count(x => observed.TryGetValue(x.DispatcherQueueItemId, out var version) && version == x.Version);
			if (stillHeld > 0)
				Logging.LogError($"Dispatcher {userId} in {departmentId} is signed off but still holds {stillHeld} desk item(s); the next sign-off or desk sweep retries.");

			return stillHeld;
		}

		private async Task<List<DispatcherQueueItem>> GetHeldAsync(int departmentId, string userId) =>
			(await _itemsRepository.GetOpenForDepartmentAsync(departmentId) ?? Enumerable.Empty<DispatcherQueueItem>())
			.Where(x => string.Equals(x.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase) &&
						(x.State == (int)DispatcherQueueItemStates.Accepted || x.State == (int)DispatcherQueueItemStates.Offered))
			.ToList();

		public async Task HeartbeatAsync(int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			var presence = await _presencesRepository.GetForUserAsync(departmentId, userId);
			if (presence == null || !presence.IsSignedIn())
				return;

			// LastSeenOn alone, and only while still signed in: saving the whole row read above would write the signed-in state
			// back over a sign-off committed in between.
			await _presencesRepository.TouchAsync(presence.DispatcherPresenceId, UtcNow(), cancellationToken);
		}

		public async Task<DispatcherDeskResult<DispatcherDeskMember>> SetAvailableAsync(int departmentId, string userId, string byUserId,
			CancellationToken cancellationToken = default)
		{
			var presence = await _presencesRepository.GetForUserAsync(departmentId, userId);
			if (presence == null || !presence.IsSignedIn())
				return DispatcherDeskResult<DispatcherDeskMember>.Fail(DispatcherDeskErrors.NotSignedIn);

			if (presence.State == (int)DispatcherPresenceStates.OnBreak)
				return DispatcherDeskResult<DispatcherDeskMember>.Fail(DispatcherDeskErrors.AlreadyOnBreak);

			var now = UtcNow();
			presence.State = (int)DispatcherPresenceStates.Available;
			presence.StateChangedOn = now;
			presence.LastSeenOn = string.Equals(byUserId, userId, StringComparison.OrdinalIgnoreCase) ? now : presence.LastSeenOn;
			presence.MissedOffers = 0;
			presence.AwayReason = null;
			await _presencesRepository.SaveOrUpdateAsync(presence, cancellationToken, true);

			await _notifier.PublishDeskChangedAsync(departmentId, userId);
			await RouteAsync(departmentId, cancellationToken);

			return DispatcherDeskResult<DispatcherDeskMember>.Ok(await GetMemberAsync(departmentId, userId, now));
		}

		public async Task<DispatcherDeskResult<DispatcherDeskMember>> SetAwayAsync(int departmentId, string userId, string reason, string byUserId,
			CancellationToken cancellationToken = default)
		{
			var presence = await _presencesRepository.GetForUserAsync(departmentId, userId);
			if (presence == null || !presence.IsSignedIn())
				return DispatcherDeskResult<DispatcherDeskMember>.Fail(DispatcherDeskErrors.NotSignedIn);

			if (presence.State == (int)DispatcherPresenceStates.OnBreak)
				return DispatcherDeskResult<DispatcherDeskMember>.Fail(DispatcherDeskErrors.AlreadyOnBreak);

			var now = UtcNow();
			presence.State = (int)DispatcherPresenceStates.Away;
			presence.StateChangedOn = now;
			presence.AwayReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim().Length > 250 ? reason.Trim().Substring(0, 250) : reason.Trim();
			await _presencesRepository.SaveOrUpdateAsync(presence, cancellationToken, true);

			// Offers in flight move on; work already taken stays with the dispatcher.
			foreach (var offered in (await _itemsRepository.GetOpenForDepartmentAsync(departmentId) ?? Enumerable.Empty<DispatcherQueueItem>())
				.Where(x => x.State == (int)DispatcherQueueItemStates.Offered && string.Equals(x.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase)))
			{
				await _routingService.Value.ReleaseAsync(departmentId, offered.DispatcherQueueItemId, userId, cancellationToken);
			}

			await _notifier.PublishDeskChangedAsync(departmentId, userId);
			return DispatcherDeskResult<DispatcherDeskMember>.Ok(await GetMemberAsync(departmentId, userId, now));
		}

		public async Task<DispatcherDeskResult<DispatcherDeskMember>> SetCallbackNumberAsync(int departmentId, string userId, string callbackNumber,
			CancellationToken cancellationToken = default)
		{
			var normalized = NormalizePhone(callbackNumber);
			if (!string.IsNullOrWhiteSpace(callbackNumber) && normalized == null)
				return DispatcherDeskResult<DispatcherDeskMember>.Fail(DispatcherDeskErrors.InvalidInput);

			var now = UtcNow();
			var presence = await GetOrCreateAsync(departmentId, userId, now, cancellationToken);
			presence.CallbackNumber = normalized;
			await _presencesRepository.SaveOrUpdateAsync(presence, cancellationToken, true);

			return DispatcherDeskResult<DispatcherDeskMember>.Ok(await GetMemberAsync(departmentId, userId, now));
		}

		public async Task<DispatcherDeskSnapshot> GetDeskAsync(int departmentId, DateTime utcNow)
		{
			var config = await _configService.GetConfigAsync(departmentId);
			var snapshot = new DispatcherDeskSnapshot
			{
				Enabled = config.Enabled,
				GeneratedOn = utcNow,
				MinimumAvailable = config.EffectiveMinimumAvailable()
			};

			var presences = (await _presencesRepository.GetByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<DispatcherPresence>()).ToList();
			var duty = await _shiftDispatchersService.GetOnDutyAsync(departmentId, utcNow);
			var items = (await _itemsRepository.GetOpenForDepartmentAsync(departmentId) ?? Enumerable.Empty<DispatcherQueueItem>()).ToList();
			var breaks = (await _breaksRepository.GetForDepartmentInRangeAsync(departmentId, utcNow.AddHours(-12), utcNow.AddHours(24)) ??
						  Enumerable.Empty<DispatcherBreak>()).ToList();

			var userIds = duty.Select(x => x.UserId)
				.Concat(presences.Where(x => x.IsSignedIn()).Select(x => x.UserId))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			var profiles = userIds.Count == 0
				? new Dictionary<string, UserProfile>()
				: (await _userProfileService.GetAllProfilesForDepartmentAsync(departmentId) ?? new Dictionary<string, UserProfile>());

			foreach (var userId in userIds)
				snapshot.Members.Add(BuildMember(userId, config, utcNow, presences, duty, items, breaks, profiles));

			snapshot.Members = snapshot.Members
				.OrderBy(x => x.DisplayState == DispatcherDisplayStates.Offline ? 1 : 0)
				.ThenBy(x => x.Name ?? x.UserId, StringComparer.OrdinalIgnoreCase)
				.ToList();
			snapshot.Queue = DispatcherRoutingPolicy.QueueOrder(items);
			snapshot.OnDutyCount = snapshot.Members.Count(x => x.IsOnDuty);
			snapshot.OnBreakCount = snapshot.Members.Count(x => x.IsOnDuty && x.DisplayState == DispatcherDisplayStates.OnBreak);
			snapshot.AvailableCount = snapshot.Members.Count(x => x.IsOnDuty &&
				(x.DisplayState == DispatcherDisplayStates.Available || x.DisplayState == DispatcherDisplayStates.Busy));

			return snapshot;
		}

		public async Task<DispatcherDeskMember> GetMemberAsync(int departmentId, string userId, DateTime utcNow)
		{
			var config = await _configService.GetConfigAsync(departmentId);
			var presence = await _presencesRepository.GetForUserAsync(departmentId, userId);
			var duty = await _shiftDispatchersService.GetDutyForUserAsync(departmentId, userId, utcNow);
			var items = (await _itemsRepository.GetOpenForDepartmentAsync(departmentId) ?? Enumerable.Empty<DispatcherQueueItem>())
				.Where(x => string.Equals(x.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();
			var breaks = (await _breaksRepository.GetOpenForUserAsync(departmentId, userId, utcNow.AddHours(-12), utcNow.AddHours(24)) ??
						  Enumerable.Empty<DispatcherBreak>()).ToList();
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
			var profiles = new Dictionary<string, UserProfile>(StringComparer.OrdinalIgnoreCase);
			if (profile != null)
				profiles[userId] = profile;

			return BuildMember(userId, config, utcNow,
				presence == null ? new List<DispatcherPresence>() : new List<DispatcherPresence> { presence },
				duty == null ? new List<DispatcherDutyWindow>() : new List<DispatcherDutyWindow> { duty },
				items, breaks, profiles);
		}

		public async Task<List<DispatcherDeskMember>> GetRoutableAsync(int departmentId, DateTime utcNow, IEnumerable<string> excludeUserIds = null)
		{
			var config = await _configService.GetConfigAsync(departmentId);
			if (!config.Enabled)
				return new List<DispatcherDeskMember>();

			var exclude = new HashSet<string>(excludeUserIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			var desk = await GetDeskAsync(departmentId, utcNow);

			var routable = desk.Members.Where(x => !exclude.Contains(x.UserId) &&
				DispatcherRoutingPolicy.IsRoutable(x, utcNow, config.EffectiveHeartbeatTimeoutMinutes())).ToList();

			// Sign-in checked membership and the Dispatch login; either can be taken away while the session lasts, and work must
			// never be offered to someone who lost them (the worker's sweep then signs them off).
			var eligible = new List<DispatcherDeskMember>();
			foreach (var member in routable)
			{
				if (await _shiftDispatchersService.IsEligibleDispatcherAsync(departmentId, member.UserId))
					eligible.Add(member);
			}

			return DispatcherRoutingPolicy.Order(eligible);
		}

		public async Task<List<string>> GetSupervisorUserIdsAsync(int departmentId, DateTime utcNow)
		{
			var supervisors = (await _shiftDispatchersService.GetOnDutyAsync(departmentId, utcNow))
				.Where(x => x.IsSupervisor).Select(x => x.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (supervisors.Count > 0)
				return supervisors;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			if (department == null)
				return new List<string>();

			return (department.AdminUsers ?? new List<string>()).Concat(new[] { department.ManagingUserId })
				.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		public async Task<bool> AreAlertsSuppressedAsync(int departmentId, string userId)
		{
			var presence = await _presencesRepository.GetForUserAsync(departmentId, userId);
			if (presence == null || presence.State != (int)DispatcherPresenceStates.OnBreak)
				return false;

			return (await _configService.GetConfigAsync(departmentId)).SuppressAlertsOnBreak;
		}

		public async Task<bool> CanSuperviseAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return false;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			if (department != null && department.IsUserAnAdmin(userId))
				return true;

			var duty = await _shiftDispatchersService.GetDutyForUserAsync(departmentId, userId, UtcNow());
			if (duty != null && duty.IsSupervisor)
				return true;

			try
			{
				var scope = await _authorizationService.GetShiftManagementScopeAsync(userId, departmentId);
				return scope != null && scope.AllGroups;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Dispatcher desk supervisor check");
				return false;
			}
		}

		public async Task<int> ProcessPresenceAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var changed = 0;
			foreach (var presence in (await _presencesRepository.GetSignedInAsync(cancellationToken) ?? Enumerable.Empty<DispatcherPresence>()).ToList())
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				try
				{
					var config = await _configService.GetConfigAsync(presence.DepartmentId);
					if (!config.Enabled)
					{
						await SignOffAsync(presence.DepartmentId, presence.UserId, null, cancellationToken);
						changed++;
						continue;
					}

					// Membership or the Dispatch login taken away since sign-in: the session ends and its work goes back.
					if (!await _shiftDispatchersService.IsEligibleDispatcherAsync(presence.DepartmentId, presence.UserId))
					{
						if ((await SignOffCoreAsync(presence.DepartmentId, presence.UserId, null, presence, cancellationToken)).Value)
							changed++;
						continue;
					}

					if (!presence.DutyEndsOn.HasValue || presence.DutyEndsOn.Value > utcNow)
						continue;

					if (presence.DutyMode == (int)DispatcherDutyModes.Scheduled)
					{
						// A back-to-back shift day carries the dispatcher on.
						// Both duty writes are conditional on the session and the duty read above: another worker that moved the duty on
						// meanwhile keeps its value.
						var next = await _shiftDispatchersService.GetDutyForUserAsync(presence.DepartmentId, presence.UserId, utcNow);
						if (next != null)
						{
							await _presencesRepository.TrySetDutyAsync(presence, (int)DispatcherDutyModes.Scheduled, next.ShiftId, next.ShiftDayId, next.EndUtc,
								cancellationToken);
							continue;
						}

						if (!config.SignOffAtShiftEnd)
							continue;

						// Without a shift the dispatcher can stay on only when the department does not require one.
						if (!config.RequireShift)
						{
							await _presencesRepository.TrySetDutyAsync(presence, (int)DispatcherDutyModes.Unscheduled, presence.DutyShiftId, presence.DutyShiftDayId,
								null, cancellationToken);
							continue;
						}
					}

					// Only the session evaluated here: a dispatcher who signed in again since the read above keeps the new one.
					if ((await SignOffCoreAsync(presence.DepartmentId, presence.UserId, null, presence, cancellationToken)).Value)
						changed++;
				}
				catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
				{
					Logging.LogException(ex, $"Dispatcher presence sweep for {presence.UserId} in {presence.DepartmentId}");
				}
			}

			changed += await HandBackFromSignedOffAsync(cancellationToken);

			return changed;
		}

		/// <summary>
		/// Work still held by a dispatcher who is not signed in: a sign-off whose hand-back failed part way, or an accept that
		/// committed just after the sign-off released the dispatcher's work. It goes back to the queue.
		/// </summary>
		private async Task<int> HandBackFromSignedOffAsync(CancellationToken cancellationToken)
		{
			var changed = 0;
			foreach (var departmentId in (await _itemsRepository.GetDepartmentsWithOpenItemsAsync() ?? Enumerable.Empty<int>()).Distinct().ToList())
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				try
				{
					var held = (await _itemsRepository.GetOpenForDepartmentAsync(departmentId) ?? Enumerable.Empty<DispatcherQueueItem>())
						.Where(x => !string.IsNullOrWhiteSpace(x.AssignedUserId) &&
									(x.State == (int)DispatcherQueueItemStates.Accepted || x.State == (int)DispatcherQueueItemStates.Offered))
						.ToList();
					if (held.Count == 0)
						continue;

					var signedIn = new HashSet<string>((await _presencesRepository.GetByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<DispatcherPresence>())
						.Where(x => x.IsSignedIn()).Select(x => x.UserId), StringComparer.OrdinalIgnoreCase);

					foreach (var holder in held.Where(x => !signedIn.Contains(x.AssignedUserId)).GroupBy(x => x.AssignedUserId, StringComparer.OrdinalIgnoreCase))
					{
						var items = holder.ToList();
						if (await HandBackAsync(departmentId, holder.Key, items, cancellationToken) < items.Count)
							changed++;
					}
				}
				catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
				{
					Logging.LogException(ex, $"Dispatcher desk hand-back sweep in {departmentId}");
				}
			}

			return changed;
		}

		private DispatcherDeskMember BuildMember(string userId, DispatcherDeskConfig config, DateTime utcNow, List<DispatcherPresence> presences,
			List<DispatcherDutyWindow> duty, List<DispatcherQueueItem> items, List<DispatcherBreak> breaks, Dictionary<string, UserProfile> profiles)
		{
			var presence = presences.FirstOrDefault(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));
			var window = duty.FirstOrDefault(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));
			var mine = items.Where(x => string.Equals(x.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();
			var myBreaks = breaks.Where(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();
			var capacity = presence?.Capacity is int c && c > 0 ? Math.Min(c, 10) : config.EffectiveMaxActiveItems();
			var active = mine.Count(x => x.State == (int)DispatcherQueueItemStates.Accepted);

			var activeBreak = myBreaks.FirstOrDefault(x => x.State == (int)DispatcherBreakStates.Active);
			var nextBreak = myBreaks.Where(x => x.State == (int)DispatcherBreakStates.Scheduled && x.ScheduledStartOn.HasValue &&
												(window == null || x.ScheduledStartOn.Value < window.EndUtc))
				.OrderBy(x => x.ScheduledStartOn).FirstOrDefault();

			var signedIn = presence != null && presence.IsSignedIn();
			var unscheduledDuty = signedIn && presence.DutyMode == (int)DispatcherDutyModes.Unscheduled &&
								  (config.AllowUnscheduledDuty || !config.RequireShift) &&
								  (!presence.DutyEndsOn.HasValue || presence.DutyEndsOn.Value > utcNow);
			// The no-shift fallback covers a scheduled session whose shift window has gone (the department does not require one).
			// An unscheduled session is on duty only by the rule above, so an explicit duty end that has passed ends it.
			var isOnDuty = window != null || unscheduledDuty ||
						   signedIn && !config.RequireShift && presence.DutyMode != (int)DispatcherDutyModes.Unscheduled;

			profiles.TryGetValue(userId, out var profile);

			return new DispatcherDeskMember
			{
				UserId = userId,
				Name = NameOf(profile) ?? userId,
				Presence = presence,
				Duty = window,
				IsOnDuty = isOnDuty,
				IsSupervisor = window?.IsSupervisor ?? false,
				ActiveBreak = activeBreak,
				NextBreak = nextBreak,
				ActiveItemCount = active,
				OfferedItemCount = mine.Count(x => x.State == (int)DispatcherQueueItemStates.Offered),
				Capacity = capacity,
				DisplayState = DispatcherRoutingPolicy.DisplayState(presence, active, capacity),
				OverdueMinutes = activeBreak?.ExpectedReturnOn is DateTime due && due < utcNow ? (int)Math.Floor((utcNow - due).TotalMinutes) : 0
			};
		}

		private async Task<DispatcherPresence> GetOrCreateAsync(int departmentId, string userId, DateTime now, CancellationToken cancellationToken)
		{
			var presence = await _presencesRepository.GetForUserAsync(departmentId, userId);
			if (presence != null)
				return presence;

			presence = new DispatcherPresence
			{
				DispatcherPresenceId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				UserId = userId,
				State = (int)DispatcherPresenceStates.Offline,
				StateChangedOn = now
			};

			if (await _presencesRepository.TryInsertAsync(presence, cancellationToken))
				return presence;

			return await _presencesRepository.GetForUserAsync(departmentId, userId) ?? presence;
		}

		private async Task RouteAsync(int departmentId, CancellationToken cancellationToken)
		{
			try
			{
				await _routingService.Value.RouteDepartmentAsync(departmentId, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher routing after a desk change in {departmentId}");
			}
		}

		internal static string NameOf(UserProfile profile)
		{
			var name = profile?.FullName?.AsFirstNameLastName?.Trim();
			return string.IsNullOrWhiteSpace(name) ? null : name;
		}

		internal static string NormalizeDeviceKind(string deviceKind)
		{
			var kind = deviceKind?.Trim().ToLowerInvariant();
			return kind == "phone" || kind == "tablet" || kind == "desktop" ? kind : null;
		}

		/// <summary>
		/// E.164-ish: digits with a leading +. Bare ten digits (no +) are taken as North American; a number written with + keeps
		/// its own country code. Null when not a phone number.
		/// </summary>
		public static string NormalizePhone(string number)
		{
			if (string.IsNullOrWhiteSpace(number))
				return null;

			var international = number.TrimStart().StartsWith("+", StringComparison.Ordinal);
			var digits = new string(number.Where(char.IsDigit).ToArray());
			if (digits.Length == 10 && !international)
				digits = "1" + digits;

			return digits.Length < 8 || digits.Length > 15 ? null : "+" + digits;
		}
	}
}

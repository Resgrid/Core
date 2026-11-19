using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Autofac;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using SharpKml.Dom;

namespace Resgrid.Workers.Framework.Logic
{
	public class BroadcastCallLogic
	{
		public static async Task<bool> ProcessCallQueueItem(CallQueueItem cqi)
		{
			// Own scope per message. These services used to be static fields resolved from the root scope,
			// shared by every call processed concurrently and by the process-wide root unit of work.
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			var _communicationService = scope.Resolve<ICommunicationService>();
			var _callsService = scope.Resolve<ICallsService>();
			var _departmentSettingsService = scope.Resolve<IDepartmentSettingsService>();
			cqi?.ApplyBroadcastDispatchFilter();

			if (cqi != null && cqi.Call != null && cqi.Call.HasAnyDispatches())
			{
				using var trace = DispatchTraceTelemetry.Begin(cqi.Call.DepartmentId, cqi.Call.CallId, cqi.QueueItem?.QueueItemId, Config.AdminAssistConfig.CaptureDispatchTraces);
				DispatchTraceTelemetry.Observe(DispatchTraceStage.BroadcastStarted);
				List<int> groupIds = new List<int>();

				/* Trying to see if I can eek out a little perf here now that profiles are in Redis. Previously the
					 * the parallel operation would cause EF errors. This shouldn't be the case now because profiles are
					 * cached and GetProfileForUser operations will hit that first.
					 */
				if (cqi.Profiles == null || !cqi.Profiles.Any())
				{
					var _userProfilesService = scope.Resolve<IUserProfileService>();

					cqi.Profiles = (await _userProfilesService.GetAllProfilesForDepartmentAsync(cqi.Call.DepartmentId)).Select(x => x.Value).ToList();
				}

				if (cqi.CallDispatchAttachmentId > 0)
				{
					//var callsService = Bootstrapper.GetKernel().Resolve<ICallsService>();
					cqi.Call.ShortenedAudioUrl = await _callsService.GetShortenedAudioUrlAsync(cqi.Call.CallId, cqi.CallDispatchAttachmentId);
				}

				cqi.Call.ShortenedCallUrl = await _callsService.GetShortenedCallLinkUrl(cqi.Call.CallId);

				try
				{
					cqi.Call.CallPriority = await _callsService.GetCallPrioritiesByIdAsync(cqi.Call.DepartmentId, cqi.Call.Priority, false);
				}
				catch {/* Doesn't matter */}

				var dispatchedUsers = new HashSet<string>();
				// Released ids compare case-insensitively (as Call.IsUserReleased does); dispatchedUsers keeps the resolver's own
				// exact-match deduplication.
				var releasedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				// Members released (stood down) from the call are not paged again by a rebroadcast, however the call reaches them
				// (by name, group, role or unit crew): seeding them as already reached keeps every expanded route below from
				// selecting them, and the direct rows (which the resolver never deduplicates) check the set themselves.
				// Read now, not from the queued call: a release made while the broadcast waited in the queue still counts. A failed
				// read pages as before the release feature: an emergency page is not held back for a stand-down list.
				try
				{
					var releases = await scope.Resolve<Resgrid.Model.Repositories.ICallPersonnelReleasesRepository>().GetByCallIdAsync(cqi.Call.CallId);
					foreach (var release in releases ?? Enumerable.Empty<CallPersonnelRelease>())
						if (release != null && !String.IsNullOrWhiteSpace(release.UserId))
						{
							dispatchedUsers.Add(release.UserId);
							releasedUsers.Add(release.UserId);
						}
				}
				catch (Exception ex) { Logging.LogException(ex, $"Call {cqi.Call.CallId} broadcast: released members could not be read; sending to every dispatched member."); }

				// Everyone this broadcast reached, for dispatch re-notification (a no-op unless the department turned it on).
				var broadcastStartedUtc = DateTime.UtcNow;
				var renotifyRecipients = new List<DispatchRenotifyRecipient>();

				var _departmentsService = scope.Resolve<IDepartmentsService>();

				var department = await _departmentsService.GetDepartmentByIdAsync(cqi.Call.DepartmentId);
				cqi.Call.Department = department;

				StartDispatchVoicePreWarm(cqi);

				// Resolve one route at a time: first sends retain their existing position relative to database
				// reads. A preview supplies all routes to the same pure resolver without calling this sender.
				var routingTimeUtc = DateTime.UtcNow;
				// renotifySourceId: the call dispatch the re-notification sweep checks the member is still on, when that is not
				// the route's own source (a unit's station group is reached through the dispatched unit).
				async Task SendExpandedAsync(DispatchRoute route, string renotifySourceId = null)
				{
					var selection = DispatchRecipientResolver.Resolve(routingTimeUtc, new[] { route }, dispatchedUsers);
					foreach (var decision in selection.Decisions)
						DispatchTraceTelemetry.Observe(decision.Selected ? DispatchTraceStage.Selected : DispatchTraceStage.Excluded, reason: !decision.Selected ? DispatchTraceReason.DuplicateRoute : decision.EmptyShiftFallback ? DispatchTraceReason.EmptyShiftFallback : DispatchTraceReason.None,
							recipientId: decision.UserId, routeKind: decision.Kind, sourceId: decision.SourceId, inputAsOfUtc: selection.AsOfUtc);
					foreach (var userId in selection.SelectedUserIds)
					{
						dispatchedUsers.Add(userId);
						if (releasedUsers.Contains(userId))
						{
							DispatchTraceTelemetry.Observe(DispatchTraceStage.Skipped, reason: DispatchTraceReason.MemberIneligible, recipientId: userId);
							continue;
						}

						try
						{
							var profile = cqi.Profiles.FirstOrDefault(x => x.UserId == userId);
							if (await _communicationService.SendCallAsync(cqi.Call, new CallDispatch { UserId = userId }, cqi.DepartmentTextNumber, cqi.Call.DepartmentId, profile, cqi.Address))
								renotifyRecipients.Add(DispatchRenotifyRecipient.ForUser(userId, RenotifySourceFor(route.Kind), renotifySourceId ?? route.SourceId));
						}
						catch (SocketException) { }
						catch (Exception ex) { Logging.LogException(ex); }
					}
				}

				if (cqi.Call.Dispatches != null)
					foreach (var dispatch in cqi.Call.Dispatches)
					{
						// A released member keeps their direct dispatch row on the record; it does not page them.
						if (dispatch != null && releasedUsers.Contains(dispatch.UserId ?? String.Empty))
						{
							DispatchTraceTelemetry.Observe(DispatchTraceStage.Skipped, reason: DispatchTraceReason.MemberIneligible, recipientId: dispatch.UserId);
							continue;
						}

						try
						{
							var selection = DispatchRecipientResolver.Resolve(routingTimeUtc,
								new[] { new DispatchRoute(DispatchRouteKind.Direct, "direct", new[] { dispatch.UserId }) }, dispatchedUsers);
							foreach (var decision in selection.Decisions)
								DispatchTraceTelemetry.Observe(decision.Selected ? DispatchTraceStage.Selected : DispatchTraceStage.Excluded, reason: !decision.Selected ? DispatchTraceReason.DuplicateRoute : decision.EmptyShiftFallback ? DispatchTraceReason.EmptyShiftFallback : DispatchTraceReason.None,
									recipientId: decision.UserId, routeKind: decision.Kind, sourceId: decision.SourceId, inputAsOfUtc: selection.AsOfUtc);
							foreach (var userId in selection.SelectedUserIds)
							{
								dispatchedUsers.Add(userId);
								var profile = cqi.Profiles.FirstOrDefault(x => x.UserId == userId);
								if (profile != null)
								{
									if (await _communicationService.SendCallAsync(cqi.Call, dispatch, cqi.DepartmentTextNumber, cqi.Call.DepartmentId, profile, cqi.Address))
										renotifyRecipients.Add(DispatchRenotifyRecipient.ForUser(userId, DispatchRenotifySourceTypes.User, userId));
								}
								else DispatchTraceTelemetry.Observe(DispatchTraceStage.Skipped, reason: DispatchTraceReason.ProfileMissing, recipientId: userId);
							}
						}
						catch (Exception ex) { Logging.LogException(ex); }
					}

				var _departmentGroupsService = scope.Resolve<IDepartmentGroupsService>();

				if (cqi.Call.GroupDispatches != null && cqi.Call.GroupDispatches.Any())
				{
					var _shiftsService = scope.Resolve<IShiftsService>();
					var useShift = await _departmentSettingsService.GetDispatchShiftInsteadOfGroupAsync(cqi.Call.DepartmentId);
					routingTimeUtc = DateTime.UtcNow;
					var onDuty = useShift
						? await _shiftsService.GetOnDutyUserIdsForGroupsAsync(cqi.Call.DepartmentId, cqi.Call.GroupDispatches.Select(x => x.DepartmentGroupId), routingTimeUtc)
						: null;
					onDuty ??= new Dictionary<int, List<string>>();
					foreach (var dispatch in cqi.Call.GroupDispatches)
					{
						if (!groupIds.Contains(dispatch.DepartmentGroupId)) groupIds.Add(dispatch.DepartmentGroupId);
						onDuty.TryGetValue(dispatch.DepartmentGroupId, out var roster);
						roster ??= new List<string>();
						// Do not add a new group-members read when the resolved roster is sufficient.
						var members = useShift && roster.Count > 0 ? Array.Empty<string>() :
							(await _departmentGroupsService.GetAllMembersForGroupAsync(dispatch.DepartmentGroupId)).Select(x => x.UserId).ToArray();
						await SendExpandedAsync(new DispatchRoute(DispatchRouteKind.Group, dispatch.DepartmentGroupId.ToString(), members, useShift, roster));
					}
				}

				// A unit released (stood down) from the call is not paged again by a rebroadcast of it, including one released
				// while this broadcast waited in the queue: the queued units are kept only while still live on the call now. A
				// failed read pages the queued units, as a failed release read does for members.
				var liveUnitDispatches = cqi.Call.GetActiveUnitDispatches();
				if (liveUnitDispatches.Any() && cqi.Call.CallId > 0)
				{
					try
					{
						var current = await scope.Resolve<Resgrid.Model.Repositories.ICallDispatchUnitRepository>().GetCallUnitDispatchesByCallIdAsync(cqi.Call.CallId);
						var stillLive = CallDispatchUnitReleases.ActiveUnitIds(current);
						liveUnitDispatches = liveUnitDispatches.Where(x => stillLive.Contains(x.UnitId)).ToList();
					}
					catch (Exception ex) { Logging.LogException(ex, $"Call {cqi.Call.CallId} broadcast: the call's units could not be re-read; paging the queued units."); }
				}
				if (liveUnitDispatches.Any())
				{
					var _unitsService = scope.Resolve<IUnitsService>();
					var crew = await _departmentSettingsService.GetUnitDispatchAlsoDispatchToAssignedPersonnelAsync(cqi.Call.DepartmentId);
					var group = await _departmentSettingsService.GetUnitDispatchAlsoDispatchToGroupAsync(cqi.Call.DepartmentId);
					foreach (var dispatch in liveUnitDispatches)
					{
						var unit = await _unitsService.GetUnitByIdAsync(dispatch.UnitId);
						if (unit?.StationGroupId != null && !groupIds.Contains(unit.StationGroupId.Value)) groupIds.Add(unit.StationGroupId.Value);
						if (await _communicationService.SendUnitCallAsync(cqi.Call, dispatch, cqi.DepartmentTextNumber, cqi.Address))
							renotifyRecipients.Add(DispatchRenotifyRecipient.ForUnit(dispatch.UnitId));
						if (crew)
						{
							var members = await _unitsService.GetCurrentRolesForUnitAsync(dispatch.UnitId);
							if (members != null) await SendExpandedAsync(new DispatchRoute(DispatchRouteKind.UnitCrew, dispatch.UnitId.ToString(), members.Select(x => x.UserId).ToArray()));
						}
						if (group && unit?.StationGroupId != null)
						{
							var members = await _departmentGroupsService.GetAllMembersForGroupAsync(unit.StationGroupId.Value);
							await SendExpandedAsync(new DispatchRoute(DispatchRouteKind.UnitGroup, unit.StationGroupId.Value.ToString(), members.Select(x => x.UserId).ToArray()),
									dispatch.UnitId.ToString());
						}
					}
				}

				if (cqi.Call.RoleDispatches != null && cqi.Call.RoleDispatches.Any())
				{
					var _rolesService = scope.Resolve<IPersonnelRolesService>();
					foreach (var dispatch in cqi.Call.RoleDispatches)
					{
						var members = await _rolesService.GetAllMembersOfRoleAsync(dispatch.RoleId);
						await SendExpandedAsync(new DispatchRoute(DispatchRouteKind.Role, dispatch.RoleId.ToString(), members.Select(x => x.UserId).ToArray()));
					}
				}

				await RegisterRenotificationsAsync(scope, cqi, renotifyRecipients, broadcastStartedUtc);

				// Send Call Print to Printer
				var _printerProvider = scope.Resolve<IPrinterProvider>();

				Dictionary<int, DepartmentGroup> fetchedGroups = new Dictionary<int, DepartmentGroup>();
				if (cqi.Call.Dispatches != null && cqi.Call.Dispatches.Any())
				{
					foreach (var d in cqi.Call.Dispatches)
					{
						var group = await _departmentGroupsService.GetGroupForUserAsync(d.UserId, cqi.Call.DepartmentId);

						if (group != null)
						{
							if (!groupIds.Contains(group.DepartmentGroupId))
								groupIds.Add(group.DepartmentGroupId);

							if (!fetchedGroups.ContainsKey(group.DepartmentGroupId))
								fetchedGroups.Add(group.DepartmentGroupId, group);
						}
					}
				}

				foreach (var groupId in groupIds)
				{
					try
					{
						DepartmentGroup group = null;

						if (fetchedGroups.ContainsKey(groupId))
							group = fetchedGroups[groupId];
						else
							group = await _departmentGroupsService.GetGroupByIdAsync(groupId);

						if (!String.IsNullOrWhiteSpace(group.PrinterData) && group.DispatchToPrinter)
						{
							var printerData = JsonConvert.DeserializeObject<DepartmentGroupPrinter>(group.PrinterData);
							var apiKey = SymmetricEncryption.Decrypt(printerData.ApiKey, Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);
							var callUrl = await _callsService.GetShortenedCallPdfUrl(cqi.Call.CallId, true, groupId);

							var printJob = _printerProvider.SubmitPrintJob(apiKey, printerData.PrinterId, "CallPrint", callUrl);
						}
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				}
				DispatchTraceTelemetry.Observe(DispatchTraceStage.BroadcastCompleted);
			}

			return true;
		}

		/// <summary>
		/// Starts dispatch re-notification for everyone the broadcast reached, when the department has it on. A status set since
		/// the broadcast was queued acknowledges the dispatch. Never fails the broadcast.
		/// </summary>
		private static async Task RegisterRenotificationsAsync(ILifetimeScope scope, CallQueueItem cqi, List<DispatchRenotifyRecipient> recipients, DateTime broadcastStartedUtc)
		{
			if (recipients == null || recipients.Count == 0)
				return;

			try
			{
				var dispatchedOnUtc = cqi.EnqueuedOn.HasValue && cqi.EnqueuedOn.Value <= broadcastStartedUtc ? cqi.EnqueuedOn.Value : broadcastStartedUtc;
				var renotificationService = scope.Resolve<IDispatchRenotificationService>();
				await renotificationService.RegisterBroadcastAsync(cqi.Call, recipients, dispatchedOnUtc);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Could not start dispatch re-notification for call {cqi.Call?.CallId}.");
			}
		}

		/// <summary>The call dispatch a routed member came through, so the sweep can tell when it is taken off the call.</summary>
		private static DispatchRenotifySourceTypes RenotifySourceFor(DispatchRouteKind kind)
		{
			switch (kind)
			{
				case DispatchRouteKind.Group:
				case DispatchRouteKind.Shift:
					return DispatchRenotifySourceTypes.Group;
				case DispatchRouteKind.Role:
					return DispatchRenotifySourceTypes.Role;
				case DispatchRouteKind.UnitCrew:
					return DispatchRenotifySourceTypes.UnitCrew;
				case DispatchRouteKind.UnitGroup:
					return DispatchRenotifySourceTypes.UnitGroup;
				default:
					return DispatchRenotifySourceTypes.User;
			}
		}

		/// <summary>
		/// Kicks off TTS generation for the dispatch voice prompt in the background,
		/// in parallel with placing the outbound calls. Cold generation (Piper model
		/// load + synthesis + normalization) takes longer than the voice webhook's
		/// per-request budget, so without this every recipient answering during the
		/// cold window hears "please wait" loops. Ring time (typically 10-30s) absorbs
		/// the generation, so by the time anyone answers the audio URL is a cache hit.
		/// Fire-and-forget: dialing must never wait on audio generation.
		/// </summary>
		private static void StartDispatchVoicePreWarm(CallQueueItem cqi)
		{
			try
			{
				// A recorded dispatch-audio attachment is played instead of TTS.
				if (cqi.CallDispatchAttachmentId > 0)
					return;

				// No recipient takes dispatches by phone; don't generate audio nobody will hear.
				if (cqi.Profiles == null || !cqi.Profiles.Any(x => x.VoiceForCall))
					return;

				var call = cqi.Call;

				_ = Task.Run(async () =>
				{
					try
					{
						// Fire-and-forget outlives the message's scope, so it takes its own.
						using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
						var ttsAudioService = scope.Resolve<ITtsAudioService>();
						var geoLocationProvider = scope.Resolve<IGeoLocationProvider>();
						var departmentSettingsService = scope.Resolve<IDepartmentSettingsService>();

						// Text and chunking must match the Twilio voice webhook exactly —
						// the TTS cache key is a hash of the chunk text. CallPriority was
						// already populated above, mirroring the webhook's own load.
						var address = await Resgrid.Services.DispatchVoicePromptBuilder.ResolveDispatchAddressAsync(call, geoLocationProvider);
						var ttsLanguage = await departmentSettingsService.GetTtsLanguageForDepartmentAsync(call.DepartmentId);
						var dispatchText = Resgrid.Services.DispatchVoicePromptBuilder.BuildDispatchPrompt(call, address);

						foreach (var chunk in Resgrid.Services.DispatchVoicePromptBuilder.ChunkText(dispatchText))
						{
							await ttsAudioService.GenerateSpeechUrlAsync(chunk, ttsLanguage);
						}
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}
	}
}

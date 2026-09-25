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
		private static ICommunicationService _communicationService;
		private static ICallsService _callsService;
		private static IUserProfileService _userProfilesService;
		private static IDepartmentGroupsService _departmentGroupsService;
		private static IUnitsService _unitsService;
		private static IPersonnelRolesService _rolesService;
		private static IPrinterProvider _printerProvider;
		private static IDepartmentSettingsService _departmentSettingsService;
		private static IShiftsService _shiftsService;
		private static IDepartmentsService _departmentsService;

		public static async Task<bool> ProcessCallQueueItem(CallQueueItem cqi)
		{
			_communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();
			_callsService = Bootstrapper.GetKernel().Resolve<ICallsService>();
			_departmentSettingsService = Bootstrapper.GetKernel().Resolve<IDepartmentSettingsService>();
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
					if (_userProfilesService == null)
						_userProfilesService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();

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

				if (_departmentsService == null)
					_departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();

				var department = await _departmentsService.GetDepartmentByIdAsync(cqi.Call.DepartmentId);
				cqi.Call.Department = department;

				StartDispatchVoicePreWarm(cqi);

				// Resolve one route at a time: first sends retain their existing position relative to database
				// reads. A preview supplies all routes to the same pure resolver without calling this sender.
				var routingTimeUtc = DateTime.UtcNow;
				async Task SendExpandedAsync(DispatchRoute route)
				{
					var selection = DispatchRecipientResolver.Resolve(routingTimeUtc, new[] { route }, dispatchedUsers);
					foreach (var decision in selection.Decisions)
						DispatchTraceTelemetry.Observe(decision.Selected ? DispatchTraceStage.Selected : DispatchTraceStage.Excluded, reason: !decision.Selected ? DispatchTraceReason.DuplicateRoute : decision.EmptyShiftFallback ? DispatchTraceReason.EmptyShiftFallback : DispatchTraceReason.None,
							recipientId: decision.UserId, routeKind: decision.Kind, sourceId: decision.SourceId, inputAsOfUtc: selection.AsOfUtc);
					foreach (var userId in selection.SelectedUserIds)
					{
						dispatchedUsers.Add(userId);
						try
						{
							var profile = cqi.Profiles.FirstOrDefault(x => x.UserId == userId);
							await _communicationService.SendCallAsync(cqi.Call, new CallDispatch { UserId = userId }, cqi.DepartmentTextNumber, cqi.Call.DepartmentId, profile, cqi.Address);
						}
						catch (SocketException) { }
						catch (Exception ex) { Logging.LogException(ex); }
					}
				}

				if (cqi.Call.Dispatches != null)
					foreach (var dispatch in cqi.Call.Dispatches)
					{
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
									await _communicationService.SendCallAsync(cqi.Call, dispatch, cqi.DepartmentTextNumber, cqi.Call.DepartmentId, profile, cqi.Address);
								else DispatchTraceTelemetry.Observe(DispatchTraceStage.Skipped, reason: DispatchTraceReason.ProfileMissing, recipientId: userId);
							}
						}
						catch (Exception ex) { Logging.LogException(ex); }
					}

				if (_departmentGroupsService == null)
					_departmentGroupsService = Bootstrapper.GetKernel().Resolve<IDepartmentGroupsService>();

				if (cqi.Call.GroupDispatches != null && cqi.Call.GroupDispatches.Any())
				{
					if (_shiftsService == null) _shiftsService = Bootstrapper.GetKernel().Resolve<IShiftsService>();
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

				if (cqi.Call.UnitDispatches != null && cqi.Call.UnitDispatches.Any())
				{
					if (_unitsService == null) _unitsService = Bootstrapper.GetKernel().Resolve<IUnitsService>();
					var crew = await _departmentSettingsService.GetUnitDispatchAlsoDispatchToAssignedPersonnelAsync(cqi.Call.DepartmentId);
					var group = await _departmentSettingsService.GetUnitDispatchAlsoDispatchToGroupAsync(cqi.Call.DepartmentId);
					foreach (var dispatch in cqi.Call.UnitDispatches)
					{
						var unit = await _unitsService.GetUnitByIdAsync(dispatch.UnitId);
						if (unit?.StationGroupId != null && !groupIds.Contains(unit.StationGroupId.Value)) groupIds.Add(unit.StationGroupId.Value);
						await _communicationService.SendUnitCallAsync(cqi.Call, dispatch, cqi.DepartmentTextNumber, cqi.Address);
						if (crew)
						{
							var members = await _unitsService.GetCurrentRolesForUnitAsync(dispatch.UnitId);
							if (members != null) await SendExpandedAsync(new DispatchRoute(DispatchRouteKind.UnitCrew, dispatch.UnitId.ToString(), members.Select(x => x.UserId).ToArray()));
						}
						if (group && unit?.StationGroupId != null)
						{
							var members = await _departmentGroupsService.GetAllMembersForGroupAsync(unit.StationGroupId.Value);
							await SendExpandedAsync(new DispatchRoute(DispatchRouteKind.UnitGroup, unit.StationGroupId.Value.ToString(), members.Select(x => x.UserId).ToArray()));
						}
					}
				}

				if (cqi.Call.RoleDispatches != null && cqi.Call.RoleDispatches.Any())
				{
					if (_rolesService == null) _rolesService = Bootstrapper.GetKernel().Resolve<IPersonnelRolesService>();
					foreach (var dispatch in cqi.Call.RoleDispatches)
					{
						var members = await _rolesService.GetAllMembersOfRoleAsync(dispatch.RoleId);
						await SendExpandedAsync(new DispatchRoute(DispatchRouteKind.Role, dispatch.RoleId.ToString(), members.Select(x => x.UserId).ToArray()));
					}
				}

				// Send Call Print to Printer
				_printerProvider = Bootstrapper.GetKernel().Resolve<IPrinterProvider>();

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
						var ttsAudioService = Bootstrapper.GetKernel().Resolve<ITtsAudioService>();
						var geoLocationProvider = Bootstrapper.GetKernel().Resolve<IGeoLocationProvider>();
						var departmentSettingsService = Bootstrapper.GetKernel().Resolve<IDepartmentSettingsService>();

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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Autofac;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
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

			if (cqi != null && cqi.Call != null && cqi.Call.HasAnyDispatches())
			{
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

				// Dispatch Personnel
				if (cqi.Call.Dispatches != null && cqi.Call.Dispatches.Any())
				{
					foreach (var d in cqi.Call.Dispatches)
					{
						try
						{
							dispatchedUsers.Add(d.UserId);

							var profile = cqi.Profiles.FirstOrDefault(x => x.UserId == d.UserId);

							if (profile != null)
							{
								await _communicationService.SendCallAsync(cqi.Call, d, cqi.DepartmentTextNumber, cqi.Call.DepartmentId, profile, cqi.Address);
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}
				}

				if (_departmentGroupsService == null)
					_departmentGroupsService = Bootstrapper.GetKernel().Resolve<IDepartmentGroupsService>();

				// Dispatch Groups
				if (cqi.Call.GroupDispatches != null && cqi.Call.GroupDispatches.Any())
				{
					if (_shiftsService == null)
						_shiftsService = Bootstrapper.GetKernel().Resolve<IShiftsService>();

					var dispatchShiftInsteadOfGroup = await _departmentSettingsService.GetDispatchShiftInsteadOfGroupAsync(cqi.Call.DepartmentId);
					var localizedDate = TimeConverterHelper.TimeConverter(DateTime.UtcNow, department);
					var shiftDate = new DateTime(localizedDate.Year, localizedDate.Month, localizedDate.Day);

					foreach (var d in cqi.Call.GroupDispatches)
					{
						if (!groupIds.Contains(d.DepartmentGroupId))
							groupIds.Add(d.DepartmentGroupId);

						var signups = await _shiftsService.GetShiftSignupsByDepartmentGroupIdAndDayAsync(d.DepartmentGroupId, shiftDate);

						if (dispatchShiftInsteadOfGroup && (signups != null && signups.Any()))
							continue;

						var members = await _departmentGroupsService.GetAllMembersForGroupAsync(d.DepartmentGroupId);

						foreach (var member in members)
						{
							if (!dispatchedUsers.Contains(member.UserId))
							{
								dispatchedUsers.Add(member.UserId);
								try
								{
									var profile = cqi.Profiles.FirstOrDefault(x => x.UserId == member.UserId);
									await _communicationService.SendCallAsync(cqi.Call, new CallDispatch() { UserId = member.UserId }, cqi.DepartmentTextNumber, cqi.Call.DepartmentId, profile, cqi.Address);
								}
								catch (SocketException sex)
								{
								}
								catch (Exception ex)
								{
									Logging.LogException(ex);
								}

							}
						}
					}
				}

				// Dispatch Units
				if (cqi.Call.UnitDispatches != null && cqi.Call.UnitDispatches.Any())
				{
					if (_unitsService == null)
						_unitsService = Bootstrapper.GetKernel().Resolve<IUnitsService>();

					bool alsoDispatchToAssignedPersonnel = await _departmentSettingsService.GetUnitDispatchAlsoDispatchToAssignedPersonnelAsync(cqi.Call.DepartmentId);
					bool alsoDispatchToGroup = await _departmentSettingsService.GetUnitDispatchAlsoDispatchToGroupAsync(cqi.Call.DepartmentId);

					foreach (var d in cqi.Call.UnitDispatches)
					{
						var unit = await _unitsService.GetUnitByIdAsync(d.UnitId);

						if (unit != null && unit.StationGroupId.HasValue)
							if (!groupIds.Contains(unit.StationGroupId.Value))
								groupIds.Add(unit.StationGroupId.Value);

						await _communicationService.SendUnitCallAsync(cqi.Call, d, cqi.DepartmentTextNumber, cqi.Address);

						if (alsoDispatchToAssignedPersonnel)
						{
							var unitAssignedMembers = await _unitsService.GetCurrentRolesForUnitAsync(d.UnitId);
							if (unitAssignedMembers != null && unitAssignedMembers.Count() > 0)
							{
								foreach (var member in unitAssignedMembers)
								{
									if (!dispatchedUsers.Contains(member.UserId))
									{
										dispatchedUsers.Add(member.UserId);
										try
										{
											var profile = cqi.Profiles.FirstOrDefault(x => x.UserId == member.UserId);
											await _communicationService.SendCallAsync(cqi.Call,
												new CallDispatch() { UserId = member.UserId }, cqi.DepartmentTextNumber,
												cqi.Call.DepartmentId, profile, cqi.Address);
										}
										catch (SocketException sex)
										{
										}
										catch (Exception ex)
										{
											Logging.LogException(ex);
										}

									}
								}
							}
						}

						if (alsoDispatchToGroup)
						{
							if (unit.StationGroupId.HasValue)
							{
								var members = await _departmentGroupsService.GetAllMembersForGroupAsync(unit.StationGroupId.Value);

								foreach (var member in members)
								{
									if (!dispatchedUsers.Contains(member.UserId))
									{
										dispatchedUsers.Add(member.UserId);
										try
										{
											var profile = cqi.Profiles.FirstOrDefault(x => x.UserId == member.UserId);
											await _communicationService.SendCallAsync(cqi.Call, new CallDispatch() { UserId = member.UserId }, cqi.DepartmentTextNumber, cqi.Call.DepartmentId, profile, cqi.Address);
										}
										catch (SocketException sex)
										{
										}
										catch (Exception ex)
										{
											Logging.LogException(ex);
										}

									}
								}
							}
						}
					}
				}

				// Dispatch Roles
				if (cqi.Call.RoleDispatches != null && cqi.Call.RoleDispatches.Any())
				{
					if (_rolesService == null)
						_rolesService = Bootstrapper.GetKernel().Resolve<IPersonnelRolesService>();

					foreach (var d in cqi.Call.RoleDispatches)
					{
						var members = await _rolesService.GetAllMembersOfRoleAsync(d.RoleId);

						foreach (var member in members)
						{
							if (!dispatchedUsers.Contains(member.UserId))
							{
								dispatchedUsers.Add(member.UserId);
								try
								{
									var profile = cqi.Profiles.FirstOrDefault(x => x.UserId == member.UserId);
									await _communicationService.SendCallAsync(cqi.Call, new CallDispatch() { UserId = member.UserId }, cqi.DepartmentTextNumber, cqi.Call.DepartmentId, profile, cqi.Address);
								}
								catch (SocketException sex)
								{
								}
								catch (Exception ex)
								{
									Logging.LogException(ex);
								}

							}
						}
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
			}

			return true;
		}
	}
}

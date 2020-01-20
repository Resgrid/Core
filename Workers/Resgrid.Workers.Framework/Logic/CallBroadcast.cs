using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Autofac;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	public class BroadcastCallLogic
	{
		private IQueueService _queueService;
		private QueueClient _client = null;

		public BroadcastCallLogic()
		{
			while (_client == null)
			{
				try
				{
					_client = QueueClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureQueueConnectionString, Config.ServiceBusConfig.CallBroadcastQueueName);
				}
				catch (TimeoutException) { }
			}
		}

		public void Process(CallQueueItem item)
		{
			bool success = true;

			if (Config.SystemBehaviorConfig.IsAzure)
			{
				ProcessQueueMessage(_client.Receive());
			}
			else
			{
				ProcessCallQueueItem(item);
			}

			_queueService = null;
		}

		public static Tuple<bool, string> ProcessQueueMessage(BrokeredMessage message)
		{
			bool success = true;
			string result = "";

			if (message != null)
			{
				try
				{
					var body = message.GetBody<string>();

					if (!String.IsNullOrWhiteSpace(body))
					{
						CallQueueItem cqi = null;
						try
						{
							cqi = ObjectSerialization.Deserialize<CallQueueItem>(body);
						}
						catch (Exception ex)
						{
							success = false;
							result = "Unable to parse message body Exception: " + ex.ToString();
							message.DeadLetter();
						}

						if (cqi != null && cqi.Call != null && cqi.Call.HasAnyDispatches())
						{
							try
							{
								ProcessCallQueueItem(cqi);
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								message.Abandon();

								success = false;
								result = ex.ToString();
							}
						}
					}
					else
					{
						success = false;
						result = "Message body is null or empty";
					}

					try
					{
						message.Complete();
					}
					catch (MessageLockLostException)
					{

					}
				}
				catch (Exception ex)
				{
					success = false;
					result = ex.ToString();

					Logging.LogException(ex);
					message.Abandon();
				}
			}

			return new Tuple<bool, string>(success, result);
		}

		public static void ProcessCallQueueItem(CallQueueItem cqi)
		{
			ICommunicationService _communicationService;
			ICallsService _callsService;

			try
			{
				if (cqi != null && cqi.Call != null && cqi.Call.HasAnyDispatches())
				{
					_communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();
					_callsService = Bootstrapper.GetKernel().Resolve<ICallsService>();

					List<int> groupIds = new List<int>();

					/* Trying to see if I can eek out a little perf here now that profiles are in Redis. Previously the
						 * the parallel operation would cause EF errors. This shouldn't be the case now because profiles are
						 * cached and GetProfileForUser operations will hit that first.
						 */
					if (cqi.Profiles == null || !cqi.Profiles.Any())
					{
						var userProfilesService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
						cqi.Profiles = userProfilesService.GetAllProfilesForDepartment(cqi.Call.DepartmentId).Select(x => x.Value).ToList();
					}

					if (cqi.CallDispatchAttachmentId > 0)
					{
						//var callsService = Bootstrapper.GetKernel().Resolve<ICallsService>();
						cqi.Call.ShortenedAudioUrl = _callsService.GetShortenedAudioUrl(cqi.Call.CallId, cqi.CallDispatchAttachmentId);
					}

					cqi.Call.ShortenedCallUrl = _callsService.GetShortenedCallLinkUrl(cqi.Call.CallId);

					try
					{
						cqi.Call.CallPriority = _callsService.GetCallPrioritesById(cqi.Call.DepartmentId, cqi.Call.Priority, false);
					}
					catch {/* Doesn't matter */}

					var dispatchedUsers = new HashSet<string>();

					// Dispatch Personnel
					if (cqi.Call.Dispatches != null && cqi.Call.Dispatches.Any())
					{
						foreach (var d in cqi.Call.Dispatches)
						{
							dispatchedUsers.Add(d.UserId);
						}

						Parallel.ForEach(cqi.Call.Dispatches, d =>
						{
							try
							{
								var profile = cqi.Profiles.FirstOrDefault(x => x.UserId == d.UserId);

								if (profile != null)
								{
									_communicationService.SendCall(cqi.Call, d, cqi.DepartmentTextNumber, cqi.Call.DepartmentId, profile, cqi.Address);
								}
							}
							catch (SocketException sex)
							{
							}
						});
					}

					var departmentGroupsService = Bootstrapper.GetKernel().Resolve<IDepartmentGroupsService>();

					// Dispatch Groups
					if (cqi.Call.GroupDispatches != null && cqi.Call.GroupDispatches.Any())
					{
						foreach (var d in cqi.Call.GroupDispatches)
						{
							if (!groupIds.Contains(d.DepartmentGroupId))
								groupIds.Add(d.DepartmentGroupId);

							var members = departmentGroupsService.GetAllMembersForGroup(d.DepartmentGroupId);

							foreach (var member in members)
							{
								if (!dispatchedUsers.Contains(member.UserId))
								{
									dispatchedUsers.Add(member.UserId);
									try
									{
										var profile = cqi.Profiles.FirstOrDefault(x => x.UserId == member.UserId);
										_communicationService.SendCall(cqi.Call, new CallDispatch() { UserId = member.UserId }, cqi.DepartmentTextNumber, cqi.Call.DepartmentId, profile, cqi.Address);
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
						var unitsService = Bootstrapper.GetKernel().Resolve<IUnitsService>();

						foreach (var d in cqi.Call.UnitDispatches)
						{
							var unit = unitsService.GetUnitById(d.UnitId);

							if (unit != null && unit.StationGroupId.HasValue)
								if (!groupIds.Contains(unit.StationGroupId.Value))
									groupIds.Add(unit.StationGroupId.Value);

							_communicationService.SendUnitCall(cqi.Call, d, cqi.DepartmentTextNumber, cqi.Address);

							var unitAssignedMembers = unitsService.GetCurrentRolesForUnit(d.UnitId);

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
											_communicationService.SendCall(cqi.Call, new CallDispatch() { UserId = member.UserId }, cqi.DepartmentTextNumber, cqi.Call.DepartmentId, profile, cqi.Address);
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
							else
							{
								if (unit.StationGroupId.HasValue)
								{
									var members = departmentGroupsService.GetAllMembersForGroup(unit.StationGroupId.Value);

									foreach (var member in members)
									{
										if (!dispatchedUsers.Contains(member.UserId))
										{
											dispatchedUsers.Add(member.UserId);
											try
											{
												var profile = cqi.Profiles.FirstOrDefault(x => x.UserId == member.UserId);
												_communicationService.SendCall(cqi.Call, new CallDispatch() { UserId = member.UserId }, cqi.DepartmentTextNumber, cqi.Call.DepartmentId, profile, cqi.Address);
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
						var rolesService = Bootstrapper.GetKernel().Resolve<IPersonnelRolesService>();

						foreach (var d in cqi.Call.RoleDispatches)
						{
							var members = rolesService.GetAllMembersOfRole(d.RoleId);

							foreach (var member in members)
							{
								if (!dispatchedUsers.Contains(member.UserId))
								{
									dispatchedUsers.Add(member.UserId);
									try
									{
										var profile = cqi.Profiles.FirstOrDefault(x => x.UserId == member.UserId);
										_communicationService.SendCall(cqi.Call, new CallDispatch() { UserId = member.UserId }, cqi.DepartmentTextNumber, cqi.Call.DepartmentId, profile, cqi.Address);
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
					var printerProvider = Bootstrapper.GetKernel().Resolve<IPrinterProvider>();

					Dictionary<int, DepartmentGroup> fetchedGroups = new Dictionary<int, DepartmentGroup>();
					if (cqi.Call.Dispatches != null && cqi.Call.Dispatches.Any())
					{
						foreach (var d in cqi.Call.Dispatches)
						{
							var group = departmentGroupsService.GetGroupForUser(d.UserId, cqi.Call.DepartmentId);

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
								group = departmentGroupsService.GetGroupById(groupId);

							if (!String.IsNullOrWhiteSpace(group.PrinterData) && group.DispatchToPrinter)
							{
								var printerData = JsonConvert.DeserializeObject<DepartmentGroupPrinter>(group.PrinterData);
								var apiKey = SymmetricEncryption.Decrypt(printerData.ApiKey, Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);
								var callUrl = _callsService.GetShortenedCallPdfUrl(cqi.Call.CallId, true, groupId);

								var printJob = printerProvider.SubmitPrintJob(apiKey, printerData.PrinterId, "CallPrint", callUrl);
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}
				}
			}
			finally
			{
				_communicationService = null;
			}
		}
	}
}

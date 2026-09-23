using System;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Stripe;
using Resgrid.Model.Repositories;
using Resgrid.Model.Events;
using KellermanSoftware.CompareNetObjects;
using Resgrid.Model.Identity;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Providers;
using Newtonsoft.Json;
using Stripe.Checkout;

namespace Resgrid.Workers.Framework.Logic
{
	public class SystemQueueLogic
	{
		public static async Task<bool> ProcessSystemQueueItem(CqrsEvent qi, CancellationToken cancellationToken = default(CancellationToken))
		{
			bool success = true;

			if (qi != null)
			{
				switch ((CqrsEventTypes)qi.Type)
				{
					case CqrsEventTypes.None:
						break;
					case CqrsEventTypes.PushRegistration:

						PushUri data = null;
						try
						{
							data = ObjectSerialization.Deserialize<PushUri>(qi.Data);
						}
						catch (Exception ex)
						{
							// Silently dropping this left the device permanently unregistered with no record
							// of the attempt anywhere in the pipeline.
							Logging.LogException(ex, "Failed to deserialize a PushRegistration queue item; the device will not receive pushes.");
						}

						if (data == null)
						{
							Logging.LogWarning("PushRegistration queue item produced no PushUri; registration skipped.");
						}
						else
						{
							var pushService = Bootstrapper.GetKernel().Resolve<IPushService>();
							var resgriterResult = await pushService.Register(data);

							if (!resgriterResult)
								Logging.LogError($"PushRegistration failed for user {data.UserId} (platform {data.PlatformType}, prefix '{data.PushLocation}', source '{data.Source}').");

							pushService = null;
						}
						break;
					case CqrsEventTypes.UnitPushRegistration:
						PushRegisterionEvent unitData = null;
						try
						{
							unitData = ObjectSerialization.Deserialize<PushRegisterionEvent>(qi.Data);

							if (unitData != null)
							{
								PushUri pushUri = new PushUri();
								pushUri.PushUriId = unitData.PushUriId;
								pushUri.UserId = unitData.UserId;
								pushUri.PlatformType = unitData.PlatformType;
								pushUri.PushLocation = unitData.PushLocation;
								pushUri.DepartmentId = unitData.DepartmentId;
								pushUri.UnitId = unitData.UnitId;
								pushUri.DeviceId = unitData.DeviceId;
								pushUri.Uuid = unitData.Uuid;

								var pushService = Bootstrapper.GetKernel().Resolve<IPushService>();

								await pushService.UnRegisterUnit(pushUri);
								var unitResult = await pushService.RegisterUnit(pushUri);

								if (!unitResult)
									Logging.LogError($"UnitPushRegistration failed for unit {unitData.UnitId} (platform {unitData.PlatformType}, prefix '{unitData.PushLocation}').");

								pushService = null;
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
						break;
					case CqrsEventTypes.ClearDepartmentCache:

						int departmentId;

						if (int.TryParse(qi.Data, out departmentId))
						{
							var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
							//var departmentSettingsService = Bootstrapper.GetKernel().Resolve<IDepartmentSettingsService>();
							var subscriptionService = Bootstrapper.GetKernel().Resolve<ISubscriptionsService>();
							//var scheduledTasksService = Bootstrapper.GetKernel().Resolve<IScheduledTasksService>();
							var departmentService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
							var actionLogsService = Bootstrapper.GetKernel().Resolve<IActionLogsService>();
							var customStatesService = Bootstrapper.GetKernel().Resolve<ICustomStateService>();
							var usersService = Bootstrapper.GetKernel().Resolve<IUsersService>();

							subscriptionService.ClearCacheForCurrentPayment(departmentId);
							departmentService.InvalidateDepartmentUsersInCache(departmentId);
							departmentService.InvalidateDepartmentInCache(departmentId);
							departmentService.InvalidatePersonnelNamesInCache(departmentId);
							userProfileService.ClearAllUserProfilesFromCache(departmentId);
							usersService.ClearCacheForDepartment(departmentId);
							actionLogsService.InvalidateActionLogs(departmentId);
							customStatesService.InvalidateCustomStateInCache(departmentId);
							departmentService.InvalidateDepartmentMembers();

							userProfileService = null;
							subscriptionService = null;
							departmentService = null;
							actionLogsService = null;
							customStatesService = null;
							usersService = null;
						}
						break;
					case CqrsEventTypes.NewChatMessage:
						NewChatNotificationEvent newChatEvent = null;

						if (qi != null && !String.IsNullOrWhiteSpace(qi.Data))
						{
							try
							{
								newChatEvent = ObjectSerialization.Deserialize<NewChatNotificationEvent>(qi.Data);
							}
							catch (Exception ex)
							{

							}

							if (newChatEvent != null)
							{
								var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
								var communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();
								var usersService = Bootstrapper.GetKernel().Resolve<IUsersService>();


								if (newChatEvent != null && newChatEvent.RecipientUserIds != null && newChatEvent.RecipientUserIds.Count > 0)
								{
									List<UserProfile> profiles = new List<UserProfile>();
									if (newChatEvent.RecipientUserIds.Count == 1)
									{
										profiles.Add(await userProfileService.GetProfileByUserIdAsync(newChatEvent.RecipientUserIds.First()));
									}
									else
									{
										profiles.AddRange(await userProfileService.GetSelectedUserProfilesAsync(newChatEvent.RecipientUserIds));
									}

									var sendingUserProfile = await userProfileService.GetProfileByUserIdAsync(newChatEvent.SendingUserId);

									var chatResult = await communicationService.SendChat(newChatEvent.Id, newChatEvent.DepartmentId, newChatEvent.SendingUserId, newChatEvent.GroupName, newChatEvent.Message, sendingUserProfile, profiles);
								}

								userProfileService = null;
								communicationService = null;
								usersService = null;
							}
						}
						break;
					case CqrsEventTypes.TroubleAlert:
						TroubleAlertEvent troubleAlertEvent = null;
						try
						{
							troubleAlertEvent = ObjectSerialization.Deserialize<TroubleAlertEvent>(qi.Data);
						}
						catch (Exception ex)
						{

						}

						if (troubleAlertEvent != null && troubleAlertEvent.DepartmentId.HasValue)
						{
							var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
							var communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();
							var usersService = Bootstrapper.GetKernel().Resolve<IUsersService>();
							var departmentService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
							var unitsService = Bootstrapper.GetKernel().Resolve<IUnitsService>();
							var departmentGroupService = Bootstrapper.GetKernel().Resolve<IDepartmentGroupsService>();
							var callsService = Bootstrapper.GetKernel().Resolve<ICallsService>();
							var departmentSettingsService = Bootstrapper.GetKernel().Resolve<IDepartmentSettingsService>();
							var geoLocationProvider = Bootstrapper.GetKernel().Resolve<IGeoLocationProvider>();

							var admins = await departmentService.GetAllAdminsForDepartmentAsync(troubleAlertEvent.DepartmentId.Value);
							var unit = await unitsService.GetUnitByIdAsync(troubleAlertEvent.UnitId);
							List<UserProfile> profiles = new List<UserProfile>();
							Call call = null;
							string departmentNumber = "";
							string callAddress = "No Call Address";
							string unitAproxAddress = "Unknown Unit Address";

							departmentNumber = await departmentSettingsService.GetTextToCallNumberForDepartmentAsync(troubleAlertEvent.DepartmentId.Value);

							if (admins != null)
								profiles.AddRange(await userProfileService.GetSelectedUserProfilesAsync(admins.Select(x => x.Id).ToList()));

							if (unit != null)
							{
								if (unit.StationGroupId.HasValue)
								{
									var groupAdmins = await departmentGroupService.GetAllAdminsForGroupAsync(unit.StationGroupId.Value);

									if (groupAdmins != null)
										profiles.AddRange(await userProfileService.GetSelectedUserProfilesAsync(groupAdmins.Select(x => x.UserId).ToList()));
								}

								if (troubleAlertEvent.CallId.HasValue && troubleAlertEvent.CallId.GetValueOrDefault() > 0)
								{
									call = await callsService.GetCallByIdAsync(troubleAlertEvent.CallId.Value);

									if (!String.IsNullOrEmpty(call.Address))
										callAddress = call.Address;
									else if (!String.IsNullOrEmpty(call.GeoLocationData) && call.GeoLocationData.Length > 1)
									{
										string[] points = call.GeoLocationData.Split(char.Parse(","));

										if (points != null && points.Length == 2)
										{
											callAddress = await geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1]));
										}
									}
								}

								if (!String.IsNullOrWhiteSpace(troubleAlertEvent.Latitude) && !String.IsNullOrWhiteSpace(troubleAlertEvent.Longitude))
								{
									unitAproxAddress = await geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(troubleAlertEvent.Latitude), double.Parse(troubleAlertEvent.Longitude));
								}

								await communicationService.SendTroubleAlertAsync(troubleAlertEvent, unit, call, departmentNumber, troubleAlertEvent.DepartmentId.Value, callAddress, unitAproxAddress, profiles);
							}
						}

						break;
					case CqrsEventTypes.AuditLog:
						AuditEvent auditEvent = null;
						try
						{
							auditEvent = ObjectSerialization.Deserialize<AuditEvent>(qi.Data);
						}
						catch (Exception ex)
						{

						}

						if (auditEvent != null)
						{
							// Same row as the audit queue writes (actor, IP, user agent, subject, fallback message); the copy of its switch
							// that used to live here had drifted from it.
							var auditLogsRepository = Bootstrapper.GetKernel().Resolve<IAuditLogsRepository>();
							var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
							var auditService = Bootstrapper.GetKernel().Resolve<IAuditService>();

							var auditLog = await AuditQueueLogic.BuildAuditLogAsync(auditEvent, userProfileService, auditService);
							await auditLogsRepository.SaveOrUpdateAsync(auditLog, cancellationToken);
						}
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}

			return success;
		}
	}
}

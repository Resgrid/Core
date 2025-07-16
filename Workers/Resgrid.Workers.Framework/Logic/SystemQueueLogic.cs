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

						}

						if (data != null)
						{
							var pushService = Bootstrapper.GetKernel().Resolve<IPushService>();
							var resgriterResult = await pushService.Register(data);

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
							var auditLogsRepository = Bootstrapper.GetKernel().Resolve<IAuditLogsRepository>();
							var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();

							var profile = await userProfileService.GetProfileByUserIdAsync(auditEvent.UserId);

							var auditLog = new AuditLog();
							auditLog.DepartmentId = auditEvent.DepartmentId;
							auditLog.UserId = auditEvent.UserId;
							auditLog.LogType = (int)auditEvent.Type;

							switch (auditEvent.Type)
							{
								case AuditLogTypes.DepartmentSettingsChanged:
									auditLog.Message = string.Format("{0} updated the department settings", profile.FullName.AsFirstNameLastName);
									var compareLogic = new CompareLogic();
									var departmentSettingsChangedBefore = JsonConvert.DeserializeObject<Department>(auditEvent.Before);
									var departmentSettingsChangedAfter = JsonConvert.DeserializeObject<Department>(auditEvent.After);
									ComparisonResult auditCompareResult = compareLogic.Compare(departmentSettingsChangedBefore, departmentSettingsChangedAfter);
									auditLog.Data = auditCompareResult.DifferencesString;
									break;
								case AuditLogTypes.UserAdded:
									if (!String.IsNullOrWhiteSpace(auditEvent.After))
									{
										var userAddedIdentityUser = JsonConvert.DeserializeObject<IdentityUser>(auditEvent.After);
										var newProfile = await userProfileService.GetProfileByUserIdAsync(userAddedIdentityUser.UserId);
										auditLog.Message = string.Format("{0} added new user {1}", profile.FullName.AsFirstNameLastName, newProfile.FullName.AsFirstNameLastName);

										auditLog.Data = $"New UserId: {newProfile.UserId}";
									}
									break;
								case AuditLogTypes.UserRemoved:

									if (!String.IsNullOrWhiteSpace(auditEvent.Before))
									{
										var userRemovedIdentityUser = JsonConvert.DeserializeObject<UserProfile>(auditEvent.Before);
										auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed user {userRemovedIdentityUser.FullName.AsFirstNameLastName}";
										auditLog.Data = "No Data";
									}

									break;
								case AuditLogTypes.GroupAdded:
									if (!String.IsNullOrWhiteSpace(auditEvent.After))
									{
										var groupAddedGroup = JsonConvert.DeserializeObject<DepartmentGroup>(auditEvent.After);
										if (groupAddedGroup.Type.HasValue && groupAddedGroup.Type.Value == (int)DepartmentGroupTypes.Station)
											auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added station group {groupAddedGroup.Name}";
										else
											auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added organizational group {groupAddedGroup.Name}";

										auditLog.Data = $"GroupId: {groupAddedGroup.DepartmentGroupId}";
									}
									break;
								case AuditLogTypes.GroupRemoved:
									if (!String.IsNullOrWhiteSpace(auditEvent.Before))
									{
										var groupRemovedGroup = JsonConvert.DeserializeObject<DepartmentGroup>(auditEvent.Before);
										auditLog.Message = string.Format("{0} removed group {1}", profile.FullName.AsFirstNameLastName, groupRemovedGroup.Name);
										auditLog.Data = "No Data";
									}
									break;
								case AuditLogTypes.GroupChanged:
									if (!String.IsNullOrWhiteSpace(auditEvent.Before) && !String.IsNullOrWhiteSpace(auditEvent.After))
									{
										var groupUpdatedBeforeGroup = JsonConvert.DeserializeObject<DepartmentGroup>(auditEvent.Before);
										var groupUpdatedAfterGroup = JsonConvert.DeserializeObject<DepartmentGroup>(auditEvent.After);

										auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated group {groupUpdatedAfterGroup.Name}";
										var compareLogicGroup = new CompareLogic();

										ComparisonResult resultGroup = compareLogicGroup.Compare(groupUpdatedBeforeGroup, groupUpdatedAfterGroup);
										auditLog.Data = resultGroup.DifferencesString;
									}
									break;
								case AuditLogTypes.UnitAdded:
									if (!String.IsNullOrWhiteSpace(auditEvent.After))
									{
										var unitedAddedUnit = JsonConvert.DeserializeObject<Unit>(auditEvent.After);
										auditLog.Message = string.Format("{0} added unit {1}", profile.FullName.AsFirstNameLastName, unitedAddedUnit.Name);
										auditLog.Data = $"UnitId: {unitedAddedUnit.UnitId}";
									}
									break;
								case AuditLogTypes.UnitRemoved:
									if (!String.IsNullOrWhiteSpace(auditEvent.Before))
									{
										var unitedRemovedUnit = JsonConvert.DeserializeObject<Unit>(auditEvent.Before);
										auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed unit {unitedRemovedUnit.Name}";
										auditLog.Data = "No Data";
									}
									break;
								case AuditLogTypes.UnitChanged:
									if (!String.IsNullOrWhiteSpace(auditEvent.Before) && !String.IsNullOrWhiteSpace(auditEvent.After))
									{
										var unitUpdatedBeforeUnit = JsonConvert.DeserializeObject<Unit>(auditEvent.Before);
										var unitUpdatedAfterUnit = JsonConvert.DeserializeObject<Unit>(auditEvent.After);

										auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated unit {unitUpdatedAfterUnit.Name}";

										var compareLogicUnit = new CompareLogic();
										ComparisonResult resultUnit = compareLogicUnit.Compare(unitUpdatedBeforeUnit, unitUpdatedAfterUnit);
										auditLog.Data = resultUnit.DifferencesString;
									}
									break;
								case AuditLogTypes.ProfileUpdated:
									if (!String.IsNullOrWhiteSpace(auditEvent.Before) && !String.IsNullOrWhiteSpace(auditEvent.After))
									{
										var profileUpdatedBeforeProfile = JsonConvert.DeserializeObject<UserProfile>(auditEvent.Before);
										var profileUpdatedAfterProfile = JsonConvert.DeserializeObject<UserProfile>(auditEvent.After);

										auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated the profile for {profileUpdatedBeforeProfile.FullName.AsFirstNameLastName}";

										var compareLogicProfile = new CompareLogic();
										ComparisonResult resultProfile = compareLogicProfile.Compare(profileUpdatedBeforeProfile, profileUpdatedAfterProfile);
										auditLog.Data = resultProfile.DifferencesString;
									}
									break;
								case AuditLogTypes.PermissionsChanged:
									if (!String.IsNullOrWhiteSpace(auditEvent.Before) && !String.IsNullOrWhiteSpace(auditEvent.After))
									{
										var updatePermissionBefore = JsonConvert.DeserializeObject<Permission>(auditEvent.Before);
										var updatePermissionAfter = JsonConvert.DeserializeObject<Permission>(auditEvent.After);

										auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated the department permissions";

										var compareLogicProfile = new CompareLogic();
										ComparisonResult resultProfile = compareLogicProfile.Compare(updatePermissionBefore, updatePermissionAfter);
										auditLog.Data = resultProfile.DifferencesString;
									}
									break;
								case AuditLogTypes.SubscriptionUpdated:
									auditLog.Message = $"{profile.FullName.AsFirstNameLastName} changed (upgrade or downgrade) the active subscription of department id {auditEvent.DepartmentId}";
									auditLog.Data = "No Data";
									break;
								case AuditLogTypes.SubscriptionBillingInfoUpdated:
									auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated the subscription billing information for department id {auditEvent.DepartmentId}";
									auditLog.Data = "No Data";
									break;
								case AuditLogTypes.SubscriptionCancelled:
									auditLog.Message = $"{profile.FullName.AsFirstNameLastName} canceled the active subscription of department id {auditEvent.DepartmentId}";
									auditLog.Data = "No Data";
									break;
								case AuditLogTypes.SubscriptionCreated:
									auditLog.Message = $"{profile.FullName.AsFirstNameLastName} created a new active subscription for department id {auditEvent.DepartmentId}";
									auditLog.Data = "No Data";
									break;
							}

							if (String.IsNullOrWhiteSpace(auditLog.Data))
								auditLog.Data = "No Data";

							if (!String.IsNullOrWhiteSpace(auditLog.Message))
							{
								auditLog.LoggedOn = DateTime.UtcNow;
								await auditLogsRepository.SaveOrUpdateAsync(auditLog, cancellationToken);
							}
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

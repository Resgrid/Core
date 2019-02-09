using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.ServiceBus.Messaging;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Stripe;
using Resgrid.Model.Repositories;
using Resgrid.Model.Events;
using KellermanSoftware.CompareNetObjects;
using Microsoft.AspNet.Identity.EntityFramework6;
using System.Linq;
using System.Collections.Generic;
using Resgrid.Model.Providers;

namespace Resgrid.Workers.Framework.Logic
{
	public class SystemQueueLogic
	{
		private QueueClient _client = null;

		public SystemQueueLogic()
		{
			while (_client == null)
			{
				try
				{
					_client = QueueClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureQueueSystemConnectionString, Config.ServiceBusConfig.SystemQueueName);
				}
				catch (TimeoutException) { }
			}
		}

		public void Process(SystemQueueItem item)
		{
			ProcessQueueMessage(_client.Receive());
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
						CqrsEvent qi = null;
						try
						{
							qi = ObjectSerialization.Deserialize<CqrsEvent>(body);
						}
						catch (Exception ex)
						{
							success = false;
							result = "Unable to parse message body Exception: " + ex.ToString();
							message.Complete();
						}

						success = ProcessSystemQueueItem(qi);
					}

					try
					{
						if (success)
							message.Complete();
					}
					catch (MessageLockLostException)
					{
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					message.Abandon();
					success = false;
					result = ex.ToString();
				}
			}

			return new Tuple<bool, string>(success, result);
		}

		public static bool ProcessSystemQueueItem(CqrsEvent qi)
		{
			bool success = true;

			if (qi != null)
			{
				switch ((CqrsEventTypes)qi.Type)
				{
					case CqrsEventTypes.None:
						break;
					case CqrsEventTypes.UnitLocation:
						UnitLocation unitLocation = null;
						try
						{
							unitLocation = ObjectSerialization.Deserialize<UnitLocation>(qi.Data);
						}
						catch (Exception ex)
						{
							
						}

						if (unitLocation != null)
						{
							IUnitsService unitService;
							try
							{
								unitService = Bootstrapper.GetKernel().Resolve<IUnitsService>();
								unitService.AddUnitLocation(unitLocation);
							}
							catch (Exception ex)
							{
								
							}
							finally
							{
								unitService = null;
							}
						}
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
							var resgriterResult = pushService.Register(data).Result;

							pushService = null;
						}
						break;
					case CqrsEventTypes.UnitPushRegistration:
						PushRegisterionEvent unitData = null;
						try
						{
							unitData = ObjectSerialization.Deserialize<PushRegisterionEvent>(qi.Data);
						}
						catch (Exception ex)
						{
							
						}

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
							var unitResult = pushService.RegisterUnit(pushUri).Result;

							pushService = null;
						}
						break;
					case CqrsEventTypes.StripeChargeSucceeded:
						var succeededCharge = Stripe.Mapper<StripeCharge>.MapFromJson(qi.Data);

						if (succeededCharge != null)
						{
							var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

							paymentProviderService.ProcessStripePayment(succeededCharge);
						}
						break;
					case CqrsEventTypes.StripeChargeFailed:
						var failedCharge = Stripe.Mapper<StripeCharge>.MapFromJson(qi.Data);

						if (failedCharge != null)
						{
							var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

							paymentProviderService.ProcessStripeChargeFailed(failedCharge);
						}
						break;
					case CqrsEventTypes.StripeChargeRefunded:
						var refundedCharge = Stripe.Mapper<StripeCharge>.MapFromJson(qi.Data);

						if (refundedCharge != null)
						{
							var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

							paymentProviderService.ProcessStripeSubscriptionRefund(refundedCharge);
						}
						break;
					case CqrsEventTypes.StripeSubUpdated:
						var updatedSubscription = Stripe.Mapper<StripeSubscription>.MapFromJson(qi.Data);

						if (updatedSubscription != null)
						{
							var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

							paymentProviderService.ProcessStripeSubscriptionUpdate(updatedSubscription);
						}
						break;
					case CqrsEventTypes.StripeSubDeleted:
						var deletedSubscription = Stripe.Mapper<StripeSubscription>.MapFromJson(qi.Data);

						if (deletedSubscription != null)
						{
							var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

							paymentProviderService.ProcessStripeSubscriptionCancellation(deletedSubscription);
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
						else
						{
							
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
										profiles.Add(userProfileService.GetProfileByUserId(newChatEvent.RecipientUserIds.First()));
									}
									else
									{
										profiles.AddRange(userProfileService.GetSelectedUserProfiles(newChatEvent.RecipientUserIds));
									}

									var sendingUserProfile = userProfileService.GetProfileByUserId(newChatEvent.SendingUserId);

									var chatResult = communicationService.SendChat(newChatEvent.Id, newChatEvent.SendingUserId, newChatEvent.GroupName, newChatEvent.Message, sendingUserProfile, profiles).Result;
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

							var admins = departmentService.GetAllAdminsForDepartment(troubleAlertEvent.DepartmentId.Value);
							var unit = unitsService.GetUnitById(troubleAlertEvent.UnitId);
							List<UserProfile> profiles = new List<UserProfile>();
							Call call = null;
							string departmentNumber = "";
							string callAddress = "No Call Address";
							string unitAproxAddress = "Unknown Unit Address";

							departmentNumber = departmentSettingsService.GetTextToCallNumberForDepartment(troubleAlertEvent.DepartmentId.Value);

							if (admins != null)
								profiles.AddRange(userProfileService.GetSelectedUserProfiles(admins.Select(x => x.Id).ToList()));

							if (unit != null)
							{
								if (unit.StationGroupId.HasValue)
								{
									var groupAdmins = departmentGroupService.GetAllAdminsForGroup(unit.StationGroupId.Value);

									if (groupAdmins != null)
										profiles.AddRange(userProfileService.GetSelectedUserProfiles(groupAdmins.Select(x => x.UserId).ToList()));
								}

								if (troubleAlertEvent.CallId.HasValue && troubleAlertEvent.CallId.GetValueOrDefault() > 0)
								{
									call = callsService.GetCallById(troubleAlertEvent.CallId.Value);

									if (!String.IsNullOrEmpty(call.Address))
										callAddress = call.Address;
									else if (!String.IsNullOrEmpty(call.GeoLocationData))
									{
										string[] points = call.GeoLocationData.Split(char.Parse(","));

										if (points != null && points.Length == 2)
										{
											callAddress = geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1]));
										}
									}
								}

								if (!String.IsNullOrWhiteSpace(troubleAlertEvent.Latitude) && !String.IsNullOrWhiteSpace(troubleAlertEvent.Longitude))
								{
									unitAproxAddress = geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(troubleAlertEvent.Latitude), double.Parse(troubleAlertEvent.Longitude));
								}

								communicationService.SendTroubleAlert(troubleAlertEvent, unit, call, departmentNumber, troubleAlertEvent.DepartmentId.Value, callAddress, unitAproxAddress, profiles);
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
							var auditLogsRepository = Bootstrapper.GetKernel().Resolve<IGenericDataRepository<AuditLog>>();
							var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();

							var profile = userProfileService.GetProfileByUserId(auditEvent.UserId);

							var auditLog = new AuditLog();
							auditLog.DepartmentId = auditEvent.DepartmentId;
							auditLog.UserId = auditEvent.UserId;
							auditLog.LogType = (int)auditEvent.Type;

							switch (auditEvent.Type)
							{
								case AuditLogTypes.DepartmentSettingsChanged:
									auditLog.Message = string.Format("{0} updated the department settings", profile.FullName.AsFirstNameLastName);
									var compareLogic = new CompareLogic();
									ComparisonResult auditCompareResult = compareLogic.Compare(auditEvent.Before, auditEvent.After);
									auditLog.Data = auditCompareResult.DifferencesString;
									break;
								case AuditLogTypes.UserAdded:
									if (auditEvent.After != null && auditEvent.After.GetType().BaseType == typeof(IdentityUser))
									{
										var newProfile = userProfileService.GetProfileByUserId(((IdentityUser)auditEvent.After).UserId);
										auditLog.Message = string.Format("{0} added new user {1}", profile.FullName.AsFirstNameLastName, newProfile.FullName.AsFirstNameLastName);

										auditLog.Data = $"New UserId: {newProfile.UserId}";
									}
									break;
								case AuditLogTypes.UserRemoved:

									if (auditEvent.Before != null && auditEvent.Before.GetType() == typeof(UserProfile))
									{
										auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed user {(((UserProfile)auditEvent.Before).FullName.AsFirstNameLastName)}";
										auditLog.Data = "No Data";
									}

									break;
								case AuditLogTypes.GroupAdded:
									if (auditEvent.After != null && auditEvent.After.GetType() == typeof(DepartmentGroup))
									{
										if (((DepartmentGroup)auditEvent.After).Type.HasValue && ((DepartmentGroup)auditEvent.After).Type.Value == (int)DepartmentGroupTypes.Station)
											auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added station group {((DepartmentGroup)auditEvent.After).Name}";
										else
											auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added organizational group {((DepartmentGroup)auditEvent.After).Name}";

										auditLog.Data = $"GroupId: {((DepartmentGroup)auditEvent.After).DepartmentGroupId}";
									}
									break;
								case AuditLogTypes.GroupRemoved:
									if (auditEvent.Before != null && auditEvent.Before.GetType() == typeof(DepartmentGroup))
									{
										auditLog.Message = string.Format("{0} removed group {1}", profile.FullName.AsFirstNameLastName, ((DepartmentGroup)auditEvent.Before).Name);
										auditLog.Data = "No Data";
									}
									break;
								case AuditLogTypes.GroupChanged:
									if (auditEvent.Before != null && auditEvent.Before.GetType() == typeof(DepartmentGroup) && auditEvent.After != null &&
										auditEvent.After.GetType() == typeof(DepartmentGroup))
									{
										auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated group {((DepartmentGroup)auditEvent.After).Name}";
										var compareLogicGroup = new CompareLogic();

										ComparisonResult resultGroup = compareLogicGroup.Compare(auditEvent.Before, auditEvent.After);
										auditLog.Data = resultGroup.DifferencesString;
									}
									break;
								case AuditLogTypes.UnitAdded:
									if (auditEvent.After != null && auditEvent.After.GetType() == typeof(Unit))
									{
										auditLog.Message = string.Format("{0} added unit {1}", profile.FullName.AsFirstNameLastName, ((Unit)auditEvent.After).Name);
										auditLog.Data = $"UnitId: {((Unit)auditEvent.After).UnitId}";
									}
									break;
								case AuditLogTypes.UnitRemoved:
									if (auditEvent.Before != null && auditEvent.Before.GetType() == typeof(Unit))
									{
										auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed unit {((Unit)auditEvent.Before).Name}";
										auditLog.Data = "No Data";
									}
									break;
								case AuditLogTypes.UnitChanged:
									if (auditEvent.Before != null && auditEvent.Before.GetType() == typeof(Unit) && auditEvent.After != null && auditEvent.After.GetType() == typeof(Unit))
									{
										auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated unit {((Unit)auditEvent.After).Name}";

										var compareLogicUnit = new CompareLogic();
										ComparisonResult resultUnit = compareLogicUnit.Compare(auditEvent.Before, auditEvent.After);
										auditLog.Data = resultUnit.DifferencesString;
									}
									break;
								case AuditLogTypes.ProfileUpdated:
									if (auditEvent.Before != null && auditEvent.Before.GetType() == typeof(UserProfile) && auditEvent.After != null && auditEvent.After.GetType() == typeof(UserProfile))
									{
										auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated the profile for {((UserProfile)auditEvent.After).FullName.AsFirstNameLastName}";

										var compareLogicProfile = new CompareLogic();
										ComparisonResult resultProfile = compareLogicProfile.Compare(auditEvent.Before, auditEvent.After);
										auditLog.Data = resultProfile.DifferencesString;
									}
									break;
								case AuditLogTypes.PermissionsChanged:
									if (auditEvent.Before != null && auditEvent.Before.GetType() == typeof(Permission) && auditEvent.After != null && auditEvent.After.GetType() == typeof(Permission))
									{
										auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated the department permissions";

										var compareLogicProfile = new CompareLogic();
										ComparisonResult resultProfile = compareLogicProfile.Compare(auditEvent.Before, auditEvent.After);
										auditLog.Data = resultProfile.DifferencesString;
									}
									break;
							}

							if (String.IsNullOrWhiteSpace(auditLog.Data))
								auditLog.Data = "No Data";

							if (!String.IsNullOrWhiteSpace(auditLog.Message))
							{
								auditLog.LoggedOn = DateTime.UtcNow;
								auditLogsRepository.SaveOrUpdate(auditLog);
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

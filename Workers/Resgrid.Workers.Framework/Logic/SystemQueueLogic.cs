using System;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
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
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.InteropExtensions;
using Resgrid.Model.Providers;
using Newtonsoft.Json;
using Stripe.Checkout;
using Message = Microsoft.Azure.ServiceBus.Message;

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
					//_client = QueueClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureQueueSystemConnectionString, Config.ServiceBusConfig.SystemQueueName);
					_client = new QueueClient(Config.ServiceBusConfig.AzureQueueSystemConnectionString, Config.ServiceBusConfig.SystemQueueName);
				}
				catch (TimeoutException) { }
			}
		}

		public void Process(SystemQueueItem item)
		{
			//ProcessQueueMessage(_client.Receive());

			var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
			{
				MaxConcurrentCalls = 1,
				AutoComplete = false
			};

			// Register the function that will process messages
			_client.RegisterMessageHandler(ProcessQueueMessage, messageHandlerOptions);
		}

		public async Task<Tuple<bool, string>> ProcessQueueMessage(Message message, CancellationToken token)
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
							//message.Complete();
							await _client.CompleteAsync(message.SystemProperties.LockToken);
						}

						success = await ProcessSystemQueueItem(qi);
					}

					try
					{
						if (success)
							await _client.CompleteAsync(message.SystemProperties.LockToken);
							//message.Complete();
					}
					catch (MessageLockLostException)
					{
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					await _client.AbandonAsync(message.SystemProperties.LockToken); 
					//message.Abandon();
					success = false;
					result = ex.ToString();
				}
			}

			return new Tuple<bool, string>(success, result);
		}

		public static async Task<bool> ProcessSystemQueueItem(CqrsEvent qi, CancellationToken cancellationToken = default(CancellationToken))
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
								await unitService.AddUnitLocationAsync(unitLocation, cancellationToken);
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
							var resgriterResult = await pushService.Register(data);

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
							var unitResult = await pushService.RegisterUnit(pushUri);

							pushService = null;
						}
						break;
					case CqrsEventTypes.StripeChargeSucceeded:
						var succeededCharge = JsonConvert.DeserializeObject<Charge>(qi.Data);

						if (succeededCharge != null)
						{
							var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

							await paymentProviderService.ProcessStripePaymentAsync(succeededCharge);
						}
						break;
					case CqrsEventTypes.StripeChargeFailed:
						var failedCharge = JsonConvert.DeserializeObject<Charge>(qi.Data);

						if (failedCharge != null)
						{
							var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

							await paymentProviderService.ProcessStripeChargeFailedAsync(failedCharge);
						}
						break;
					case CqrsEventTypes.StripeChargeRefunded:
						var refundedCharge = JsonConvert.DeserializeObject<Charge>(qi.Data);

						if (refundedCharge != null)
						{
							var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

							await paymentProviderService.ProcessStripeSubscriptionRefundAsync(refundedCharge);
						}
						break;
					case CqrsEventTypes.StripeSubUpdated:
						var updatedSubscription = JsonConvert.DeserializeObject<Subscription>(qi.Data);

						if (updatedSubscription != null)
						{
							var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

							await paymentProviderService.ProcessStripeSubscriptionUpdateAsync(updatedSubscription);
						}
						break;
					case CqrsEventTypes.StripeSubDeleted:
						var deletedSubscription = JsonConvert.DeserializeObject<Subscription>(qi.Data);

						if (deletedSubscription != null)
						{
							var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

							await paymentProviderService.ProcessStripeSubscriptionCancellationAsync(deletedSubscription);
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
										profiles.Add(await userProfileService.GetProfileByUserIdAsync(newChatEvent.RecipientUserIds.First()));
									}
									else
									{
										profiles.AddRange(await userProfileService.GetSelectedUserProfilesAsync(newChatEvent.RecipientUserIds));
									}

									var sendingUserProfile = await userProfileService.GetProfileByUserIdAsync(newChatEvent.SendingUserId);

									var chatResult = await communicationService.SendChat(newChatEvent.Id, newChatEvent.SendingUserId, newChatEvent.GroupName, newChatEvent.Message, sendingUserProfile, profiles);
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
									else if (!String.IsNullOrEmpty(call.GeoLocationData))
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
									ComparisonResult auditCompareResult = compareLogic.Compare(auditEvent.Before, auditEvent.After);
									auditLog.Data = auditCompareResult.DifferencesString;
									break;
								case AuditLogTypes.UserAdded:
									if (auditEvent.After != null && auditEvent.After.GetType().BaseType == typeof(IdentityUser))
									{
										var newProfile = await userProfileService.GetProfileByUserIdAsync(((IdentityUser)auditEvent.After).UserId);
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
					case CqrsEventTypes.StripeCheckoutCompleted:
						var stripeCheckoutSession = JsonConvert.DeserializeObject<Session>(qi.Data);

						if (stripeCheckoutSession != null)
						{
							var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

							await paymentProviderService.ProcessStripeCheckoutCompletedAsync(stripeCheckoutSession, cancellationToken);
						}
						break;
					case CqrsEventTypes.StripeCheckoutUpdated:
						var stripeCheckoutSessionUpdated = JsonConvert.DeserializeObject<Session>(qi.Data);

						if (stripeCheckoutSessionUpdated != null)
						{
							var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

							await paymentProviderService.ProcessStripeCheckoutUpdateAsync(stripeCheckoutSessionUpdated, cancellationToken);
						}
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}

			return success;
		}

		static Task ExceptionReceivedHandler(ExceptionReceivedEventArgs exceptionReceivedEventArgs)
		{
			//Console.WriteLine($"Message handler encountered an exception {exceptionReceivedEventArgs.Exception}.");
			//var context = exceptionReceivedEventArgs.ExceptionReceivedContext;
			//Console.WriteLine("Exception context for troubleshooting:");
			//Console.WriteLine($"- Endpoint: {context.Endpoint}");
			//Console.WriteLine($"- Entity Path: {context.EntityPath}");
			//Console.WriteLine($"- Executing Action: {context.Action}");
			return Task.CompletedTask;
		}
	}
}

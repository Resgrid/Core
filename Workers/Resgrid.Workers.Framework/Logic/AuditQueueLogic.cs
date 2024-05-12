﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Newtonsoft.Json;
using Resgrid.Model.Events;
using Resgrid.Model.Repositories;
using KellermanSoftware.CompareNetObjects;
using Resgrid.Model.Identity;

namespace Resgrid.Workers.Framework.Logic
{
	public class AuditQueueLogic
	{
		public static async Task<bool> ProcessAuditQueueItem(AuditEvent auditEvent, CancellationToken cancellationToken = default(CancellationToken))
		{
			bool success = true;

			if (auditEvent != null)
			{
				try
				{
					var auditLogsRepository = Bootstrapper.GetKernel().Resolve<IAuditLogsRepository>();
					var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();

					var profile = await userProfileService.GetProfileByUserIdAsync(auditEvent.UserId);

					var auditLog = new AuditLog();
					auditLog.DepartmentId = auditEvent.DepartmentId;
					auditLog.UserId = auditEvent.UserId;
					auditLog.LogType = (int)auditEvent.Type;
					auditLog.IpAddress = auditEvent.IpAddress;
					auditLog.UserAgent = auditEvent.UserAgent;
					auditLog.ServerName = auditEvent.ServerName;
					auditLog.Successful = auditEvent.Successful;

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
							auditLog.Message =
								$"{profile.FullName.AsFirstNameLastName} changed (upgrade or downgrade) the active subscription of department id {auditEvent.DepartmentId}";
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
						case AuditLogTypes.UserAccountDeleted:
							auditLog.Message = $"{profile.FullName.AsFirstNameLastName} has deleted their own account";

							auditLog.Data = "No Data";
							break;
						case AuditLogTypes.DeleteDepartmentRequested:
							auditLog.Message = $"{profile.FullName.AsFirstNameLastName} has requested that the Resgrid department be deleted";

							auditLog.Data = "No Data";
							break;
						case AuditLogTypes.DeleteDepartmentRequestedCancelled:
							auditLog.Message = $"{profile.FullName.AsFirstNameLastName} cancelled the pending department deletion request";

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
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			return success;
		}
	}
}

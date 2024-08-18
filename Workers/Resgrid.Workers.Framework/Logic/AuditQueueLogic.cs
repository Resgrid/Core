using System;
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
							auditLog.Message = $"{profile.FullName.AsFirstNameLastName} canceled the pending department deletion request";

							auditLog.Data = "No Data";
							break;
						case AuditLogTypes.CallReactivated:
							auditLog.Message = $"{profile.FullName.AsFirstNameLastName} reactivated call";

							auditLog.Data = "No Data";
							break;
						case AuditLogTypes.AddonSubscriptionModified:
							auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated the addon subscription for department id {auditEvent.DepartmentId}";
							auditLog.Data = "No Data";
							break;
						case AuditLogTypes.DeleteStaticShift:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var deleteStaticShiftBefore = JsonConvert.DeserializeObject<Workshift>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} deleted the static shift {deleteStaticShiftBefore.Name}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} deleted a static shift.";
							}
							auditLog.Data = "No Data";
							break;
						case AuditLogTypes.UpdateStaticShift:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before) && !String.IsNullOrWhiteSpace(auditEvent.After))
							{
								var updateStaticShiftBefore = JsonConvert.DeserializeObject<Workshift>(auditEvent.Before);
								var updateStaticShiftAfter = JsonConvert.DeserializeObject<Workshift>(auditEvent.After);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated the static shift {updateStaticShiftBefore.Name}";

								var compareLogicProfile = new CompareLogic();
								ComparisonResult resultProfile = compareLogicProfile.Compare(updateStaticShiftBefore, updateStaticShiftAfter);
								auditLog.Data = resultProfile.DifferencesString;
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated the static shift";
							}
							break;
						case AuditLogTypes.CustomStatusAdded:
							if (!String.IsNullOrWhiteSpace(auditEvent.After))
							{
								var customStateAddedAfter = JsonConvert.DeserializeObject<CustomState>(auditEvent.After);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added a new Custom Status {customStateAddedAfter.Name}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added a new Custom Status.";
							}
							auditLog.Data = "No Data";
							break;
						case AuditLogTypes.CustomStatusRemoved:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var customStateRemovedBefore = JsonConvert.DeserializeObject<CustomState>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed a Custom Status {customStateRemovedBefore.Name}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed a Custom Status.";
							}
							auditLog.Data = "No Data";
							break;
						case AuditLogTypes.CustomStatusUpdated:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before) && !String.IsNullOrWhiteSpace(auditEvent.After))
							{
								var updateCustomStatusBefore = JsonConvert.DeserializeObject<CustomState>(auditEvent.Before);
								var updateCustomStatusAfter = JsonConvert.DeserializeObject<CustomState>(auditEvent.After);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated the Custom Status {updateCustomStatusBefore.Name}";

								var compareLogicProfile = new CompareLogic();
								ComparisonResult resultProfile = compareLogicProfile.Compare(updateCustomStatusBefore, updateCustomStatusAfter);
								auditLog.Data = resultProfile.DifferencesString;
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} a Custom Status";
							}
							break;
						case AuditLogTypes.CustomStatusDetailUpdated:
							auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated the a Custom Status Detail";
							break;
						case AuditLogTypes.CallTypeAdded:
							if (!String.IsNullOrWhiteSpace(auditEvent.After))
							{
								var callTypeAddedAfter = JsonConvert.DeserializeObject<CallType>(auditEvent.After);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added Call Type {callTypeAddedAfter.Type}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added a Call Type.";
							}
							break;
						case AuditLogTypes.CallTypeEdited:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var callTypeAddedBefore = JsonConvert.DeserializeObject<CallType>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited Call Type {callTypeAddedBefore.Type}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited a Call Type.";
							}
							break;
						case AuditLogTypes.CallTypeRemoved:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var callTypeAddedBefore = JsonConvert.DeserializeObject<CallType>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed Call Type {callTypeAddedBefore.Type}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed a Call Type.";
							}
							break;
						case AuditLogTypes.CallPriorityAdded:
							if (!String.IsNullOrWhiteSpace(auditEvent.After))
							{
								var callPriorityAddedAfter = JsonConvert.DeserializeObject<DepartmentCallPriority>(auditEvent.After);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added Call Priority {callPriorityAddedAfter.Name}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed a Call Priority.";
							}
							break;
						case AuditLogTypes.CallPriorityEdited:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var callPriorityAddedAfter = JsonConvert.DeserializeObject<DepartmentCallPriority>(auditEvent.After);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added Call Priority {callPriorityAddedAfter.Name}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed a Call Priority.";
							}
							break;
						case AuditLogTypes.CallPriorityRemoved:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var callPriorityDeletedBefore = JsonConvert.DeserializeObject<DepartmentCallPriority>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added Call Priority {callPriorityDeletedBefore.Name}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed a Call Priority.";
							}
							break;
						case AuditLogTypes.UnitTypeAdded:
							if (!String.IsNullOrWhiteSpace(auditEvent.After))
							{
								var unitTypeAddedAfter = JsonConvert.DeserializeObject<UnitType>(auditEvent.After);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added Unit Type {unitTypeAddedAfter.Type}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed a Unit Type.";
							}
							break;
						case AuditLogTypes.UnitTypeEdited:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var unitTypeEditedBerfore = JsonConvert.DeserializeObject<UnitType>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited Unit Type {unitTypeEditedBerfore.Type}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited a Unit Type.";
							}
							break;
						case AuditLogTypes.UnitTypeRemoved:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var unitTypeRemovedBerfore = JsonConvert.DeserializeObject<UnitType>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed Unit Type {unitTypeRemovedBerfore.Type}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed a Unit Type.";
							}
							break;
						case AuditLogTypes.CertificationTypeAdded:
							if (!String.IsNullOrWhiteSpace(auditEvent.After))
							{
								var certificationAddedAfter = JsonConvert.DeserializeObject<DepartmentCertificationType>(auditEvent.After);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added Certification Type {certificationAddedAfter.Type}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added a Certification Type.";
							}
							break;
						case AuditLogTypes.CertificationTypeEdited:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var certificationEditedBefore = JsonConvert.DeserializeObject<DepartmentCertificationType>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited Certification Type {certificationEditedBefore.Type}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited a Certification Type.";
							}
							break;
						case AuditLogTypes.CertificationTypeRemoved:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var certificationRemovedBefore = JsonConvert.DeserializeObject<DepartmentCertificationType>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed Certification Type {certificationRemovedBefore.Type}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited a Certification Type.";
							}
							break;
						case AuditLogTypes.DocumentCategoryAdded:
							if (!String.IsNullOrWhiteSpace(auditEvent.After))
							{
								var documentCategoryAddedAfter = JsonConvert.DeserializeObject<DocumentCategory>(auditEvent.After);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added Document Category {documentCategoryAddedAfter.Name}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited a Document Category.";
							}
							break;
						case AuditLogTypes.DocumentCategoryEdited:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var documentCategoryEditedBefore = JsonConvert.DeserializeObject<DocumentCategory>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited Document Category {documentCategoryEditedBefore.Name}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited a Document Category.";
							}
							break;
						case AuditLogTypes.DocumentCategoryRemoved:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var documentCategoryRemovedBefore = JsonConvert.DeserializeObject<DocumentCategory>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed Document Category {documentCategoryRemovedBefore.Name}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed a Document Category.";
							}
							break;
						case AuditLogTypes.DocumentAdded:
							if (!String.IsNullOrWhiteSpace(auditEvent.After))
							{
								var documentAddedAfter = JsonConvert.DeserializeObject<Document>(auditEvent.After);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added Document {documentAddedAfter.Name}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added a Document.";
							}
							break;
						case AuditLogTypes.DocumentEdited:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited Document {auditEvent.Before}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited a Document.";
							}
							break;
						case AuditLogTypes.DocumentRemoved:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed Document {auditEvent.Before}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed a Document.";
							}
							break;
						case AuditLogTypes.NoteCategoryAdded:
							if (!String.IsNullOrWhiteSpace(auditEvent.After))
							{
								var noteCategoryAddedAfter = JsonConvert.DeserializeObject<NoteCategory>(auditEvent.After);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added Note Category {noteCategoryAddedAfter.Name}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added a Note Category.";
							}
							break;
						case AuditLogTypes.NoteCategoryEdited:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var noteCategoryEditedBefore = JsonConvert.DeserializeObject<NoteCategory>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited Note Category {noteCategoryEditedBefore.Name}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited a Note Category.";
							}
							break;
						case AuditLogTypes.NoteCategoryRemoved:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var noteCategoryRemovedBefore = JsonConvert.DeserializeObject<NoteCategory>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed Note Category {noteCategoryRemovedBefore.Name}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed a Note Category.";
							}
							break;
						case AuditLogTypes.NoteAdded:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var noteAddedBefore = JsonConvert.DeserializeObject<Note>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added Note {noteAddedBefore.Title}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added a Note.";
							}
							break;
						case AuditLogTypes.NoteEdited:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var noteEditedBefore = JsonConvert.DeserializeObject<Note>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited Note {noteEditedBefore.Title}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited a Note.";
							}
							break;
						case AuditLogTypes.NoteRemoved:
							if (!String.IsNullOrWhiteSpace(auditEvent.Before))
							{
								var noteRemovedBefore = JsonConvert.DeserializeObject<Note>(auditEvent.Before);

								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed Note {noteRemovedBefore.Title}";
							}
							else
							{
								auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed a Note.";
							}
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

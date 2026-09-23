using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
					var auditService = Bootstrapper.GetKernel().Resolve<IAuditService>();

					var auditLog = await BuildAuditLogAsync(auditEvent, userProfileService, auditService);
					await auditLogsRepository.SaveOrUpdateAsync(auditLog, cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			return success;
		}

		/// <summary>
		/// Turns an audit event into the row the worker saves: who did what (Message) and the change detail
		/// (Data) for its type. Kept apart from the save so the wording can be tested without the container.
		/// </summary>
		public static async Task<AuditLog> BuildAuditLogAsync(AuditEvent auditEvent, IUserProfileService userProfileService, IAuditService auditService)
		{
			var profile = await userProfileService.GetProfileByUserIdAsync(auditEvent.UserId);

			var auditLog = new AuditLog();
			auditLog.DepartmentId = auditEvent.DepartmentId;
			auditLog.UserId = auditEvent.UserId;
			auditLog.LogType = (int)auditEvent.Type;
			auditLog.IpAddress = auditEvent.IpAddress;
			auditLog.UserAgent = auditEvent.UserAgent;
			auditLog.ServerName = auditEvent.ServerName;
			auditLog.Successful = auditEvent.Successful;
			// The subject of the action, so a privileged event can be found by who it was done to
			// and not only by who did it. Null for events that act on the department as a whole.
			auditLog.ObjectId = auditEvent.TargetUserId;

			switch (auditEvent.Type)
			{
				case AuditLogTypes.PasswordResetByAdministrator:
					var passwordResetTarget = String.IsNullOrWhiteSpace(auditEvent.TargetUserId)
						? null
						: await userProfileService.GetProfileByUserIdAsync(auditEvent.TargetUserId);
					auditLog.Message = passwordResetTarget == null
						? $"{profile.FullName.AsFirstNameLastName} performed a privileged password reset action"
						: $"{profile.FullName.AsFirstNameLastName} performed a privileged password reset action on {passwordResetTarget.FullName.AsFirstNameLastName}";
					auditLog.Data = String.IsNullOrWhiteSpace(auditEvent.After) ? "No Data" : auditEvent.After;
					break;
				case AuditLogTypes.DepartmentSettingsChanged:
					auditLog.Message = string.Format("{0} updated the department settings", profile.FullName.AsFirstNameLastName);
					// Several screens share this type and none but the Settings page sends a Department: the
					// Profile page sends a profile snapshot, Records a settings model or a cutover with no Before.
					auditLog.Data = GetSettingsChangedAuditData(auditEvent.Before, auditEvent.After);
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
					auditLog.Data = GetDepartmentDeletionQueueItemAuditData(auditEvent.After, false);
					break;
				case AuditLogTypes.DeleteDepartmentRequestedCancelled:
					auditLog.Message = $"{profile.FullName.AsFirstNameLastName} canceled the pending department deletion request";
					auditLog.Data = GetDepartmentDeletionQueueItemAuditData(auditEvent.Before, true);
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
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated a Custom Status.";
					}
					break;
				case AuditLogTypes.CustomStatusDetailUpdated:
					auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated a Custom Status Detail.";
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
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added a Call Priority.";
					}
					break;
				case AuditLogTypes.CallPriorityEdited:
					if (!String.IsNullOrWhiteSpace(auditEvent.Before))
					{
						var callPriorityEditedBefore = JsonConvert.DeserializeObject<DepartmentCallPriority>(auditEvent.Before);

						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited Call Priority {callPriorityEditedBefore.Name}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} edited a Call Priority.";
					}
					break;
				case AuditLogTypes.CallPriorityRemoved:
					if (!String.IsNullOrWhiteSpace(auditEvent.Before))
					{
						var callPriorityDeletedBefore = JsonConvert.DeserializeObject<DepartmentCallPriority>(auditEvent.Before);

						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed Call Priority {callPriorityDeletedBefore.Name}";
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
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added a Unit Type.";
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
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed a Certification Type.";
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
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added a Document Category.";
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

				case AuditLogTypes.WorkflowAdded:
					if (!String.IsNullOrWhiteSpace(auditEvent.After))
					{
						var wfAdded = JsonConvert.DeserializeObject<dynamic>(auditEvent.After);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} created Workflow {wfAdded?.Name}";
						auditLog.Data = $"WorkflowId: {wfAdded?.WorkflowId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} created a Workflow.";
					}
					break;

				case AuditLogTypes.WorkflowEdited:
					if (!String.IsNullOrWhiteSpace(auditEvent.After))
					{
						var wfEdited = JsonConvert.DeserializeObject<dynamic>(auditEvent.After);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated Workflow {wfEdited?.Name}";
						auditLog.Data = $"WorkflowId: {wfEdited?.WorkflowId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated a Workflow.";
					}
					break;

				case AuditLogTypes.WorkflowDeleted:
					if (!String.IsNullOrWhiteSpace(auditEvent.Before))
					{
						var wfDeleted = JsonConvert.DeserializeObject<dynamic>(auditEvent.Before);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} deleted Workflow {wfDeleted?.Name}";
						auditLog.Data = $"WorkflowId: {wfDeleted?.WorkflowId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} deleted a Workflow.";
					}
					break;

				case AuditLogTypes.WorkflowStepAdded:
					if (!String.IsNullOrWhiteSpace(auditEvent.After))
					{
						var stepAdded = JsonConvert.DeserializeObject<dynamic>(auditEvent.After);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added a step to Workflow {stepAdded?.WorkflowId}";
						auditLog.Data = $"WorkflowStepId: {stepAdded?.WorkflowStepId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added a Workflow Step.";
					}
					break;

				case AuditLogTypes.WorkflowStepEdited:
					if (!String.IsNullOrWhiteSpace(auditEvent.After))
					{
						var stepEdited = JsonConvert.DeserializeObject<dynamic>(auditEvent.After);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated a step in Workflow {stepEdited?.WorkflowId}";
						auditLog.Data = $"WorkflowStepId: {stepEdited?.WorkflowStepId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated a Workflow Step.";
					}
					break;

				case AuditLogTypes.WorkflowStepDeleted:
					if (!String.IsNullOrWhiteSpace(auditEvent.Before))
					{
						var stepDeleted = JsonConvert.DeserializeObject<dynamic>(auditEvent.Before);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} deleted Workflow Step {stepDeleted?.WorkflowStepId}";
						auditLog.Data = $"WorkflowStepId: {stepDeleted?.WorkflowStepId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} deleted a Workflow Step.";
					}
					break;

				// NOTE: Credential audit cases intentionally do not log Before/After data to prevent sensitive info being stored.
				case AuditLogTypes.WorkflowCredentialAdded:
					if (!String.IsNullOrWhiteSpace(auditEvent.After))
					{
						var credAdded = JsonConvert.DeserializeObject<dynamic>(auditEvent.After);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added Workflow Credential {credAdded?.Name}";
						auditLog.Data = $"WorkflowCredentialId: {credAdded?.WorkflowCredentialId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added a Workflow Credential.";
					}
					break;

				case AuditLogTypes.WorkflowCredentialEdited:
					if (!String.IsNullOrWhiteSpace(auditEvent.After))
					{
						var credEdited = JsonConvert.DeserializeObject<dynamic>(auditEvent.After);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated Workflow Credential {credEdited?.Name}";
						auditLog.Data = $"WorkflowCredentialId: {credEdited?.WorkflowCredentialId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated a Workflow Credential.";
					}
					break;

				case AuditLogTypes.WorkflowCredentialDeleted:
					if (!String.IsNullOrWhiteSpace(auditEvent.Before))
					{
						var credDeleted = JsonConvert.DeserializeObject<dynamic>(auditEvent.Before);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} deleted Workflow Credential {credDeleted?.Name}";
						auditLog.Data = $"WorkflowCredentialId: {credDeleted?.WorkflowCredentialId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} deleted a Workflow Credential.";
					}
					break;

				// ── User Defined Fields ───────────────────────────────────────────────
				case AuditLogTypes.UdfDefinitionCreated:
					if (!String.IsNullOrWhiteSpace(auditEvent.After))
					{
						var udfDefCreated = JsonConvert.DeserializeObject<dynamic>(auditEvent.After);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} created UDF Definition v{udfDefCreated?.Version} for entity type {udfDefCreated?.EntityType}";
						auditLog.Data = $"UdfDefinitionId: {udfDefCreated?.UdfDefinitionId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} created a UDF Definition.";
					}
					break;

				case AuditLogTypes.UdfDefinitionUpdated:
					if (!String.IsNullOrWhiteSpace(auditEvent.After))
					{
						var udfDefUpdated = JsonConvert.DeserializeObject<dynamic>(auditEvent.After);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated UDF Definition — new version v{udfDefUpdated?.Version} for entity type {udfDefUpdated?.EntityType}";
						auditLog.Data = $"UdfDefinitionId: {udfDefUpdated?.UdfDefinitionId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated a UDF Definition.";
					}
					break;

				case AuditLogTypes.UdfDefinitionDeleted:
					if (!String.IsNullOrWhiteSpace(auditEvent.Before))
					{
						var udfDefDeleted = JsonConvert.DeserializeObject<dynamic>(auditEvent.Before);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} deleted UDF Definition v{udfDefDeleted?.Version} for entity type {udfDefDeleted?.EntityType}";
						auditLog.Data = $"UdfDefinitionId: {udfDefDeleted?.UdfDefinitionId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} deleted a UDF Definition.";
					}
					break;

				case AuditLogTypes.UdfFieldAdded:
					if (!String.IsNullOrWhiteSpace(auditEvent.After))
					{
						var udfFieldAdded = JsonConvert.DeserializeObject<dynamic>(auditEvent.After);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added UDF field '{udfFieldAdded?.Label}' to definition {udfFieldAdded?.UdfDefinitionId}";
						auditLog.Data = $"UdfFieldId: {udfFieldAdded?.UdfFieldId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} added a UDF field.";
					}
					break;

				case AuditLogTypes.UdfFieldUpdated:
					if (!String.IsNullOrWhiteSpace(auditEvent.After))
					{
						var udfFieldUpdated = JsonConvert.DeserializeObject<dynamic>(auditEvent.After);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated UDF field '{udfFieldUpdated?.Label}'";
						auditLog.Data = $"UdfFieldId: {udfFieldUpdated?.UdfFieldId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} updated a UDF field.";
					}
					break;

				case AuditLogTypes.UdfFieldRemoved:
					if (!String.IsNullOrWhiteSpace(auditEvent.Before))
					{
						var udfFieldRemoved = JsonConvert.DeserializeObject<dynamic>(auditEvent.Before);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed UDF field '{udfFieldRemoved?.Label}' from definition {udfFieldRemoved?.UdfDefinitionId}";
						auditLog.Data = $"UdfFieldId: {udfFieldRemoved?.UdfFieldId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} removed a UDF field.";
					}
					break;

				case AuditLogTypes.UdfFieldValueSaved:
					if (!String.IsNullOrWhiteSpace(auditEvent.After))
					{
						var udfValueSaved = JsonConvert.DeserializeObject<dynamic>(auditEvent.After);
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} saved UDF values for {udfValueSaved?.EntityType} entity {udfValueSaved?.EntityId}";
						auditLog.Data = $"UdfDefinitionId: {udfValueSaved?.UdfDefinitionId}, EntityId: {udfValueSaved?.EntityId}";
					}
					else
					{
						auditLog.Message = $"{profile.FullName.AsFirstNameLastName} saved UDF field values.";
					}
					break;

			} // end switch

			// Fallback so no audit event is ever silently dropped. Any AuditLogTypes value that
			// doesn't have an explicit case above (e.g. newly added types) would otherwise leave
			// Message empty and never be persisted. Build a generic, human-readable message
			// instead of discarding the event.
			if (String.IsNullOrWhiteSpace(auditLog.Message))
			{
				var actor = profile?.FullName?.AsFirstNameLastName;
				var action = auditService.GetAuditLogTypeString(auditEvent.Type);

				auditLog.Message = String.IsNullOrWhiteSpace(actor) ? action : $"{actor} - {action}";
			}

			if (String.IsNullOrWhiteSpace(auditLog.Data))
				auditLog.Data = "No Data";

			auditLog.LoggedOn = DateTime.UtcNow;

			return auditLog;
		}

		private static string GetDepartmentDeletionQueueItemAuditData(string queueItemJson, bool cancelled)
		{
			if (String.IsNullOrWhiteSpace(queueItemJson))
				return "No Data";

			try
			{
				var queueItem = JsonConvert.DeserializeObject<QueueItem>(queueItemJson);

				if (queueItem == null)
					return "No Data";

				if (cancelled)
					return $"QueueItemId: {queueItem.QueueItemId}; Scheduled deletion (UTC): {queueItem.ToBeCompletedOn:u}; OriginallyRequestedByUserId: {queueItem.QueuedByUserId}; QueuedOn (UTC): {queueItem.QueuedOn:u}";

				return $"QueueItemId: {queueItem.QueueItemId}; Scheduled deletion (UTC): {queueItem.ToBeCompletedOn:u}; RequestedByUserId: {queueItem.QueuedByUserId}";
			}
			catch (Exception ex)
			{
				// A null/malformed payload must not drop the whole audit row via the outer catch.
				Logging.LogException(ex, "AuditQueueLogic::Failed to deserialize department deletion queue item audit payload");
				return "No Data";
			}
		}

		/// <summary>
		/// Describes a settings change as one line per changed JSON value ("Path: before -> after"), whatever
		/// object the producer serialized. When there is no Before, each After value is listed as "Path: value".
		/// A side that is not JSON is recorded verbatim so the audit row is still written.
		/// </summary>
		public static string GetSettingsChangedAuditData(string before, string after)
		{
			var hasBefore = !String.IsNullOrWhiteSpace(before);
			var hasAfter = !String.IsNullOrWhiteSpace(after);

			if (!hasBefore && !hasAfter)
				return "No Data";

			var beforeToken = hasBefore ? TryParseJson(before) : null;
			var afterToken = hasAfter ? TryParseJson(after) : null;

			if ((hasBefore && beforeToken == null) || (hasAfter && afterToken == null))
				return $"Before: {(hasBefore ? before : "(none)")}; After: {(hasAfter ? after : "(none)")}";

			var beforeValues = FlattenJson(beforeToken);
			var afterValues = FlattenJson(afterToken);
			var paths = beforeValues.Keys.Concat(afterValues.Keys.Where(p => !beforeValues.ContainsKey(p)));

			var differences = new List<string>();
			foreach (var path in paths)
			{
				beforeValues.TryGetValue(path, out var beforeValue);
				afterValues.TryGetValue(path, out var afterValue);

				if (JToken.DeepEquals(beforeValue, afterValue))
					continue;

				var name = String.IsNullOrEmpty(path) ? "Value" : path;
				differences.Add(hasBefore
					? $"{name}: {FormatJsonValue(beforeValue)} -> {FormatJsonValue(afterValue)}"
					: $"{name}: {FormatJsonValue(afterValue)}");
			}

			return differences.Count == 0 ? "No Data" : String.Join(Environment.NewLine, differences);
		}

		private static JToken TryParseJson(string value)
		{
			var trimmed = value.Trim();
			if (!trimmed.StartsWith("{") && !trimmed.StartsWith("["))
				return null;

			try
			{
				// Dates stay strings so the recorded value is exactly what the producer serialized.
				using var reader = new JsonTextReader(new StringReader(trimmed)) { DateParseHandling = DateParseHandling.None };
				return JToken.ReadFrom(reader);
			}
			catch (JsonReaderException)
			{
				return null;
			}
		}

		private static Dictionary<string, JToken> FlattenJson(JToken token)
		{
			var values = new Dictionary<string, JToken>(StringComparer.Ordinal);

			if (token is JValue)
				values[token.Path] = token;
			else if (token is JContainer container)
				foreach (var value in container.Descendants().OfType<JValue>())
					values[value.Path] = value;

			return values;
		}

		private static string FormatJsonValue(JToken value)
		{
			if (value == null || value.Type == JTokenType.Null)
				return "(none)";

			return value.Type == JTokenType.String ? value.Value<string>() : value.ToString(Formatting.None);
		}
	}
}

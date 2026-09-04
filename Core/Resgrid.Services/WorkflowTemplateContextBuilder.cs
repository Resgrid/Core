using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Scriban.Runtime;

namespace Resgrid.Services
{
	/// <summary>
	/// Builds a Scriban ScriptObject context for workflow template rendering.
	/// Returns the ScriptObject as <c>object</c> to keep Scriban out of Resgrid.Model.
	/// </summary>
	public class WorkflowTemplateContextBuilder : IWorkflowTemplateContextBuilder
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentMemberSensitiveDataService _memberSensitiveDataService;
		private readonly IDepartmentProfileMediaService _departmentProfileMediaService;

		public WorkflowTemplateContextBuilder(
			IDepartmentsService departmentsService,
			IDepartmentSettingsService departmentSettingsService,
			IUserProfileService userProfileService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			IUnitsService unitsService,
			IDepartmentMemberSensitiveDataService memberSensitiveDataService,
			IDepartmentProfileMediaService departmentProfileMediaService)
		{
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_userProfileService = userProfileService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_unitsService = unitsService;
			_memberSensitiveDataService = memberSensitiveDataService;
			_departmentProfileMediaService = departmentProfileMediaService;
		}

		public async Task<object> BuildContextAsync(
			int departmentId,
			WorkflowTriggerEventType eventType,
			string eventPayloadJson,
			CancellationToken cancellationToken)
		{
			var scriptObject = new ScriptObject();

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			var phoneNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(departmentId);
			var branding = await _departmentProfileMediaService.GetEmailBrandingAsync(departmentId);

			AddCommonDepartmentVariables(scriptObject, department, phoneNumber, branding);
			AddCommonTimestampVariables(scriptObject, department?.TimeZone);

			string triggeringUserId = null;

			// Deserialize event payload and map event-specific variables
			switch (eventType)
			{
				case WorkflowTriggerEventType.CallAdded:
				case WorkflowTriggerEventType.CallUpdated:
				case WorkflowTriggerEventType.CallClosed:
				{
					var call = TryDeserialize<CallAddedEvent>(eventPayloadJson)?.Call
					           ?? TryDeserialize<CallUpdatedEvent>(eventPayloadJson)?.Call
					           ?? TryDeserialize<CallClosedEvent>(eventPayloadJson)?.Call;
					if (call != null)
					{
						await MapCallVariablesAsync(scriptObject, call, departmentId, cancellationToken);
						triggeringUserId = call.ReportingUserId;
					}
					break;
				}
				case WorkflowTriggerEventType.UnitStatusChanged:
				{
					var evt = TryDeserialize<UnitStatusEvent>(eventPayloadJson);
					if (evt != null) MapUnitStatusVariables(scriptObject, evt.Status, evt.PreviousStatus);
					break;
				}
				case WorkflowTriggerEventType.PersonnelStaffingChanged:
				{
					var evt = TryDeserialize<UserStaffingEvent>(eventPayloadJson);
					if (evt != null)
					{
						MapStaffingVariables(scriptObject, evt.Staffing, evt.PreviousStaffing);
						triggeringUserId = evt.Staffing?.UserId;
					}
					break;
				}
				case WorkflowTriggerEventType.PersonnelStatusChanged:
				{
					var evt = TryDeserialize<UserStatusEvent>(eventPayloadJson);
					if (evt != null)
					{
						MapPersonnelStatusVariables(scriptObject, evt.Status, evt.PreviousStatus);
						triggeringUserId = evt.Status?.UserId;
					}
					break;
				}
				case WorkflowTriggerEventType.UserCreated:
				{
					var evt = TryDeserialize<UserCreatedEvent>(eventPayloadJson);
					if (evt != null)
					{
						var newUser = new ScriptObject();
						newUser["id"] = evt.User?.Id;
						newUser["username"] = evt.User?.UserName;
						newUser["email"] = evt.User?.Email;
						newUser["name"] = evt.Name;
						scriptObject["new_user"] = newUser;
						triggeringUserId = evt.User?.Id;
					}
					break;
				}
				case WorkflowTriggerEventType.UserAssignedToGroup:
				{
					var evt = TryDeserialize<UserAssignedToGroupEvent>(eventPayloadJson);
					if (evt != null)
					{
						MapGroupVariables(scriptObject, evt.Group, "group");
						MapGroupVariables(scriptObject, evt.PreviousGroup, "previous_group");
						var assignedUser = new ScriptObject();
						assignedUser["id"] = evt.UserId;
						assignedUser["name"] = evt.Name;
						scriptObject["assigned_user"] = assignedUser;
						triggeringUserId = evt.UserId;
					}
					break;
				}
				case WorkflowTriggerEventType.DocumentAdded:
				{
					var evt = TryDeserialize<DocumentAddedEvent>(eventPayloadJson);
					if (evt?.Document != null)
					{
						MapDocumentVariables(scriptObject, evt.Document);
						triggeringUserId = evt.Document.UserId;
					}
					break;
				}
				case WorkflowTriggerEventType.NoteAdded:
				{
					var evt = TryDeserialize<NoteAddedEvent>(eventPayloadJson);
					if (evt?.Note != null)
					{
						MapNoteVariables(scriptObject, evt.Note);
						triggeringUserId = evt.Note.UserId;
					}
					break;
				}
				case WorkflowTriggerEventType.UnitAdded:
				{
					var evt = TryDeserialize<UnitAddedEvent>(eventPayloadJson);
					if (evt?.Unit != null) MapUnitVariables(scriptObject, evt.Unit);
					break;
				}
				case WorkflowTriggerEventType.LogAdded:
				{
					var evt = TryDeserialize<LogAddedEvent>(eventPayloadJson);
					if (evt?.Log != null)
					{
						MapLogVariables(scriptObject, evt.Log);
						triggeringUserId = evt.Log.LoggedByUserId;
					}
					break;
				}
				case WorkflowTriggerEventType.CalendarEventAdded:
				case WorkflowTriggerEventType.CalendarEventUpdated:
				{
					var item = TryDeserialize<CalendarEventAddedEvent>(eventPayloadJson)?.Item
					           ?? TryDeserialize<CalendarEventUpdatedEvent>(eventPayloadJson)?.Item;
					if (item != null)
					{
						MapCalendarVariables(scriptObject, item);
						triggeringUserId = item.CreatorUserId;
					}
					break;
				}
				case WorkflowTriggerEventType.ShiftCreated:
				case WorkflowTriggerEventType.ShiftUpdated:
				{
					var shiftEvt = TryDeserialize<ShiftCreatedEvent>(eventPayloadJson);
					var shiftUpdEvt = TryDeserialize<ShiftUpdatedEvent>(eventPayloadJson);
					var shiftItem = shiftEvt?.Item ?? shiftUpdEvt?.Item;
					if (shiftItem != null) MapShiftVariables(scriptObject, shiftItem, shiftEvt?.DepartmentNumber ?? shiftUpdEvt?.DepartmentNumber);
					break;
				}
				case WorkflowTriggerEventType.ResourceOrderAdded:
				{
					var evt = TryDeserialize<ResourceOrderAddedEvent>(eventPayloadJson);
					if (evt?.Order != null) MapResourceOrderVariables(scriptObject, evt.Order);
					break;
				}
				case WorkflowTriggerEventType.ShiftTradeRequested:
				{
					var evt = TryDeserialize<ShiftTradeRequestedEvent>(eventPayloadJson);
					if (evt != null)
					{
						var st = new ScriptObject();
						st["id"] = evt.ShiftSignupTradeId;
						st["department_number"] = evt.DepartmentNumber;
						scriptObject["shift_trade"] = st;
					}
					break;
				}
				case WorkflowTriggerEventType.ShiftTradeFilled:
				{
					var evt = TryDeserialize<ShiftTradeFilledEvent>(eventPayloadJson);
					if (evt != null)
					{
						var st = new ScriptObject();
						st["id"] = evt.ShiftSignupTradeId;
						st["filled_by_user_id"] = evt.UserId;
						st["department_number"] = evt.DepartmentNumber;
						scriptObject["shift_trade"] = st;
						triggeringUserId = evt.UserId;
					}
					break;
				}
				case WorkflowTriggerEventType.MessageSent:
				{
					var evt = TryDeserialize<MessageSentEvent>(eventPayloadJson);
					if (evt?.Message != null)
					{
						MapMessageVariables(scriptObject, evt.Message);
						triggeringUserId = evt.Message.SendingUserId;
					}
					break;
				}
				case WorkflowTriggerEventType.TrainingAdded:
				case WorkflowTriggerEventType.TrainingUpdated:
				{
					var trainingItem = TryDeserialize<TrainingAddedEvent>(eventPayloadJson)?.Training
					                   ?? TryDeserialize<TrainingUpdatedEvent>(eventPayloadJson)?.Training;
					if (trainingItem != null)
					{
						MapTrainingVariables(scriptObject, trainingItem);
						triggeringUserId = trainingItem.CreatedByUserId;
					}
					break;
				}
				case WorkflowTriggerEventType.InventoryAdjusted:
				{
					var evt = TryDeserialize<InventoryAdjustedEvent>(eventPayloadJson);
					if (evt?.Inventory != null)
					{
						MapInventoryVariables(scriptObject, evt.Inventory, evt.PreviousAmount);
						triggeringUserId = evt.Inventory.AddedByUserId;
					}
					break;
				}
				case WorkflowTriggerEventType.CertificationExpiring:
				{
					var evt = TryDeserialize<CertificationExpiringEvent>(eventPayloadJson);
					if (evt?.Certification != null)
					{
						MapCertificationVariables(scriptObject, evt.Certification, evt.DaysUntilExpiry);
						triggeringUserId = evt.Certification.UserId;
					}
					break;
				}
				case WorkflowTriggerEventType.FormSubmitted:
				{
					var evt = TryDeserialize<FormSubmittedEvent>(eventPayloadJson);
					if (evt?.Form != null)
					{
						var form = new ScriptObject();
						form["id"] = evt.Form.FormId;
						form["name"] = evt.Form.Name;
						form["type"] = evt.Form.Type;
						form["submitted_data"] = evt.SubmittedData;
						form["submitted_by_user_id"] = evt.SubmittedByUserId;
						form["submitted_on"] = evt.SubmittedOn;
						scriptObject["form"] = form;
						triggeringUserId = evt.SubmittedByUserId;
					}
					break;
				}
				case WorkflowTriggerEventType.PersonnelRoleChanged:
				{
					var evt = TryDeserialize<PersonnelRoleChangedEvent>(eventPayloadJson);
					if (evt != null)
					{
						var roleChange = new ScriptObject();
						roleChange["user_id"] = evt.UserId;
						roleChange["role_id"] = evt.PersonnelRoleId;
						roleChange["role_name"] = evt.RoleName;
						roleChange["role_description"] = evt.RoleDescription;
						roleChange["action"] = evt.Action;
						scriptObject["role_change"] = roleChange;
						triggeringUserId = evt.UserId;
					}
					break;
				}
				case WorkflowTriggerEventType.GroupAdded:
				case WorkflowTriggerEventType.GroupUpdated:
				{
					var grp = TryDeserialize<GroupAddedEvent>(eventPayloadJson)?.Group
					          ?? TryDeserialize<GroupUpdatedEvent>(eventPayloadJson)?.Group;
					if (grp != null) MapGroupVariables(scriptObject, grp, "group");
					break;
				}
				case WorkflowTriggerEventType.CommandEstablished:
				case WorkflowTriggerEventType.CommandTransferred:
				case WorkflowTriggerEventType.IncidentClosed:
				case WorkflowTriggerEventType.ResourceAssigned:
				case WorkflowTriggerEventType.ResourceReleased:
				case WorkflowTriggerEventType.ObjectiveCompleted:
				case WorkflowTriggerEventType.CriticalParDetected:
				case WorkflowTriggerEventType.IncidentRoleAssigned:
				case WorkflowTriggerEventType.AdHocResourceCreated:
				case WorkflowTriggerEventType.IncidentChannelOpened:
				case WorkflowTriggerEventType.PublicIncidentNoteAdded:
				case WorkflowTriggerEventType.InternalIncidentNoteAdded:
				case WorkflowTriggerEventType.PublicIncidentDocumentAdded:
				case WorkflowTriggerEventType.InternalIncidentDocumentAdded:
				case WorkflowTriggerEventType.IncidentNoteRemoved:
				case WorkflowTriggerEventType.IncidentDocumentRemoved:
				case WorkflowTriggerEventType.IncidentActionPlanUpdated:
				case WorkflowTriggerEventType.IncidentCommandPostUpdated:
				case WorkflowTriggerEventType.IncidentPublicSharingEnabled:
				case WorkflowTriggerEventType.IncidentPublicSharingDisabled:
				{
					triggeringUserId = MapIncidentVariables(scriptObject, eventPayloadJson);
					break;
				}
				case WorkflowTriggerEventType.RunCardActivated:
				{
					var evt = TryDeserialize<RunCardActivatedEvent>(eventPayloadJson);
					if (evt != null)
					{
						var rc = new ScriptObject();
						rc["call_id"] = evt.CallId;
						rc["run_card_id"] = evt.RunCardId;
						rc["run_card_name"] = evt.RunCardName ?? string.Empty;
						rc["alarm_level"] = evt.AlarmLevel;
						rc["mode"] = evt.ModeUsed;
						rc["was_auto_dispatched"] = evt.WasAutoDispatched;
						rc["unit_count"] = evt.UnitIds?.Count ?? 0;
						rc["personnel_count"] = evt.UserIds?.Count ?? 0;
						scriptObject["run_card"] = rc;
					}
					break;
				}
				case WorkflowTriggerEventType.CallAlarmEscalated:
				{
					var evt = TryDeserialize<CallAlarmEscalatedEvent>(eventPayloadJson);
					if (evt != null)
					{
						var esc = new ScriptObject();
						esc["call_id"] = evt.CallId;
						esc["previous_alarm_level"] = evt.PreviousAlarmLevel;
						esc["new_alarm_level"] = evt.NewAlarmLevel;
						esc["added_unit_count"] = evt.AddedUnitIds?.Count ?? 0;
						esc["added_personnel_count"] = evt.AddedUserIds?.Count ?? 0;
						scriptObject["escalation"] = esc;
					}
					break;
				}
				case WorkflowTriggerEventType.DispatchShortfallDetected:
				{
					var evt = TryDeserialize<DispatchShortfallEvent>(eventPayloadJson);
					if (evt != null)
					{
						var sf = new ScriptObject();
						sf["call_id"] = evt.CallId;
						sf["run_card_id"] = evt.RunCardId;
						sf["alarm_level"] = evt.AlarmLevel;
						sf["shortfall_count"] = evt.Shortfalls?.Count ?? 0;
						sf["summary"] = evt.Shortfalls != null
							? string.Join("; ", evt.Shortfalls.Select(s => $"{(s.IsUnitRequirement ? "Unit type" : "Role")} {s.TypeOrRoleName ?? s.TypeOrRoleId.ToString()}: {s.FilledCount}/{s.RequiredCount}"))
							: string.Empty;
						scriptObject["shortfall"] = sf;
					}
					break;
				}
				case WorkflowTriggerEventType.StationCoverageGapDetected:
				{
					var evt = TryDeserialize<StationCoverageGapEvent>(eventPayloadJson);
					if (evt != null)
					{
						var gap = new ScriptObject();
						gap["call_id"] = evt.CallId ?? 0;
						gap["gap_count"] = evt.MoveUps?.Count ?? 0;
						gap["summary"] = evt.MoveUps != null
							? string.Join("; ", evt.MoveUps.Select(m => $"{m.StationGroupName}: {m.AvailableAfterDispatch}/{m.MinimumRequired} {(m.UnitTypeName ?? m.PersonnelRoleName ?? string.Empty)}"))
							: string.Empty;
						scriptObject["coverage_gap"] = gap;
					}
					break;
				}
				case WorkflowTriggerEventType.RecordCreated:
				case WorkflowTriggerEventType.RecordSubmittedForReview:
				case WorkflowTriggerEventType.RecordReturnedForCorrection:
				case WorkflowTriggerEventType.RecordFinalized:
				case WorkflowTriggerEventType.RecordAmended:
				case WorkflowTriggerEventType.RecordVoided:
				case WorkflowTriggerEventType.RecordCancelled:
				case WorkflowTriggerEventType.RecordSubmissionQueued:
				case WorkflowTriggerEventType.RecordSubmissionAccepted:
				case WorkflowTriggerEventType.RecordSubmissionRejected:
				case WorkflowTriggerEventType.RecordSubmissionFailed:
				{
					// Records (RMS): the payload is the outbox snapshot carried by RecordsWorkflowEvent; it is never
					// rehydrated from current record state, so a retry sees exactly what the original run saw.
					var evt = TryDeserialize<RecordsWorkflowEvent>(eventPayloadJson);
					if (evt != null)
						triggeringUserId = MapRecordsEventVariables(scriptObject, evt);
					break;
				}
			}

			await AddCommonUserVariablesAsync(scriptObject, departmentId, triggeringUserId);

			return scriptObject;
		}

		public IReadOnlyList<TemplateVariableDescriptor> GetVariableDescriptors(WorkflowTriggerEventType eventType)
		{
			return WorkflowTemplateVariableCatalog.GetVariableCatalog(eventType);
		}

		// ── Common Variable Mappers ───────────────────────────────────────────────────

		private static void AddCommonDepartmentVariables(ScriptObject obj, Department dept, string phoneNumber, DepartmentEmailBranding branding)
		{
			var d = new ScriptObject();
			d["id"] = dept?.DepartmentId ?? 0;
			d["name"] = dept?.Name ?? string.Empty;
			d["code"] = dept?.Code ?? string.Empty;
			d["type"] = dept?.DepartmentType ?? string.Empty;
			d["time_zone"] = dept?.TimeZone ?? string.Empty;
			d["use_24_hour_time"] = dept?.Use24HourTime ?? false;
			d["created_on"] = dept?.CreatedOn;
			d["phone_number"] = phoneNumber ?? string.Empty;

			// Department Profile identity (RMS plan section 4.10.1) for workflow email bodies. The logo URL is
			// gated by the same opt-in as the system emails, so a workflow author cannot leak a masthead the
			// department never enabled; the name and website are plain identity and always available.
			d["display_name"] = !string.IsNullOrWhiteSpace(branding?.DisplayName) ? branding.DisplayName : (dept?.Name ?? string.Empty);
			d["logo_url"] = branding != null && branding.Enabled ? (branding.LogoUrl ?? string.Empty) : string.Empty;
			d["website"] = branding?.Website ?? string.Empty;

			var addr = new ScriptObject();
			if (dept?.Address != null)
			{
				addr["street"] = dept.Address.Address1 ?? string.Empty;
				addr["city"] = dept.Address.City ?? string.Empty;
				addr["state"] = dept.Address.State ?? string.Empty;
				addr["postal_code"] = dept.Address.PostalCode ?? string.Empty;
				addr["country"] = dept.Address.Country ?? string.Empty;
				addr["full"] = dept.Address.FormatAddress() ?? string.Empty;
			}
			else
			{
				addr["street"] = string.Empty;
				addr["city"] = string.Empty;
				addr["state"] = string.Empty;
				addr["postal_code"] = string.Empty;
				addr["country"] = string.Empty;
				addr["full"] = string.Empty;
			}
			d["address"] = addr;
			obj["department"] = d;
		}

		private static void AddCommonTimestampVariables(ScriptObject obj, string timeZoneId)
		{
			var utcNow = DateTime.UtcNow;
			DateTime deptNow;
			try
			{
				// NodaTime tzdb (no ICU / OS tzdata). DHI runs in globalization-invariant mode where
				// TimeZoneInfo.FindSystemTimeZoneById can't map a Windows zone id (TimeZoneNotFoundException).
				// DateTimeHelpers.GetLocalDateTime resolves UTC -> department-local via the embedded tzdb.
				deptNow = string.IsNullOrWhiteSpace(timeZoneId)
					? utcNow
					: DateTimeHelpers.GetLocalDateTime(utcNow, timeZoneId);
			}
			catch
			{
				deptNow = utcNow;
			}

			var ts = new ScriptObject();
			ts["utc_now"] = utcNow;
			ts["department_now"] = deptNow;
			ts["date"] = deptNow.ToString("yyyy-MM-dd");
			ts["time"] = deptNow.ToString("HH:mm:ss");
			ts["day_of_week"] = deptNow.DayOfWeek.ToString();
			obj["timestamp"] = ts;
		}

		private static string MapIncidentVariables(ScriptObject obj, string eventPayloadJson)
		{
			JObject payload;
			try
			{
				payload = JObject.Parse(eventPayloadJson ?? "{}");
			}
			catch (JsonException)
			{
				payload = new JObject();
			}

			JToken Find(params string[] names)
			{
				foreach (var name in names)
				{
					if (payload.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var value))
						return value;
				}
				return null;
			}

			string Text(params string[] names) => Find(names)?.Type == JTokenType.Null ? null : Find(names)?.ToString();
			object Scalar(params string[] names)
			{
				var token = Find(names);
				return token == null || token.Type == JTokenType.Null ? null : ((JValue)token).Value;
			}

			var incident = new ScriptObject();
			incident["command_id"] = Text("IncidentCommandId");
			incident["call_id"] = Scalar("CallId") ?? 0;
			incident["department_id"] = Scalar("DepartmentId") ?? 0;
			incident["user_id"] = Text("EstablishedByUserId", "ToUserId", "UserId", "CreatedByUserId", "UploadedByUserId", "UpdatedByUserId", "RemovedByUserId");
			incident["name"] = Text("Name");
			incident["visibility"] = Scalar("Visibility") ?? 0;
			incident["note_id"] = Text("IncidentNoteId");
			incident["note_type"] = Scalar("NoteType") ?? 0;
			incident["title"] = Text("Title");
			incident["body"] = Text("Body");
			incident["containment_percent"] = Scalar("ContainmentPercent");
			incident["attachment_id"] = Text("IncidentAttachmentId");
			incident["file_name"] = Text("FileName");
			incident["content_type"] = Text("ContentType");
			incident["content_length"] = Scalar("ContentLength") ?? 0L;
			incident["sha256_hash"] = Text("Sha256Hash");
			incident["description"] = Text("Description");
			incident["action_plan"] = Text("ActionPlan");
			incident["latitude"] = Text("Latitude");
			incident["longitude"] = Text("Longitude");
			incident["enabled"] = Scalar("Enabled") ?? false;
			obj["incident"] = incident;

			return incident["user_id"]?.ToString();
		}

		private async Task AddCommonUserVariablesAsync(ScriptObject obj, int departmentId, string userId)
		{
			var u = new ScriptObject();
			if (!string.IsNullOrWhiteSpace(userId))
			{
				var profile = await _userProfileService.GetProfileByUserIdAsync(userId);

				// The identification number is department-scoped and protected (plan 5.1). Workflow
				// variables feed outbound email, SMS and webhooks with no reveal step, so a protected
				// department renders the placeholder rather than the number — and the legacy global
				// column is not consulted at all, since it answers for the wrong department.
				var sensitive = await _memberSensitiveDataService.GetByDepartmentAndUserAsync(departmentId, userId);
				u["id"] = userId;
				u["first_name"] = profile?.FirstName ?? string.Empty;
				u["last_name"] = profile?.LastName ?? string.Empty;
				u["full_name"] = profile?.FullName?.AsFirstNameLastName ?? string.Empty;
				u["email"] = profile?.MembershipEmail ?? string.Empty;
				u["mobile_number"] = profile?.MobileNumber ?? string.Empty;
				u["home_number"] = profile?.HomeNumber ?? string.Empty;
				u["identification_number"] = ProtectedDataEnvelope.SafeDisplay(sensitive?.IdentificationNumber) ?? string.Empty;
				u["username"] = string.Empty; // populated from IdentityUser if needed
				u["time_zone"] = profile?.TimeZone ?? string.Empty;
			}
			else
			{
				u["id"] = string.Empty;
				u["first_name"] = string.Empty;
				u["last_name"] = string.Empty;
				u["full_name"] = string.Empty;
				u["email"] = string.Empty;
				u["mobile_number"] = string.Empty;
				u["home_number"] = string.Empty;
				u["identification_number"] = string.Empty;
				u["username"] = string.Empty;
				u["time_zone"] = string.Empty;
			}
			obj["user"] = u;
		}

		// ── Event-Specific Mappers ────────────────────────────────────────────────────

		private async Task MapCallVariablesAsync(ScriptObject obj, Call call, int departmentId, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var c = new ScriptObject();
			c["id"] = call.CallId;
			c["number"] = call.Number ?? string.Empty;
			// ADP (plan section 8): workflows render these into outbound email/SMS/webhooks and run
			// UNATTENDED — no grant can exist here, so a protected department's cataloged values must
			// degrade to the REDACTED placeholder. Ciphertext must never reach a template, and the
			// system-generated call number stays plaintext as the safe identifier.
			c["name"] = ProtectedDataEnvelope.SafeDisplay(call.Name) ?? string.Empty;
			c["nature"] = ProtectedDataEnvelope.SafeDisplay(call.NatureOfCall) ?? string.Empty;
			c["notes"] = ProtectedDataEnvelope.SafeDisplay(call.Notes) ?? string.Empty;
			c["address"] = ProtectedDataEnvelope.SafeDisplay(call.Address) ?? string.Empty;
			c["geo_location"] = ProtectedDataEnvelope.SafeDisplay(call.GeoLocationData) ?? string.Empty;
			c["type"] = ProtectedDataEnvelope.SafeDisplay(call.Type) ?? string.Empty;
			c["incident_number"] = ProtectedDataEnvelope.SafeDisplay(call.IncidentNumber) ?? string.Empty;
			c["reference_number"] = ProtectedDataEnvelope.SafeDisplay(call.ReferenceNumber) ?? string.Empty;
			c["map_page"] = call.MapPage ?? string.Empty;
			c["priority"] = call.Priority;
			c["priority_text"] = call.GetPriorityText();
			c["is_critical"] = call.IsCritical;
			c["state"] = call.State;
			c["state_text"] = call.GetStateText();
			c["source"] = call.CallSource;
			c["external_id"] = ProtectedDataEnvelope.SafeDisplay(call.ExternalIdentifier) ?? string.Empty;
			c["logged_on"] = call.LoggedOn;
			c["closed_on"] = call.ClosedOn;
			c["completed_notes"] = ProtectedDataEnvelope.SafeDisplay(call.CompletedNotes) ?? string.Empty;
			c["contact_name"] = ProtectedDataEnvelope.SafeDisplay(call.ContactName) ?? string.Empty;
			c["contact_number"] = ProtectedDataEnvelope.SafeDisplay(call.ContactNumber) ?? string.Empty;
			c["w3w"] = ProtectedDataEnvelope.SafeDisplay(call.W3W) ?? string.Empty;
			c["dispatch_count"] = call.DispatchCount;
			c["dispatch_on"] = call.DispatchOn;
			c["form_data"] = ProtectedDataEnvelope.SafeDisplay(call.CallFormData) ?? string.Empty;
			c["is_deleted"] = call.IsDeleted;
			c["deleted_reason"] = ProtectedDataEnvelope.SafeDisplay(call.DeletedReason) ?? string.Empty;

			// ── Personnel dispatches ──────────────────────────────────────────────────
			// Pre-fetch all user profiles and group/role data needed for enrichment
			var dispatchUserIds = call.Dispatches != null
				? call.Dispatches.Where(d => !string.IsNullOrWhiteSpace(d.UserId)).Select(d => d.UserId).Distinct().ToList()
				: new List<string>();

			Dictionary<string, UserProfile> profileMap = new Dictionary<string, UserProfile>();
			IReadOnlyDictionary<string, DepartmentMemberSensitiveData> sensitiveByUser =
				new Dictionary<string, DepartmentMemberSensitiveData>();
			if (dispatchUserIds.Count > 0)
			{
				var profiles = await _userProfileService.GetSelectedUserProfilesAsync(dispatchUserIds);
				profileMap = profiles?.ToDictionary(p => p.UserId, p => p) ?? new Dictionary<string, UserProfile>();

				// Identification numbers are department-scoped and protected (plan 5.1); this context
				// has no reveal step, so they render as the placeholder for a protected department.
				sensitiveByUser = await _memberSensitiveDataService.GetResolvedForDepartmentAsync(departmentId, null, null);
			}

			// Pre-fetch department roles once (used for role-name enrichment below)
			List<PersonnelRole> allDeptRoles = dispatchUserIds.Count > 0
				? await _personnelRolesService.GetRolesForDepartmentAsync(departmentId) ?? new List<PersonnelRole>()
				: new List<PersonnelRole>();

			// Pre-fetch per-user group membership in parallel to avoid N+1
			Dictionary<string, DepartmentGroup> userGroupMap = new Dictionary<string, DepartmentGroup>();
			if (dispatchUserIds.Count > 0)
			{
				var groupTasks = dispatchUserIds.Select(uid =>
					_departmentGroupsService.GetGroupForUserAsync(uid, departmentId)
						.ContinueWith(t => (UserId: uid, Group: t.Result), TaskContinuationOptions.ExecuteSynchronously));
				var groupResults = await Task.WhenAll(groupTasks);
				foreach (var (uid, grp) in groupResults)
					if (grp != null)
						userGroupMap[uid] = grp;
			}

			// Pre-fetch per-user role assignments in parallel to avoid N+1
			Dictionary<string, List<PersonnelRole>> userRolesMap = new Dictionary<string, List<PersonnelRole>>();
			if (dispatchUserIds.Count > 0)
			{
				var roleTasks = dispatchUserIds.Select(uid =>
					_personnelRolesService.GetRolesForUserAsync(uid, departmentId)
						.ContinueWith(t => (UserId: uid, Roles: t.Result), TaskContinuationOptions.ExecuteSynchronously));
				var roleResults = await Task.WhenAll(roleTasks);
				foreach (var (uid, roles) in roleResults)
					userRolesMap[uid] = roles ?? new List<PersonnelRole>();
			}

			var dispatches = new ScriptArray();
			if (call.Dispatches != null)
			{
				foreach (var d in call.Dispatches)
				{
					var item = new ScriptObject();
					item["user_id"] = d.UserId ?? string.Empty;
					item["dispatch_count"] = d.DispatchCount;
					item["dispatched_on"] = d.DispatchedOn;

					// Enrich with profile data
					if (!string.IsNullOrWhiteSpace(d.UserId) && profileMap.TryGetValue(d.UserId, out var profile))
					{
						item["first_name"] = profile.FirstName ?? string.Empty;
						item["last_name"] = profile.LastName ?? string.Empty;
						item["full_name"] = profile.FullName?.AsFirstNameLastName ?? string.Empty;
						item["email"] = profile.MembershipEmail ?? string.Empty;
						item["mobile_number"] = profile.MobileNumber ?? string.Empty;
						item["identification_number"] = sensitiveByUser.TryGetValue(d.UserId, out var sensitive)
							? ProtectedDataEnvelope.SafeDisplay(sensitive.IdentificationNumber) ?? string.Empty
							: string.Empty;
					}
					else
					{
						item["first_name"] = string.Empty;
						item["last_name"] = string.Empty;
						item["full_name"] = string.Empty;
						item["email"] = string.Empty;
						item["mobile_number"] = string.Empty;
						item["identification_number"] = string.Empty;
					}

					// Enrich with group membership (synchronous lookup from preloaded map)
					if (!string.IsNullOrWhiteSpace(d.UserId) && userGroupMap.TryGetValue(d.UserId, out var userGroup))
					{
						item["group_id"] = userGroup.DepartmentGroupId;
						item["group_name"] = userGroup.Name ?? string.Empty;
					}
					else
					{
						item["group_id"] = 0;
						item["group_name"] = string.Empty;
					}

					// Enrich with role names (synchronous lookup from preloaded map)
					if (!string.IsNullOrWhiteSpace(d.UserId) && userRolesMap.TryGetValue(d.UserId, out var userRoles))
					{
						var roleNames = userRoles.Select(r => r.Name ?? string.Empty).ToList();
						item["role_names"] = string.Join(", ", roleNames);

						var roleArray = new ScriptArray();
						foreach (var roleName in roleNames)
							roleArray.Add(roleName);
						item["roles"] = roleArray;
					}
					else
					{
						item["role_names"] = string.Empty;
						item["roles"] = new ScriptArray();
					}

					dispatches.Add(item);
				}
			}
			c["dispatches"] = dispatches;

			// ── Unit dispatches ──────────────────────────────────────────────────────
			var unitDispatches = new ScriptArray();
			if (call.UnitDispatches != null)
			{
				foreach (var d in call.UnitDispatches)
				{
					var item = new ScriptObject();
					item["unit_id"] = d.UnitId;
					item["dispatch_count"] = d.DispatchCount;
					item["dispatched_on"] = d.DispatchedOn;

					// Resolve unit name – prefer navigation property, fall back to service lookup
					Unit unit = d.Unit;
					if (unit == null && d.UnitId > 0)
						unit = await _unitsService.GetUnitByIdAsync(d.UnitId);

					item["unit_name"] = unit?.Name ?? string.Empty;
					item["unit_type"] = unit?.Type ?? string.Empty;
					item["vin"] = unit?.VIN ?? string.Empty;
					item["plate_number"] = unit?.PlateNumber ?? string.Empty;
					item["station_group_id"] = unit?.StationGroupId ?? 0;

					unitDispatches.Add(item);
				}
			}
			c["unit_dispatches"] = unitDispatches;

			// ── Group dispatches ──────────────────────────────────────────────────────
			var groupDispatches = new ScriptArray();
			if (call.GroupDispatches != null)
			{
				foreach (var d in call.GroupDispatches)
				{
					var item = new ScriptObject();
					item["group_id"] = d.DepartmentGroupId;
					item["dispatch_count"] = d.DispatchCount;
					item["dispatched_on"] = d.DispatchedOn;

					// Resolve group name – prefer navigation property, fall back to service lookup
					DepartmentGroup group = d.Group;
					if (group == null && d.DepartmentGroupId > 0)
						group = await _departmentGroupsService.GetGroupByIdAsync(d.DepartmentGroupId);

					item["group_name"] = group?.Name ?? string.Empty;
					item["group_type"] = group?.Type ?? 0;
					item["dispatch_email"] = group?.DispatchEmail ?? string.Empty;
					item["latitude"] = group?.Latitude ?? string.Empty;
					item["longitude"] = group?.Longitude ?? string.Empty;

					groupDispatches.Add(item);
				}
			}
			c["group_dispatches"] = groupDispatches;

			// ── Role dispatches ──────────────────────────────────────────────────────
			var roleDispatches = new ScriptArray();
			if (call.RoleDispatches != null)
			{
				foreach (var d in call.RoleDispatches)
				{
					var item = new ScriptObject();
					item["role_id"] = d.RoleId;
					item["dispatch_count"] = d.DispatchCount;
					item["dispatched_on"] = d.DispatchedOn;

					// Resolve role name – prefer navigation property, fall back to service lookup
					PersonnelRole role = d.Role;
					if (role == null && d.RoleId > 0)
						role = await _personnelRolesService.GetRoleByIdAsync(d.RoleId);

					item["role_name"] = role?.Name ?? string.Empty;
					item["role_description"] = role?.Description ?? string.Empty;

					roleDispatches.Add(item);
				}
			}
			c["role_dispatches"] = roleDispatches;

			// ── Call notes ──────────────────────────────────────────────────────
			var notesList = new ScriptArray();
			if (call.CallNotes != null)
			{
				foreach (var n in call.CallNotes)
				{
					var item = new ScriptObject();
					item["note"] = ProtectedDataEnvelope.SafeDisplay(n.Note) ?? string.Empty;
					item["source"] = n.Source.ToString();
					item["timestamp"] = n.Timestamp;
					item["user_id"] = n.UserId ?? string.Empty;
					notesList.Add(item);
				}
			}
			c["notes_list"] = notesList;

			// ── Contacts ──────────────────────────────────────────────────────
			var contacts = new ScriptArray();
			if (call.Contacts != null)
			{
				foreach (var ct in call.Contacts)
				{
					var item = new ScriptObject();
					item["contact_id"] = ct.ContactId ?? string.Empty;
					item["contact_type"] = ct.GetContactTypeName();
					contacts.Add(item);
				}
			}
			c["contacts"] = contacts;


			obj["call"] = c;
		}

		private static void MapUnitStatusVariables(ScriptObject obj, UnitState status, UnitState previousStatus)
		{
			var s = new ScriptObject();
			if (status != null)
			{
				s["id"] = status.UnitStateId;
				s["state"] = status.State;
				s["state_text"] = status.GetStatusText();
				s["timestamp"] = status.Timestamp;
				s["note"] = ProtectedDataEnvelope.SafeDisplay(status.Note) ?? string.Empty;
				s["latitude"] = status.Latitude;
				s["longitude"] = status.Longitude;
				s["destination_id"] = status.DestinationId;
			}
			obj["unit_status"] = s;

			var u = new ScriptObject();
			if (status?.Unit != null)
			{
				u["id"] = status.Unit.UnitId;
				u["name"] = status.Unit.Name ?? string.Empty;
				u["type"] = status.Unit.Type ?? string.Empty;
				u["vin"] = status.Unit.VIN ?? string.Empty;
				u["plate_number"] = status.Unit.PlateNumber ?? string.Empty;
				u["station_group_id"] = status.Unit.StationGroupId;
			}
			obj["unit"] = u;

			var ps = new ScriptObject();
			if (previousStatus != null)
			{
				ps["state"] = previousStatus.State;
				ps["state_text"] = previousStatus.GetStatusText();
				ps["timestamp"] = previousStatus.Timestamp;
			}
			obj["previous_unit_status"] = ps;
		}

		private static void MapStaffingVariables(ScriptObject obj, UserState staffing, UserState previous)
		{
			var s = new ScriptObject();
			if (staffing != null)
			{
				s["id"] = staffing.UserStateId;
				s["state"] = staffing.State;
				s["state_text"] = staffing.GetStaffingText();
				s["timestamp"] = staffing.Timestamp;
				s["note"] = staffing.Note ?? string.Empty;
			}
			obj["staffing"] = s;

			var ps = new ScriptObject();
			if (previous != null)
			{
				ps["state"] = previous.State;
				ps["state_text"] = previous.GetStaffingText();
				ps["timestamp"] = previous.Timestamp;
			}
			obj["previous_staffing"] = ps;
		}

		private static void MapPersonnelStatusVariables(ScriptObject obj, ActionLog status, ActionLog previous)
		{
			var s = new ScriptObject();
			if (status != null)
			{
				s["id"] = status.ActionLogId;
				s["action_type"] = status.ActionTypeId;
				s["action_text"] = status.GetActionText();
				s["timestamp"] = status.Timestamp;
				s["geo_location"] = ProtectedDataEnvelope.SafeDisplay(status.GeoLocationData) ?? string.Empty;
				s["destination_id"] = status.DestinationId;
				s["note"] = ProtectedDataEnvelope.SafeDisplay(status.Note) ?? string.Empty;
			}
			obj["status"] = s;

			var ps = new ScriptObject();
			if (previous != null)
			{
				ps["action_type"] = previous.ActionTypeId;
				ps["action_text"] = previous.GetActionText();
				ps["timestamp"] = previous.Timestamp;
			}
			obj["previous_status"] = ps;
		}

		private static void MapDocumentVariables(ScriptObject obj, Document doc)
		{
			var d = new ScriptObject();
			d["id"] = doc.DocumentId;
			d["name"] = doc.Name ?? string.Empty;
			d["category"] = doc.Category ?? string.Empty;
			d["description"] = doc.Description ?? string.Empty;
			d["type"] = doc.Type ?? string.Empty;
			d["filename"] = doc.Filename ?? string.Empty;
			d["admins_only"] = doc.AdminsOnly;
			d["added_on"] = doc.AddedOn;
			obj["document"] = d;
		}

		private static void MapNoteVariables(ScriptObject obj, Note note)
		{
			var n = new ScriptObject();
			n["id"] = note.NoteId;
			n["title"] = note.Title ?? string.Empty;
			n["body"] = note.Body ?? string.Empty;
			n["color"] = note.Color ?? string.Empty;
			n["category"] = note.Category ?? string.Empty;
			n["is_admin_only"] = note.IsAdminOnly;
			n["added_on"] = note.AddedOn;
			n["expires_on"] = note.ExpiresOn;
			obj["note"] = n;
		}

		private static void MapUnitVariables(ScriptObject obj, Unit unit)
		{
			var u = new ScriptObject();
			u["id"] = unit.UnitId;
			u["name"] = unit.Name ?? string.Empty;
			u["type"] = unit.Type ?? string.Empty;
			u["vin"] = unit.VIN ?? string.Empty;
			u["plate_number"] = unit.PlateNumber ?? string.Empty;
			u["station_group_id"] = unit.StationGroupId;
			u["four_wheel"] = unit.FourWheel;
			u["special_permit"] = unit.SpecialPermit;
			obj["unit"] = u;
		}

		private static void MapLogVariables(ScriptObject obj, Log log)
		{
			var l = new ScriptObject();
			l["id"] = log.LogId;
			l["narrative"] = log.Narrative ?? string.Empty;
			l["type"] = log.Type ?? string.Empty;
			l["log_type"] = log.LogType;
			l["external_id"] = log.ExternalId ?? string.Empty;
			l["initial_report"] = log.InitialReport ?? string.Empty;
			l["course"] = log.Course ?? string.Empty;
			l["course_code"] = log.CourseCode ?? string.Empty;
			l["instructors"] = log.Instructors ?? string.Empty;
			l["cause"] = log.Cause ?? string.Empty;
			l["contact_name"] = log.ContactName ?? string.Empty;
			l["contact_number"] = log.ContactNumber ?? string.Empty;
			l["location"] = log.Location ?? string.Empty;
			l["started_on"] = log.StartedOn;
			l["ended_on"] = log.EndedOn;
			l["logged_on"] = log.LoggedOn;
			l["other_agencies"] = log.OtherAgencies ?? string.Empty;
			l["other_units"] = log.OtherUnits ?? string.Empty;
			l["other_personnel"] = log.OtherPersonnel ?? string.Empty;
			l["call_id"] = log.CallId;
			obj["log"] = l;
		}

		private static void MapCalendarVariables(ScriptObject obj, CalendarItem item)
		{
			var c = new ScriptObject();
			c["id"] = item.CalendarItemId;
			c["title"] = item.Title ?? string.Empty;
			c["description"] = item.Description ?? string.Empty;
			c["location"] = item.Location ?? string.Empty;
			c["start"] = item.Start;
			c["end"] = item.End;
			c["is_all_day"] = item.IsAllDay;
			c["item_type"] = item.ItemType;
			c["signup_type"] = item.SignupType;
			c["is_public"] = item.Public;
			obj["calendar"] = c;
		}

		private static void MapShiftVariables(ScriptObject obj, Shift shift, string deptNumber)
		{
			var s = new ScriptObject();
			s["id"] = shift.ShiftId;
			s["name"] = shift.Name ?? string.Empty;
			s["code"] = shift.Code ?? string.Empty;
			s["schedule_type"] = shift.ScheduleType;
			s["assignment_type"] = shift.AssignmentType;
			s["color"] = shift.Color ?? string.Empty;
			s["start_day"] = shift.StartDay;
			s["start_time"] = shift.StartTime ?? string.Empty;
			s["end_time"] = shift.EndTime ?? string.Empty;
			s["hours"] = shift.Hours;
			s["department_number"] = deptNumber ?? string.Empty;
			obj["shift"] = s;
		}

		private static void MapResourceOrderVariables(ScriptObject obj, ResourceOrder order)
		{
			var o = new ScriptObject();
			o["id"] = order.ResourceOrderId;
			o["title"] = order.Title ?? string.Empty;
			o["incident_number"] = order.IncidentNumber ?? string.Empty;
			o["incident_name"] = order.IncidentName ?? string.Empty;
			o["incident_address"] = order.IncidentAddress ?? string.Empty;
			o["summary"] = order.Summary ?? string.Empty;
			o["open_date"] = order.OpenDate;
			o["needed_by"] = order.NeededBy;
			o["contact_name"] = order.ContactName ?? string.Empty;
			o["contact_number"] = order.ContactNumber ?? string.Empty;
			o["special_instructions"] = order.SpecialInstructions ?? string.Empty;
			o["meetup_location"] = order.MeetupLocation ?? string.Empty;
			o["financial_code"] = order.FinancialCode ?? string.Empty;
			obj["order"] = o;
		}

		private static void MapMessageVariables(ScriptObject obj, Message msg)
		{
			var m = new ScriptObject();
			m["id"] = msg.MessageId;
			m["subject"] = msg.Subject ?? string.Empty;
			m["body"] = msg.Body ?? string.Empty;
			m["is_broadcast"] = msg.IsBroadcast;
			m["sent_on"] = msg.SentOn;
			m["type"] = msg.Type;
			m["recipients"] = msg.Recipients ?? string.Empty;
			m["expire_on"] = msg.ExpireOn;
			obj["message"] = m;
		}

		private static void MapTrainingVariables(ScriptObject obj, Training training)
		{
			var t = new ScriptObject();
			t["id"] = training.TrainingId;
			t["name"] = training.Name ?? string.Empty;
			t["description"] = training.Description ?? string.Empty;
			t["training_text"] = training.TrainingText ?? string.Empty;
			t["minimum_score"] = training.MinimumScore;
			t["created_on"] = training.CreatedOn;
			t["to_be_completed_by"] = training.ToBeCompletedBy;
			obj["training"] = t;
		}

		private static void MapInventoryVariables(ScriptObject obj, Inventory inventory, double previousAmount)
		{
			var i = new ScriptObject();
			i["id"] = inventory.InventoryId;
			i["type_name"] = inventory.Type?.Type ?? string.Empty;
			i["type_description"] = inventory.Type?.Description ?? string.Empty;
			i["unit_of_measure"] = inventory.Type?.UnitOfMesasure ?? string.Empty;
			i["batch"] = inventory.Batch ?? string.Empty;
			i["note"] = inventory.Note ?? string.Empty;
			i["location"] = inventory.Location ?? string.Empty;
			i["amount"] = inventory.Amount;
			i["previous_amount"] = previousAmount;
			i["timestamp"] = inventory.TimeStamp;
			i["group_id"] = inventory.GroupId;
			obj["inventory"] = i;
		}

		private static void MapCertificationVariables(ScriptObject obj, PersonnelCertification cert, int daysUntilExpiry)
		{
			// Cataloged since v6 (plan 5.1). Workflow variables feed outbound email, SMS and
			// webhooks with no reveal step, so a protected department's certification renders as the
			// placeholder — an expiry reminder can still say a certification is due without naming
			// the licence number.
			var c = new ScriptObject();
			c["id"] = cert.PersonnelCertificationId;
			c["name"] = ProtectedDataEnvelope.SafeDisplay(cert.Name) ?? string.Empty;
			c["number"] = ProtectedDataEnvelope.SafeDisplay(cert.Number) ?? string.Empty;
			c["type"] = ProtectedDataEnvelope.SafeDisplay(cert.Type) ?? string.Empty;
			c["area"] = ProtectedDataEnvelope.SafeDisplay(cert.Area) ?? string.Empty;
			c["issued_by"] = ProtectedDataEnvelope.SafeDisplay(cert.IssuedBy) ?? string.Empty;
			c["expires_on"] = cert.ExpiresOn;
			c["received_on"] = cert.RecievedOn;
			c["days_until_expiry"] = daysUntilExpiry;
			obj["certification"] = c;
		}

		private static void MapGroupVariables(ScriptObject obj, DepartmentGroup group, string key)
		{
			var g = new ScriptObject();
			if (group != null)
			{
				g["id"] = group.DepartmentGroupId;
				g["name"] = group.Name ?? string.Empty;
				g["type"] = group.Type;
				g["dispatch_email"] = group.DispatchEmail ?? string.Empty;
				g["message_email"] = group.MessageEmail ?? string.Empty;
				g["latitude"] = group.Latitude ?? string.Empty;
				g["longitude"] = group.Longitude ?? string.Empty;
				g["what3words"] = group.What3Words ?? string.Empty;

				var addr = new ScriptObject();
				if (group.Address != null)
				{
					addr["street"] = group.Address.Address1 ?? string.Empty;
					addr["city"] = group.Address.City ?? string.Empty;
					addr["state"] = group.Address.State ?? string.Empty;
					addr["postal_code"] = group.Address.PostalCode ?? string.Empty;
					addr["country"] = group.Address.Country ?? string.Empty;
				}
				g["address"] = addr;
			}
			obj[key] = g;
		}

		// ── Records (RMS) ────────────────────────────────────────────────────────────────

		/// <summary>Maps event.*, record.* and record_change.* from the dispatched snapshot; returns the author as the triggering user.</summary>
		private static string MapRecordsEventVariables(ScriptObject obj, RecordsWorkflowEvent evt)
		{
			var e = new ScriptObject();
			e["id"] = evt.EventId ?? string.Empty;
			e["name"] = evt.EventName ?? string.Empty;
			e["schema_version"] = evt.SchemaVersion;
			e["occurred_on"] = evt.OccurredOn;
			e["correlation_id"] = evt.CorrelationId ?? string.Empty;
			e["causation_id"] = evt.CausationId ?? string.Empty;
			e["sequence"] = evt.Sequence;
			e["is_replay"] = evt.IsReplay;
			e["origin_client"] = evt.OriginClient ?? RmsOriginClient.System.ToString();
			obj["event"] = e;

			var payload = evt.Payload ?? new JObject();
			var recordToken = payload["record"] as JObject;
			var record = ToScriptObject(recordToken);
			var recordId = recordToken?["id"]?.Type == JTokenType.String ? (string)recordToken["id"] : null;
			var isIncident = recordToken?["kind"]?.Type == JTokenType.String && string.Equals((string)recordToken["kind"], "IncidentReport", StringComparison.Ordinal);
			record["url"] = string.IsNullOrWhiteSpace(recordId)
				? string.Empty
				: $"{(Resgrid.Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/')}/User/{(isIncident ? "IncidentReports" : "Records")}/Details/{recordId}";
			obj["record"] = record;

			var change = ToScriptObject(payload["record_change"] as JObject);
			if (payload["extra"] is JObject extra)
			{
				// Transition-specific facts (e.g. the cancelled record's number_disposition) surface on record_change.
				foreach (var property in extra.Properties())
					change[property.Name] = ToScriptValue(property.Value);
			}
			obj["record_change"] = change;

			if (payload["review"] is JObject review)
				obj["review"] = ToScriptObject(review);

			if (payload["submission"] is JObject submission)
				obj["submission"] = ToScriptObject(submission);

			return recordToken?["author_user_id"]?.Type == JTokenType.String ? (string)recordToken["author_user_id"] : null;
		}

		private static ScriptObject ToScriptObject(JObject source)
		{
			var result = new ScriptObject();
			if (source == null)
				return result;

			foreach (var property in source.Properties())
				result[property.Name] = ToScriptValue(property.Value);

			return result;
		}

		private static object ToScriptValue(JToken token)
		{
			if (token == null)
				return null;

			switch (token.Type)
			{
				case JTokenType.Null:
				case JTokenType.Undefined:
					return null;
				case JTokenType.Object:
					return ToScriptObject((JObject)token);
				case JTokenType.Array:
					var array = new ScriptArray();
					foreach (var item in (JArray)token)
						array.Add(ToScriptValue(item));
					return array;
				default:
					return ((JValue)token).Value;
			}
		}

		private static T TryDeserialize<T>(string json) where T : class
		{
			try { return string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<T>(json); }
			catch { return null; }
		}
	}
}


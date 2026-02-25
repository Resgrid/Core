using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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

		public WorkflowTemplateContextBuilder(
			IDepartmentsService departmentsService,
			IDepartmentSettingsService departmentSettingsService,
			IUserProfileService userProfileService)
		{
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_userProfileService = userProfileService;
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

			AddCommonDepartmentVariables(scriptObject, department, phoneNumber);
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
						MapCallVariables(scriptObject, call);
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
			}

			await AddCommonUserVariablesAsync(scriptObject, triggeringUserId);

			return scriptObject;
		}

		public IReadOnlyList<TemplateVariableDescriptor> GetVariableDescriptors(WorkflowTriggerEventType eventType)
		{
			return WorkflowTemplateVariableCatalog.GetVariableCatalog(eventType);
		}

		// ── Common Variable Mappers ───────────────────────────────────────────────────

		private static void AddCommonDepartmentVariables(ScriptObject obj, Department dept, string phoneNumber)
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
				var tz = !string.IsNullOrWhiteSpace(timeZoneId)
					? TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)
					: TimeZoneInfo.Utc;
				deptNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
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

		private async Task AddCommonUserVariablesAsync(ScriptObject obj, string userId)
		{
			var u = new ScriptObject();
			if (!string.IsNullOrWhiteSpace(userId))
			{
				var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
				u["id"] = userId;
				u["first_name"] = profile?.FirstName ?? string.Empty;
				u["last_name"] = profile?.LastName ?? string.Empty;
				u["full_name"] = profile?.FullName?.AsFirstNameLastName ?? string.Empty;
				u["email"] = profile?.MembershipEmail ?? string.Empty;
				u["mobile_number"] = profile?.MobileNumber ?? string.Empty;
				u["home_number"] = profile?.HomeNumber ?? string.Empty;
				u["identification_number"] = profile?.IdentificationNumber ?? string.Empty;
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

		private static void MapCallVariables(ScriptObject obj, Call call)
		{
			var c = new ScriptObject();
			c["id"] = call.CallId;
			c["number"] = call.Number ?? string.Empty;
			c["name"] = call.Name ?? string.Empty;
			c["nature"] = call.NatureOfCall ?? string.Empty;
			c["notes"] = call.Notes ?? string.Empty;
			c["address"] = call.Address ?? string.Empty;
			c["geo_location"] = call.GeoLocationData ?? string.Empty;
			c["type"] = call.Type ?? string.Empty;
			c["incident_number"] = call.IncidentNumber ?? string.Empty;
			c["reference_number"] = call.ReferenceNumber ?? string.Empty;
			c["map_page"] = call.MapPage ?? string.Empty;
			c["priority"] = call.Priority;
			c["priority_text"] = call.GetPriorityText();
			c["is_critical"] = call.IsCritical;
			c["state"] = call.State;
			c["state_text"] = call.GetStateText();
			c["source"] = call.CallSource;
			c["external_id"] = call.ExternalIdentifier ?? string.Empty;
			c["logged_on"] = call.LoggedOn;
			c["closed_on"] = call.ClosedOn;
			c["completed_notes"] = call.CompletedNotes ?? string.Empty;
			c["contact_name"] = call.ContactName ?? string.Empty;
			c["contact_number"] = call.ContactNumber ?? string.Empty;
			c["w3w"] = call.W3W ?? string.Empty;
			c["dispatch_count"] = call.DispatchCount;
			c["dispatch_on"] = call.DispatchOn;
			c["form_data"] = call.CallFormData ?? string.Empty;
			c["is_deleted"] = call.IsDeleted;
			c["deleted_reason"] = call.DeletedReason ?? string.Empty;
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
				s["note"] = status.Note ?? string.Empty;
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
				s["geo_location"] = status.GeoLocationData ?? string.Empty;
				s["destination_id"] = status.DestinationId;
				s["note"] = status.Note ?? string.Empty;
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
			var c = new ScriptObject();
			c["id"] = cert.PersonnelCertificationId;
			c["name"] = cert.Name ?? string.Empty;
			c["number"] = cert.Number ?? string.Empty;
			c["type"] = cert.Type ?? string.Empty;
			c["area"] = cert.Area ?? string.Empty;
			c["issued_by"] = cert.IssuedBy ?? string.Empty;
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

		private static T TryDeserialize<T>(string json) where T : class
		{
			try { return string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<T>(json); }
			catch { return null; }
		}
	}
}


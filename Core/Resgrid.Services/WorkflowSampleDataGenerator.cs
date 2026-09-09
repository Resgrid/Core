using System;
using Resgrid.Model;
using Scriban.Runtime;

namespace Resgrid.Services
{
	/// <summary>
	/// Generates realistic sample Scriban ScriptObject data for every workflow trigger event type.
	/// Used for template preview, validation, and test triggering in the UI.
	/// </summary>
	public static class WorkflowSampleDataGenerator
	{
		public static object GenerateSampleData(WorkflowTriggerEventType eventType)
		{
			var obj = new ScriptObject();
			AddSampleDepartment(obj);
			AddSampleTimestamp(obj);
			AddSampleUser(obj);
			AddEventSpecificSamples(obj, eventType);
			return obj;
		}

		private static void AddSampleDepartment(ScriptObject obj)
		{
			var d = new ScriptObject();
			d["id"] = 1;
			d["name"] = "Sample Fire Department";
			d["code"] = "SFD1";
			d["type"] = "Fire";
			d["time_zone"] = "Eastern Standard Time";
			d["use_24_hour_time"] = false;
			d["created_on"] = new DateTime(2020, 1, 1);
			d["phone_number"] = "555-867-5309";
			d["display_name"] = "Sample Fire Department";
			d["logo_url"] = "https://app.resgrid.com/User/Department/PublicMasthead?key=sample";
			d["website"] = "https://www.samplefire.example";

			var addr = new ScriptObject();
			addr["street"] = "100 Main Street";
			addr["city"] = "Springfield";
			addr["state"] = "IL";
			addr["postal_code"] = "62701";
			addr["country"] = "US";
			addr["full"] = "100 Main Street Springfield IL 62701 US";
			d["address"] = addr;
			obj["department"] = d;
		}

		private static void AddSampleTimestamp(ScriptObject obj)
		{
			var now = DateTime.Now;
			var ts = new ScriptObject();
			ts["utc_now"] = DateTime.UtcNow;
			ts["department_now"] = now;
			ts["date"] = now.ToString("yyyy-MM-dd");
			ts["time"] = now.ToString("HH:mm:ss");
			ts["day_of_week"] = now.DayOfWeek.ToString();
			obj["timestamp"] = ts;
		}

		private static void AddSampleUser(ScriptObject obj)
		{
			var u = new ScriptObject();
			u["id"] = "00000000-0000-0000-0000-000000000001";
			u["first_name"] = "John";
			u["last_name"] = "Smith";
			u["full_name"] = "John Smith";
			u["email"] = "john.smith@samplefire.org";
			u["mobile_number"] = "555-123-4567";
			u["home_number"] = "555-987-6543";
			u["identification_number"] = "FF-0042";
			u["username"] = "jsmith";
			u["time_zone"] = "Eastern Standard Time";
			obj["user"] = u;
		}

		private static void AddEventSpecificSamples(ScriptObject obj, WorkflowTriggerEventType eventType)
		{
			switch (eventType)
			{
				case WorkflowTriggerEventType.ChecklistCompleted:
				case WorkflowTriggerEventType.ChecklistFailed:
                case WorkflowTriggerEventType.ChecklistMissed:
                case WorkflowTriggerEventType.ChecklistScheduleChanged:
                case WorkflowTriggerEventType.ChecklistOccurrenceSkipped:
					obj["protection"] = new ScriptObject { ["is_redacted"] = false, ["redacted_fields"] = new ScriptArray(), ["catalog_version"] = 0 };
					var scheduled = eventType != WorkflowTriggerEventType.ChecklistCompleted && eventType != WorkflowTriggerEventType.ChecklistFailed;
					obj["checklist"] = new ScriptObject { ["completion_id"] = "11111111-1111-1111-1111-111111111111", ["definition_id"] = "22222222-2222-2222-2222-222222222222", ["version_id"] = "33333333-3333-3333-3333-333333333333", ["schedule_id"] = scheduled ? "55555555-5555-5555-5555-555555555555" : null, ["occurrence_id"] = "66666666-6666-6666-6666-666666666666", ["period_start_utc"] = "2026-09-08T08:00:00.0000000Z", ["window_end_utc"] = "2026-09-08T09:00:00.0000000Z", ["state"] = 4, ["revision"] = 2, ["is_active"] = true, ["target_type"] = 1, ["target_id"] = "12", ["score"] = 75m, ["passed"] = false, ["item_id"] = "44444444-4444-4444-4444-444444444444", ["url"] = $"{(Resgrid.Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/')}/User/Checklists/" + (scheduled ? "Due" : "CompletionDetail/11111111-1111-1111-1111-111111111111") };
					break;
				case WorkflowTriggerEventType.CallAdded:
				case WorkflowTriggerEventType.CallUpdated:
				case WorkflowTriggerEventType.CallClosed:
					var c = new ScriptObject();
					c["id"] = 1001;
					c["number"] = "2024-001001";
					c["name"] = "Structure Fire";
					c["nature"] = "Reported structure fire with smoke visible from the street.";
					c["notes"] = "Caller reports occupants may still be inside.";
					c["address"] = "456 Oak Avenue, Springfield, IL";
					c["geo_location"] = "39.7817,-89.6501";
					c["type"] = "Structure Fire";
					c["incident_number"] = "INC-2024-001";
					c["reference_number"] = "REF-9876";
					c["map_page"] = "Grid 12-B";
					c["priority"] = 3;
					c["priority_text"] = "Emergency";
					c["is_critical"] = true;
					c["state"] = 0;
					c["state_text"] = "Active";
					c["source"] = 0;
					c["external_id"] = "EXT-001001";
					c["logged_on"] = DateTime.Now.AddMinutes(-10);
					c["closed_on"] = eventType == WorkflowTriggerEventType.CallClosed ? (object)DateTime.Now : null;
					c["completed_notes"] = eventType == WorkflowTriggerEventType.CallClosed ? "Fire extinguished, no injuries." : "";
					c["contact_name"] = "Jane Doe";
					c["contact_number"] = "555-111-2222";
					c["w3w"] = "filled.count.soap";
					c["dispatch_count"] = 3;
					c["dispatch_on"] = DateTime.Now.AddMinutes(-8);
					c["form_data"] = "{}";
					c["is_deleted"] = false;
					c["deleted_reason"] = "";
					obj["call"] = c;
					break;

				case WorkflowTriggerEventType.UnitStatusChanged:
					var us = new ScriptObject();
					us["id"] = 501;
					us["state"] = 2;
					us["state_text"] = "Committed";
					us["timestamp"] = DateTime.Now;
					us["note"] = "En route to scene";
					us["latitude"] = 39.7817m;
					us["longitude"] = -89.6501m;
					us["destination_id"] = 1001;
					obj["unit_status"] = us;
					var un = new ScriptObject();
					un["id"] = 10;
					un["name"] = "Engine 1";
					un["type"] = "Engine";
					un["vin"] = "1FTNX21L8NEA00001";
					un["plate_number"] = "IL-FIRE1";
					un["station_group_id"] = 1;
					obj["unit"] = un;
					var pus = new ScriptObject();
					pus["state"] = 0;
					pus["state_text"] = "Available";
					pus["timestamp"] = DateTime.Now.AddMinutes(-5);
					obj["previous_unit_status"] = pus;
					break;

				case WorkflowTriggerEventType.PersonnelStaffingChanged:
					var sf = new ScriptObject();
					sf["id"] = 301;
					sf["state"] = 1;
					sf["state_text"] = "Delayed";
					sf["timestamp"] = DateTime.Now;
					sf["note"] = "Running 15 minutes late";
					obj["staffing"] = sf;
					var psf = new ScriptObject();
					psf["state"] = 0;
					psf["state_text"] = "Available";
					psf["timestamp"] = DateTime.Now.AddHours(-1);
					obj["previous_staffing"] = psf;
					break;

				case WorkflowTriggerEventType.PersonnelStatusChanged:
					var st = new ScriptObject();
					st["id"] = 401;
					st["action_type"] = 1;
					st["action_text"] = "Responding";
					st["timestamp"] = DateTime.Now;
					st["geo_location"] = "39.7817,-89.6501";
					st["destination_id"] = 1001;
					st["note"] = "";
					obj["status"] = st;
					var pst = new ScriptObject();
					pst["action_type"] = 0;
					pst["action_text"] = "Standing By";
					pst["timestamp"] = DateTime.Now.AddMinutes(-2);
					obj["previous_status"] = pst;
					break;

				case WorkflowTriggerEventType.MessageSent:
					var msg = new ScriptObject();
					msg["id"] = 201;
					msg["subject"] = "Station Meeting Tonight";
					msg["body"] = "All personnel please report to the station for the monthly meeting at 1900 hours.";
					msg["is_broadcast"] = true;
					msg["sent_on"] = DateTime.Now;
					msg["type"] = 0;
					msg["recipients"] = "all";
					msg["expire_on"] = DateTime.Now.AddDays(1);
					obj["message"] = msg;
					break;

				case WorkflowTriggerEventType.TrainingAdded:
				case WorkflowTriggerEventType.TrainingUpdated:
					var tr = new ScriptObject();
					tr["id"] = 101;
					tr["name"] = "Hazmat Operations Level 1";
					tr["description"] = "Annual hazardous materials operations training for all first responders.";
					tr["training_text"] = "Complete all modules and pass the quiz with a minimum score of 80%.";
					tr["minimum_score"] = 80.0;
					tr["created_on"] = DateTime.Today;
					tr["to_be_completed_by"] = DateTime.Today.AddDays(30);
					obj["training"] = tr;
					break;

				case WorkflowTriggerEventType.InventoryAdjusted:
					var inv = new ScriptObject();
					inv["id"] = 601;
					inv["type_name"] = "SCBA Cylinder";
					inv["type_description"] = "Self-contained breathing apparatus cylinder";
					inv["unit_of_measure"] = "unit";
					inv["batch"] = "2024-BATCH-01";
					inv["note"] = "Monthly inventory check";
					inv["location"] = "Apparatus Bay A, Shelf 3";
					inv["amount"] = 12.0;
					inv["previous_amount"] = 14.0;
					inv["timestamp"] = DateTime.Now;
					inv["group_id"] = 1;
					obj["inventory"] = inv;
					break;

				case WorkflowTriggerEventType.CertificationExpiring:
					var cert = new ScriptObject();
					cert["id"] = 701;
					cert["name"] = "EMT-Basic";
					cert["number"] = "EMT-2024-0042";
					cert["type"] = "EMS";
					cert["area"] = "Emergency Medical";
					cert["issued_by"] = "State EMS Authority";
					cert["expires_on"] = DateTime.Today.AddDays(30);
					cert["received_on"] = DateTime.Today.AddYears(-2);
					cert["days_until_expiry"] = 30;
					obj["certification"] = cert;
					break;

				case WorkflowTriggerEventType.FormSubmitted:
					var fm = new ScriptObject();
					fm["id"] = "form-001";
					fm["name"] = "Daily Apparatus Check";
					fm["type"] = 0;
					fm["submitted_data"] = "{\"apparatus\":\"Engine 1\",\"mileage\":\"45231\",\"condition\":\"Good\"}";
					fm["submitted_by_user_id"] = "00000000-0000-0000-0000-000000000001";
					fm["submitted_on"] = DateTime.Now;
					obj["form"] = fm;
					break;

				case WorkflowTriggerEventType.LogAdded:
					var log = new ScriptObject();
					log["id"] = 801;
					log["narrative"] = "Responded to structure fire at 456 Oak Ave. Fire was contained to kitchen. No injuries reported.";
					log["type"] = "Fire";
					log["log_type"] = 1;
					log["external_id"] = "";
					log["initial_report"] = "Smoke detector activation";
					log["course"] = "";
					log["course_code"] = "";
					log["instructors"] = "";
					log["cause"] = "Cooking fire";
					log["contact_name"] = "Jane Doe";
					log["contact_number"] = "555-111-2222";
					log["location"] = "456 Oak Avenue";
					log["started_on"] = DateTime.Now.AddHours(-2);
					log["ended_on"] = DateTime.Now.AddHours(-1);
					log["logged_on"] = DateTime.Now;
					log["other_agencies"] = "Police";
					log["other_units"] = "Ladder 2, Rescue 1";
					log["other_personnel"] = "";
					log["call_id"] = 1001;
					obj["log"] = log;
					break;

				case WorkflowTriggerEventType.DocumentAdded:
					var doc = new ScriptObject();
					doc["id"] = 901;
					doc["name"] = "2024 Standard Operating Guidelines";
					doc["category"] = "SOG";
					doc["description"] = "Updated standard operating guidelines for 2024.";
					doc["type"] = "application/pdf";
					doc["filename"] = "SOG-2024.pdf";
					doc["admins_only"] = false;
					doc["added_on"] = DateTime.Today;
					obj["document"] = doc;
					break;

				case WorkflowTriggerEventType.NoteAdded:
					var note = new ScriptObject();
					note["id"] = 1101;
					note["title"] = "Hydrant Out of Service";
					note["body"] = "Hydrant at 123 Main St is out of service for repairs until further notice.";
					note["color"] = "#FF0000";
					note["category"] = "Operations";
					note["is_admin_only"] = false;
					note["added_on"] = DateTime.Today;
					note["expires_on"] = DateTime.Today.AddDays(14);
					obj["note"] = note;
					break;

				case WorkflowTriggerEventType.CalendarEventAdded:
				case WorkflowTriggerEventType.CalendarEventUpdated:
					var cal = new ScriptObject();
					cal["id"] = 1201;
					cal["title"] = "Monthly Drill Night";
					cal["description"] = "Monthly training drill - all hands required.";
					cal["location"] = "Station 1";
					cal["start"] = DateTime.Today.AddDays(7).AddHours(19);
					cal["end"] = DateTime.Today.AddDays(7).AddHours(21);
					cal["is_all_day"] = false;
					cal["item_type"] = 0;
					cal["signup_type"] = 1;
					cal["is_public"] = false;
					obj["calendar"] = cal;
					break;

				case WorkflowTriggerEventType.ShiftCreated:
				case WorkflowTriggerEventType.ShiftUpdated:
					var shift = new ScriptObject();
					shift["id"] = 1301;
					shift["name"] = "A Shift";
					shift["code"] = "A";
					shift["schedule_type"] = 0;
					shift["assignment_type"] = 0;
					shift["color"] = "#0000FF";
					shift["start_day"] = DateTime.Today;
					shift["start_time"] = "07:00 AM";
					shift["end_time"] = "07:00 AM";
					shift["hours"] = 24;
					shift["department_number"] = "SFD1";
					obj["shift"] = shift;
					break;

				case WorkflowTriggerEventType.GroupAdded:
				case WorkflowTriggerEventType.GroupUpdated:
					var grp = new ScriptObject();
					grp["id"] = 1;
					grp["name"] = "Station 1";
					grp["type"] = 1;
					grp["dispatch_email"] = "station1@samplefire.org";
					grp["message_email"] = "station1-msg@samplefire.org";
					grp["latitude"] = "39.7817";
					grp["longitude"] = "-89.6501";
					grp["what3words"] = "filled.count.soap";
					var ga = new ScriptObject();
					ga["street"] = "100 Main Street";
					ga["city"] = "Springfield";
					ga["state"] = "IL";
					ga["postal_code"] = "62701";
					ga["country"] = "US";
					grp["address"] = ga;
					obj["group"] = grp;
					break;

				case WorkflowTriggerEventType.PersonnelRoleChanged:
					var rc = new ScriptObject();
					rc["user_id"] = "00000000-0000-0000-0000-000000000001";
					rc["role_id"] = 5;
					rc["role_name"] = "Driver/Operator";
					rc["role_description"] = "Certified apparatus driver and pump operator.";
					rc["action"] = "Added";
					obj["role_change"] = rc;
					break;

				case WorkflowTriggerEventType.UnitAdded:
					var ua = new ScriptObject();
					ua["id"] = 10;
					ua["name"] = "Engine 3";
					ua["type"] = "Engine";
					ua["vin"] = "1FTNX21L8NEA00003";
					ua["plate_number"] = "IL-FIRE3";
					ua["station_group_id"] = 1;
					ua["four_wheel"] = false;
					ua["special_permit"] = false;
					obj["unit"] = ua;
					break;

				case WorkflowTriggerEventType.UserCreated:
					var nu = new ScriptObject();
					nu["id"] = "00000000-0000-0000-0000-000000000002";
					nu["username"] = "ajenkins";
					nu["email"] = "a.jenkins@samplefire.org";
					nu["name"] = "Alex Jenkins";
					obj["new_user"] = nu;
					break;

				case WorkflowTriggerEventType.UserAssignedToGroup:
					var aug = new ScriptObject();
					aug["id"] = "00000000-0000-0000-0000-000000000001";
					aug["name"] = "John Smith";
					obj["assigned_user"] = aug;
					var aggp = new ScriptObject();
					aggp["id"] = 2;
					aggp["name"] = "Station 2";
					aggp["type"] = 1;
					aggp["dispatch_email"] = "station2@samplefire.org";
					obj["group"] = aggp;
					var apg = new ScriptObject();
					apg["id"] = 1;
					apg["name"] = "Station 1";
					obj["previous_group"] = apg;
					break;

				case WorkflowTriggerEventType.ResourceOrderAdded:
					var ro = new ScriptObject();
					ro["id"] = 1401;
					ro["title"] = "Type 3 Engine Request";
					ro["incident_number"] = "INC-2024-WILDFIRE";
					ro["incident_name"] = "Oak Creek Wildfire";
					ro["incident_address"] = "Forest Road 42, Springfield County";
					ro["summary"] = "Requesting mutual aid Type 3 engine for wildfire suppression.";
					ro["open_date"] = DateTime.Today;
					ro["needed_by"] = DateTime.Today.AddDays(1);
					ro["contact_name"] = "IC Jones";
					ro["contact_number"] = "555-999-8888";
					ro["special_instructions"] = "Crew must have red card certification.";
					ro["meetup_location"] = "Fire Camp, Hwy 66 Mile Marker 22";
					ro["financial_code"] = "FIN-2024-WF";
					obj["order"] = ro;
					break;

				case WorkflowTriggerEventType.ShiftTradeRequested:
				case WorkflowTriggerEventType.ShiftTradeFilled:
					var stt = new ScriptObject();
					stt["id"] = 1501;
					stt["filled_by_user_id"] = eventType == WorkflowTriggerEventType.ShiftTradeFilled ? "00000000-0000-0000-0000-000000000002" : "";
					stt["department_number"] = "SFD1";
					obj["shift_trade"] = stt;
					break;

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
					var incident = new ScriptObject();
					incident["command_id"] = "f0d7c9bd-c692-4c63-b714-111111111111";
					incident["call_id"] = 1001;
					incident["department_id"] = 1;
					incident["user_id"] = "00000000-0000-0000-0000-000000000001";
					incident["name"] = "Oak Creek Wildfire";
					incident["visibility"] = eventType == WorkflowTriggerEventType.PublicIncidentNoteAdded || eventType == WorkflowTriggerEventType.PublicIncidentDocumentAdded ? 1 : 0;
					incident["note_id"] = "98e7cbdd-b3fa-4725-8332-222222222222";
					incident["note_type"] = (int)IncidentNoteType.Containment;
					incident["title"] = "Containment update";
					incident["body"] = "Fire is 40% contained; forward progress has stopped on the east flank.";
					incident["containment_percent"] = 40m;
					incident["attachment_id"] = "35990439-23a1-43b5-a2dd-333333333333";
					incident["file_name"] = "public-situation-map.pdf";
					incident["content_type"] = "application/pdf";
					incident["content_length"] = 284123L;
					incident["sha256_hash"] = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
					incident["description"] = "Current public situation map";
					incident["action_plan"] = "Protect life, hold the east flank, and maintain evacuation routes.";
					incident["latitude"] = "39.7817";
					incident["longitude"] = "-89.6501";
					incident["enabled"] = eventType == WorkflowTriggerEventType.IncidentPublicSharingEnabled;
					obj["incident"] = incident;
					break;

				case WorkflowTriggerEventType.RunCardActivated:
					var runCard = new ScriptObject();
					runCard["call_id"] = 1001;
					runCard["run_card_id"] = 7;
					runCard["run_card_name"] = "Structure Fire - First Alarm";
					runCard["alarm_level"] = 1;
					runCard["mode"] = 1;
					runCard["was_auto_dispatched"] = true;
					runCard["unit_count"] = 3;
					runCard["personnel_count"] = 6;
					obj["run_card"] = runCard;
					break;

				case WorkflowTriggerEventType.CallAlarmEscalated:
					var escalation = new ScriptObject();
					escalation["call_id"] = 1001;
					escalation["previous_alarm_level"] = 1;
					escalation["new_alarm_level"] = 2;
					escalation["added_unit_count"] = 2;
					escalation["added_personnel_count"] = 4;
					obj["escalation"] = escalation;
					break;

				case WorkflowTriggerEventType.DispatchShortfallDetected:
					var shortfall = new ScriptObject();
					shortfall["call_id"] = 1001;
					shortfall["run_card_id"] = 7;
					shortfall["alarm_level"] = 1;
					shortfall["shortfall_count"] = 1;
					shortfall["summary"] = "Unit type Ladder: 0/1";
					obj["shortfall"] = shortfall;
					break;

				case WorkflowTriggerEventType.StationCoverageGapDetected:
					var coverageGap = new ScriptObject();
					coverageGap["call_id"] = 1001;
					coverageGap["gap_count"] = 1;
					coverageGap["summary"] = "Station 1: 0/1 Engine";
					obj["coverage_gap"] = coverageGap;
					break;

				case WorkflowTriggerEventType.RecordCreated:
				case WorkflowTriggerEventType.RecordSubmittedForReview:
				case WorkflowTriggerEventType.RecordReturnedForCorrection:
				case WorkflowTriggerEventType.RecordFinalized:
				case WorkflowTriggerEventType.RecordAmended:
				case WorkflowTriggerEventType.RecordVoided:
				case WorkflowTriggerEventType.RecordCancelled:
				// The submission and obligation triggers build their own blocks inside AddRecordsSamples; without
				// these cases the template preview advertises submission.* and obligation.* variables that never
				// render.
				case WorkflowTriggerEventType.RecordSubmissionQueued:
				case WorkflowTriggerEventType.RecordSubmissionAccepted:
				case WorkflowTriggerEventType.RecordSubmissionRejected:
				case WorkflowTriggerEventType.RecordSubmissionFailed:
				case WorkflowTriggerEventType.RecordOverdue:
				case WorkflowTriggerEventType.RecordApproved:
				case WorkflowTriggerEventType.RecordAttachmentAdded:
				case WorkflowTriggerEventType.RecordDisclosureRequested:
				case WorkflowTriggerEventType.RecordDisclosureProduced:
				case WorkflowTriggerEventType.RecordDisclosureReleased:
				case WorkflowTriggerEventType.RecordDisclosureClosed:
				case WorkflowTriggerEventType.RecordLegalHoldPlaced:
				case WorkflowTriggerEventType.RecordLegalHoldReleased:
				case WorkflowTriggerEventType.RecordEvidenceCaptured:
				case WorkflowTriggerEventType.RecordPurged:
				case WorkflowTriggerEventType.RecordExportScheduled:
				case WorkflowTriggerEventType.RecordInspectionCompleted:
				case WorkflowTriggerEventType.RecordViolationOverdue:
				case WorkflowTriggerEventType.RecordPermitExpiring:
				case WorkflowTriggerEventType.RecordDefinitionPublished:
				case WorkflowTriggerEventType.RecordDefinitionRetired:
					AddRecordsSamples(obj, eventType);
					break;
			}
		}

		/// <summary>
		/// Records (RMS) triggers 100-112: a bounded snapshot matching RecordEventVariables, RecordVariables and
		/// RecordChangeVariables in WorkflowTemplateVariableCatalog. The state pair follows the trigger.
		/// </summary>
		private static void AddRecordsSamples(ScriptObject obj, WorkflowTriggerEventType eventType)
		{
			var previousState = "Draft";
			var currentState = "Draft";
			var reasonCode = "";
			var recordNumber = "";
			var revisionNumber = 0;
			string revisionId = null;
			string priorRevisionId = null;
			DateTime? finalizedOn = null;

			switch (eventType)
			{
				case WorkflowTriggerEventType.RecordSubmittedForReview:
					currentState = "ReadyForReview";
					break;
				case WorkflowTriggerEventType.RecordReturnedForCorrection:
					previousState = "ReadyForReview";
					currentState = "Returned";
					reasonCode = "incomplete";
					break;
				case WorkflowTriggerEventType.RecordFinalized:
					currentState = "Finalized";
					recordNumber = "TRN-2026-0042";
					revisionNumber = 1;
					revisionId = "0f3c1d2e-5b6a-4c7d-8e9f-0a1b2c3d4e5f";
					finalizedOn = DateTime.Now;
					break;
				case WorkflowTriggerEventType.RecordAmended:
					previousState = "Finalized";
					currentState = "Amended";
					recordNumber = "TRN-2026-0042";
					revisionNumber = 2;
					priorRevisionId = "0f3c1d2e-5b6a-4c7d-8e9f-0a1b2c3d4e5f";
					revisionId = "1a2b3c4d-6e7f-4a8b-9c0d-1e2f3a4b5c6d";
					reasonCode = "correction";
					finalizedOn = DateTime.Now.AddDays(-1);
					break;
				case WorkflowTriggerEventType.RecordVoided:
					previousState = "Finalized";
					currentState = "Voided";
					recordNumber = "TRN-2026-0042";
					revisionNumber = 2;
					priorRevisionId = "0f3c1d2e-5b6a-4c7d-8e9f-0a1b2c3d4e5f";
					revisionId = "2b3c4d5e-7f8a-4b9c-0d1e-2f3a4b5c6d7e";
					reasonCode = "duplicate";
					finalizedOn = DateTime.Now.AddDays(-1);
					break;
				case WorkflowTriggerEventType.RecordCancelled:
					currentState = "Cancelled";
					break;
				case WorkflowTriggerEventType.RecordApproved:
					previousState = "ReadyForReview";
					currentState = "Approved";
					break;
				case WorkflowTriggerEventType.RecordAttachmentAdded:
				case WorkflowTriggerEventType.RecordEvidenceCaptured:
				case WorkflowTriggerEventType.RecordLegalHoldPlaced:
				case WorkflowTriggerEventType.RecordLegalHoldReleased:
					recordNumber = "TRN-2026-0042";
					revisionNumber = 1;
					revisionId = "0f3c1d2e-5b6a-4c7d-8e9f-0a1b2c3d4e5f";
					currentState = "Finalized";
					previousState = "Finalized";
					finalizedOn = DateTime.Now.AddDays(-1);
					break;
				case WorkflowTriggerEventType.RecordPurged:
					previousState = "Finalized";
					currentState = "Purged";
					break;
			}

			var e = new ScriptObject();
			e["id"] = "5f1c0f0e-9a2b-4c3d-8e4f-6a7b8c9d0e1f";
			e["name"] = eventType.ToString();
			e["schema_version"] = 1;
			e["occurred_on"] = DateTime.Now;
			e["correlation_id"] = "9d8c7b6a-5f4e-4d3c-b2a1-0f9e8d7c6b5a";
			e["causation_id"] = "";
			e["sequence"] = revisionNumber + 1;
			e["is_replay"] = false;
			e["origin_client"] = "Web";
			obj["event"] = e;

			var r = new ScriptObject();
			r["id"] = "9d8c7b6a-5f4e-4d3c-b2a1-0f9e8d7c6b5a";
			r["kind"] = "Operational";
			r["record_number"] = recordNumber;
			r["draft_reference"] = "D-7Q2MX";
			r["definition_key"] = "system.training";
			r["definition_version"] = 1;
			r["type_key"] = "Training";
			r["state"] = currentState;
			r["lifecycle_preset"] = "QuickEntry";
			r["department_id"] = 1;
			r["station_group_id"] = 12;
			r["call_id"] = 1001;
			r["external_id"] = "";
			r["author_user_id"] = "00000000-0000-0000-0000-000000000001";
			r["owner_user_id"] = "00000000-0000-0000-0000-000000000001";
			r["started_on"] = DateTime.Now.AddHours(-3);
			r["ended_on"] = DateTime.Now.AddHours(-1);
			r["created_on"] = DateTime.Now.AddHours(-1);
			r["finalized_on"] = finalizedOn;
			r["revision_id"] = revisionId ?? "";
			r["revision_number"] = revisionNumber;
			r["checksum"] = revisionId == null ? "" : "3a7bd3e2360a3d29eea436fcfb7e44c735d117c42d1c1835420b6b9942dd4f1b";
			r["summary"] = "Pump Operations — Hose evolutions";
			r["url"] = "https://resgrid.local/User/Records/Details/9d8c7b6a-5f4e-4d3c-b2a1-0f9e8d7c6b5a";
			obj["record"] = r;

			var c = new ScriptObject();
			c["previous_state"] = previousState;
			c["current_state"] = currentState;
			c["prior_revision_id"] = priorRevisionId ?? "";
			c["current_revision_id"] = revisionId ?? "";
			c["reason_code"] = reasonCode;
			if (eventType == WorkflowTriggerEventType.RecordCancelled)
				c["number_disposition"] = "none";
			obj["record_change"] = c;

			if (eventType >= WorkflowTriggerEventType.RecordSubmissionQueued && eventType <= WorkflowTriggerEventType.RecordSubmissionFailed)
			{
				r["kind"] = "IncidentReport";
				r["incident_number"] = "2026-000123";
				r["neris_incident_id"] = eventType == WorkflowTriggerEventType.RecordSubmissionQueued ? "" : "FD24027000I2026000123";
				var s = new ScriptObject();
				s["id"] = "7c1e2a9b-3d4f-4e5a-8b6c-1d2e3f4a5b6c";
				s["destination"] = "NERIS";
				s["destination_version"] = "1.4.78";
				s["state"] = eventType == WorkflowTriggerEventType.RecordSubmissionQueued ? "Queued" : eventType == WorkflowTriggerEventType.RecordSubmissionAccepted ? "Accepted" : eventType == WorkflowTriggerEventType.RecordSubmissionRejected ? "Rejected" : "Failed";
				s["external_id"] = eventType == WorkflowTriggerEventType.RecordSubmissionQueued ? "" : "FD24027000I2026000123";
				s["external_status"] = eventType == WorkflowTriggerEventType.RecordSubmissionAccepted ? "APPROVED" : eventType == WorkflowTriggerEventType.RecordSubmissionRejected ? "REJECTED" : "";
				s["attempts"] = eventType == WorkflowTriggerEventType.RecordSubmissionFailed ? 5 : 1;
				s["max_attempts"] = 5;
				s["error_summary"] = eventType == WorkflowTriggerEventType.RecordSubmissionRejected ? "dispatch.call_answered (missing)" : eventType == WorkflowTriggerEventType.RecordSubmissionFailed ? "Delivery exhausted its retries: NERIS returned 503." : "";
				s["queued_on"] = DateTime.Now.AddMinutes(-30);
				s["sent_on"] = DateTime.Now.AddMinutes(-1);
				s["completed_on"] = eventType == WorkflowTriggerEventType.RecordSubmissionQueued ? (DateTime?)null : DateTime.Now;
				obj["submission"] = s;
			}

			if (eventType == WorkflowTriggerEventType.RecordOverdue)
			{
				r["kind"] = "Operational";
				var o = new ScriptObject();
				o["type"] = "Review";
				o["due_on"] = DateTime.Now.AddHours(-30);
				o["overdue_hours"] = 30;
				o["responsible_user_id"] = "00000000-0000-0000-0000-000000000000";
				o["overdue_count"] = 1;
				obj["obligation"] = o;
			}

			var protection = new ScriptObject();
			protection["is_protected"] = false;
			protection["is_redacted"] = false;
			protection["redacted_fields"] = new ScriptArray();
			protection["protected_catalog_version"] = 0;
			obj["protection"] = protection;

			// definition.* and fields.* (RMS-1B): shown for every Records trigger so a department-definition template previews.
			var isDefinitionTrigger = eventType == WorkflowTriggerEventType.RecordDefinitionPublished || eventType == WorkflowTriggerEventType.RecordDefinitionRetired;
			var definition = new ScriptObject();
			definition["id"] = "2b3c4d5e-6f70-4a81-9b92-a3b4c5d6e7f8";
			definition["key"] = "security-patrol";
			definition["name"] = "Security Patrol Log";
			definition["category"] = "Security";
			definition["owner"] = "Department";
			definition["version"] = 2;
			definition["previous_version"] = eventType == WorkflowTriggerEventType.RecordDefinitionPublished ? 1 : (int?)null;
			definition["state"] = eventType == WorkflowTriggerEventType.RecordDefinitionRetired ? "Retired" : "Published";
			definition["lifecycle_preset"] = "QuickEntry";
			definition["template_key"] = "template.security-patrol";
			definition["jurisdiction_profile_key"] = "us";
			definition["minimum_client_capability"] = "records.v1b";
			definition["schema_checksum"] = "9f2c1e0d8b7a6f5e4d3c2b1a0f9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4b3a2f1e";
			definition["published_on"] = DateTime.Now.AddDays(-7);
			definition["retired"] = eventType == WorkflowTriggerEventType.RecordDefinitionRetired;
			definition["reason"] = eventType == WorkflowTriggerEventType.RecordDefinitionRetired ? "Replaced by security-patrol-v2" : "";
			definition["exposed_field_keys"] = new ScriptArray { "client_site", "officer", "exception_reported" };
			definition["section_keys"] = new ScriptArray { "assignment", "checkpoints", "observations", "exceptions", "handoff" };
			obj["definition"] = definition;
			if (!isDefinitionTrigger)
			{
				var fields = new ScriptObject();
				fields["client_site"] = "Harbor Logistics — Pier 4";
				fields["officer"] = "J. Alvarez";
				fields["exception_reported"] = true;
				var checkpoint = new ScriptObject();
				checkpoint["checkpoint"] = "Gate B";
				checkpoint["status"] = "Exception";
				fields["checkpoints"] = new ScriptArray { checkpoint };
				fields["checkpoints_count"] = 1;
				obj["fields"] = fields;
			}

			if (eventType == WorkflowTriggerEventType.RecordApproved)
			{
				var approval = new ScriptObject();
				approval["reviewer_user_id"] = "00000000-0000-0000-0000-000000000002";
				approval["approver_user_id"] = "00000000-0000-0000-0000-000000000002";
				approval["approved_on"] = DateTime.Now;
				approval["submitted_for_review_on"] = DateTime.Now.AddHours(-5);
				approval["review_due_on"] = DateTime.Now.AddHours(43);
				approval["return_count"] = 0;
				obj["review"] = approval;
			}

			if (eventType == WorkflowTriggerEventType.RecordAttachmentAdded)
			{
				var a = new ScriptObject();
				a["id"] = "4e5f6a7b-8c9d-4e0f-a1b2-c3d4e5f6a7b8";
				a["content_type"] = "application/pdf";
				a["byte_size"] = 184320;
				a["checksum"] = "9b74c9897bac770ffc029102a200c5de7cbb3d8bd2e6f9a5c14f1f2f8a1d0c11";
				a["classification"] = "Unrestricted";
				a["scan_state"] = "Clean";
				a["uploaded_by_user_id"] = "00000000-0000-0000-0000-000000000001";
				a["uploaded_on"] = DateTime.Now;
				a["count"] = 2;
				obj["attachment"] = a;
			}

			if (eventType >= WorkflowTriggerEventType.RecordDisclosureRequested && eventType <= WorkflowTriggerEventType.RecordDisclosureClosed)
			{
				r["kind"] = "Disclosure";
				var produced = eventType != WorkflowTriggerEventType.RecordDisclosureRequested;
				var d = new ScriptObject();
				d["request_id"] = "6d7e8f9a-0b1c-4d2e-8f3a-4b5c6d7e8f9a";
				d["request_number"] = "PRR-2026-0007";
				d["state"] = eventType == WorkflowTriggerEventType.RecordDisclosureRequested ? "Received" : eventType == WorkflowTriggerEventType.RecordDisclosureProduced ? "Produced" : eventType == WorkflowTriggerEventType.RecordDisclosureReleased ? "Released" : "Closed";
				d["received_on"] = DateTime.Now.AddDays(-6);
				d["statutory_due_on"] = DateTime.Now.AddDays(4);
				d["jurisdiction_profile"] = "State public records act";
				d["redaction_profile"] = "Standard";
				d["assigned_to_user_id"] = "00000000-0000-0000-0000-000000000003";
				d["closed_on"] = eventType == WorkflowTriggerEventType.RecordDisclosureClosed || eventType == WorkflowTriggerEventType.RecordDisclosureReleased ? DateTime.Now : (DateTime?)null;
				d["disposition"] = eventType == WorkflowTriggerEventType.RecordDisclosureClosed ? "Withdrawn" : eventType == WorkflowTriggerEventType.RecordDisclosureReleased ? "Released" : "";
				d["production_id"] = produced ? "7e8f9a0b-1c2d-4e3f-9a4b-5c6d7e8f9a0b" : "";
				d["production_number"] = produced ? 1 : (int?)null;
				d["record_count"] = produced ? 3 : (int?)null;
				d["withheld_field_count"] = produced ? 4 : (int?)null;
				d["checksum"] = produced ? "2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae" : "";
				d["byte_size"] = produced ? 512000 : (int?)null;
				d["released_on"] = eventType == WorkflowTriggerEventType.RecordDisclosureReleased ? DateTime.Now : (DateTime?)null;
				d["released_by_user_id"] = eventType == WorkflowTriggerEventType.RecordDisclosureReleased ? "00000000-0000-0000-0000-000000000003" : "";
				d["delivery_method"] = eventType == WorkflowTriggerEventType.RecordDisclosureReleased ? "Secure email" : "";
				obj["disclosure"] = d;
			}

			if (eventType == WorkflowTriggerEventType.RecordLegalHoldPlaced || eventType == WorkflowTriggerEventType.RecordLegalHoldReleased)
			{
				r["kind"] = "Operational";
				var released = eventType == WorkflowTriggerEventType.RecordLegalHoldReleased;
				var h = new ScriptObject();
				h["id"] = "8f9a0b1c-2d3e-4f4a-8b5c-6d7e8f9a0b1c";
				h["record_id"] = "9d8c7b6a-5f4e-4d3c-b2a1-0f9e8d7c6b5a";
				h["definition_key"] = "";
				h["period_start"] = (DateTime?)null;
				h["period_end"] = (DateTime?)null;
				h["reason"] = "Litigation";
				h["placed_by_user_id"] = "00000000-0000-0000-0000-000000000003";
				h["placed_on"] = DateTime.Now.AddDays(released ? -30 : 0);
				h["released_by_user_id"] = released ? "00000000-0000-0000-0000-000000000003" : "";
				h["released_on"] = released ? DateTime.Now : (DateTime?)null;
				h["is_released"] = released;
				obj["legal_hold"] = h;
			}

			if (eventType == WorkflowTriggerEventType.RecordEvidenceCaptured)
			{
				r["kind"] = "Operational";
				var ev = new ScriptObject();
				ev["id"] = "0b1c2d3e-4f5a-4b6c-8d7e-8f9a0b1c2d3e";
				ev["record_id"] = "9d8c7b6a-5f4e-4d3c-b2a1-0f9e8d7c6b5a";
				ev["record_kind"] = "Operational";
				ev["kind"] = "RunCardActivation";
				ev["source_subsystem"] = "Dispatch";
				ev["source_entity_type"] = "RunCardActivation";
				ev["source_entity_id"] = "4471";
				ev["classification"] = "Unrestricted";
				ev["checksum"] = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
				ev["byte_size"] = 2048;
				ev["source_item_count"] = 3;
				ev["coverage_start"] = DateTime.Now.AddHours(-3);
				ev["coverage_end"] = DateTime.Now.AddHours(-1);
				ev["captured_by_user_id"] = "00000000-0000-0000-0000-000000000001";
				ev["captured_on"] = DateTime.Now;
				obj["evidence"] = ev;
			}

			if (eventType == WorkflowTriggerEventType.RecordPurged)
			{
				r["kind"] = "Operational";
				var p = new ScriptObject();
				p["purged_on"] = DateTime.Now;
				p["attachments_purged"] = 2;
				p["search_erasure_pending"] = true;
				p["reason"] = "Retention period elapsed (7 years)";
				obj["purge"] = p;
			}

			if (eventType == WorkflowTriggerEventType.RecordExportScheduled)
			{
				r["kind"] = "Export";
				var x = new ScriptObject();
				x["run_id"] = "1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e5f";
				x["template_id"] = "2d3e4f5a-6b7c-4d8e-9f0a-1b2c3d4e5f6a";
				x["template_key"] = "state-monthly-runs";
				x["template_name"] = "State monthly run report";
				x["format"] = "Csv";
				x["scope"] = "Window";
				x["window_start"] = DateTime.Now.AddMonths(-1);
				x["window_end"] = DateTime.Now;
				x["record_count"] = 127;
				x["file_name"] = "state-monthly-runs-20260901-0600.csv";
				x["content_type"] = "text/csv";
				x["byte_size"] = 48213;
				x["checksum"] = "a7ffc6f8bf1ed76651c14756a061d662f580ff4de43b49fa82d80a4b80f8434a";
				x["redacted"] = false;
				x["generated_on"] = DateTime.Now;
				x["expires_on"] = DateTime.Now.AddDays(30);
				obj["export"] = x;
			}

			if (eventType == WorkflowTriggerEventType.RecordInspectionCompleted)
			{
				r["kind"] = "Prevention";
				var i = new ScriptObject();
				i["id"] = "3e4f5a6b-7c8d-4e9f-a0b1-c2d3e4f5a6b7";
				i["number"] = "INSP-2026-0031";
				i["occupancy_id"] = "4f5a6b7c-8d9e-4f0a-b1c2-d3e4f5a6b7c8";
				i["occupancy_number"] = "OCC-2026-0012";
				i["occupancy_name"] = "Riverside Assisted Living";
				i["program_id"] = "5a6b7c8d-9e0f-4a1b-c2d3-e4f5a6b7c8d9";
				i["program_name"] = "Annual life-safety inspection";
				i["state"] = "ReinspectionRequired";
				i["result"] = "Fail";
				i["scheduled_on"] = DateTime.Now.AddDays(-3);
				i["completed_on"] = DateTime.Now;
				i["inspector_user_id"] = "00000000-0000-0000-0000-000000000003";
				i["violation_count"] = 2;
				i["critical_violation_count"] = 1;
				i["is_reinspection"] = false;
				obj["inspection"] = i;
			}

			if (eventType == WorkflowTriggerEventType.RecordViolationOverdue)
			{
				r["kind"] = "Prevention";
				var v = new ScriptObject();
				v["id"] = "6b7c8d9e-0f1a-4b2c-d3e4-f5a6b7c8d9e0";
				v["inspection_id"] = "3e4f5a6b-7c8d-4e9f-a0b1-c2d3e4f5a6b7";
				v["occupancy_id"] = "4f5a6b7c-8d9e-4f0a-b1c2-d3e4f5a6b7c8";
				v["occupancy_number"] = "OCC-2026-0012";
				v["occupancy_name"] = "Riverside Assisted Living";
				v["code_set_id"] = "7c8d9e0f-1a2b-4c3d-e4f5-a6b7c8d9e0f1";
				v["code_section_id"] = "8d9e0f1a-2b3c-4d4e-f5a6-b7c8d9e0f1a2";
				v["code_section_number"] = "IFC 1031.2";
				v["severity"] = "Serious";
				v["state"] = "Open";
				v["due_on"] = DateTime.Now.AddDays(-4);
				v["days_overdue"] = 4;
				obj["violation"] = v;
			}

			if (eventType == WorkflowTriggerEventType.RecordPermitExpiring)
			{
				r["kind"] = "Prevention";
				var p = new ScriptObject();
				p["id"] = "9e0f1a2b-3c4d-4e5f-a6b7-c8d9e0f1a2b3";
				p["number"] = "PRM-2026-0107";
				p["type_id"] = "0f1a2b3c-4d5e-4f6a-b7c8-d9e0f1a2b3c4";
				p["type_name"] = "Hot work";
				p["type_code"] = "HW";
				p["occupancy_id"] = "4f5a6b7c-8d9e-4f0a-b1c2-d3e4f5a6b7c8";
				p["occupancy_number"] = "OCC-2026-0012";
				p["occupancy_name"] = "Riverside Assisted Living";
				p["state"] = "Issued";
				p["issued_on"] = DateTime.Now.AddDays(-335);
				p["effective_on"] = DateTime.Now.AddDays(-335);
				p["expires_on"] = DateTime.Now.AddDays(30);
				p["days_until_expiry"] = 30;
				p["fee_paid"] = true;
				obj["permit"] = p;
			}

			if (eventType == WorkflowTriggerEventType.RecordSubmittedForReview || eventType == WorkflowTriggerEventType.RecordReturnedForCorrection)
			{
				var returned = eventType == WorkflowTriggerEventType.RecordReturnedForCorrection;
				var review = new ScriptObject();
				review["reviewer_user_id"] = returned ? "00000000-0000-0000-0000-000000000002" : "";
				review["submitted_for_review_on"] = DateTime.Now.AddHours(-2);
				review["review_due_on"] = DateTime.Now.AddHours(46);
				review["returned_on"] = returned ? DateTime.Now : (DateTime?)null;
				review["return_count"] = returned ? 1 : 0;
				review["reason_code"] = returned ? "incomplete" : "";
				review["reason_text"] = returned ? "Unit times are missing for Engine 1." : "";
				obj["review"] = review;
			}
		}
	}
}

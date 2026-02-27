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
			}
		}
	}
}


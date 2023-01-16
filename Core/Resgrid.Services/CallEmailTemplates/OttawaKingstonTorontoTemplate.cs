using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;

namespace Resgrid.Services.CallEmailTemplates
{
	public class OttawaKingstonTorontoTemplate : ICallEmailTemplate
	{
		/*
		 *  004001 14:42:09 18-06-22 POCSAG-1  ALPHA   512  LT1 & FOY- MEDICAL ASSIST (555 Main St) UNC. MALE POST SEIZURE
			0131049 14:42:11 18-06-22 POCSAG-1  ALPHA   512  LT1 & FOY- MEDICAL ASSIST (555 Main St) UNC. MALE POST SEIZURE
			0004001 14:42:18 18-06-22 POCSAG-1  ALPHA   512  LT1 & FOY- MEDICAL ASSIST (555 Main St) UNC. MALE POST SEIZURE
			0001001 14:42:20 18-06-22 POCSAG-1  ALPHA   512  LT1 & FOY- MEDICAL ASSIST (555 Main St) UNC. MALE POST SEIZURE

			0004002 15:10:29 18-06-22 POCSAG-2  ALPHA   512  LT1 & FOY- STAND DOWN TO MEDICAL ASSIST AT LA RUE MILLS RD
			0001002 15:10:31 18-06-22 POCSAG-2  ALPHA   512  LT1 & FOY- STAND DOWN TO MEDICAL ASSIST AT LA RUE MILLS RD

			0004001 15:49:06 18-06-22 POCSAG-1  ALPHA   512  LT1,4- MEDICAL ASSIST (555 Main St) 56 Y/O FEMALE UNC POST SEIZURE

			0006001 18:26:21 18-06-22 POCSAG-1  ALPHA   512  ATH- TRUCK FIRE (555 Main St) CLOSE TO SHOP

			 WHAT WE HOPE IT WILL LOOK LIKE WHEN ON SYSTEM I CAN CHANGE THESE.

			 ATH- TRUCK FIRE (555 Main St) CLOSE TO SHOP. CS COUNTY RD 42
			 ATH - STAND DOWN TO WILTSETOWN RD
			EK 1,2 - 2 VEHICLE MVC. 2 PTS. 1 PT EJECTED FROM VEHICLE. (555 Main St)

			EK 3 - BURN COMPLAINT (555 Main St) CS KITLEY LINE 9.

			EK 3 - STAND DOWN TO MULVAUGH RD

		*/
		public async Task<Call> GenerateCall(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls, List<Unit> units,
			int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes, IGeoLocationProvider geolocationProvider)
		{
			if (email == null)
				return null;

			if (String.IsNullOrEmpty(email.TextBody))
				return null;

			if (String.IsNullOrEmpty(email.Subject))
				return null;

			string[] sections = email.TextBody.Split(new[] {"  ALPHA   512  "}, StringSplitOptions.None);
			string[] sectionOneParts = sections[0].Split(new[] {" "}, StringSplitOptions.None);

			Call c = new Call();
			c.Notes = email.TextBody;
			c.Name = sections[1].Trim();
			c.LoggedOn = DateTime.UtcNow;
			c.Priority = priority;
			c.ReportingUserId = managingUser;
			c.Dispatches = new Collection<CallDispatch>();
			c.CallSource = (int)CallSources.EmailImport;
			c.SourceIdentifier = email.MessageId;
			c.NatureOfCall = sections[1].Trim();
			c.IncidentNumber = sectionOneParts[0].Trim();
			c.ExternalIdentifier = sectionOneParts[0].Trim();

			if (users != null && users.Any())
			{
				foreach (var u in users)
				{
					CallDispatch cd = new CallDispatch();
					cd.UserId = u.UserId;

					c.Dispatches.Add(cd);
				}
			}

			// Search for an active call
			if (activeCalls != null && activeCalls.Any())
			{
				var activeCall = activeCalls.FirstOrDefault(x => x.IncidentNumber == c.IncidentNumber);

				if (activeCall != null)
				{
					activeCall.Notes = c.Notes;
					activeCall.LastDispatchedOn = DateTime.UtcNow;

					return activeCall;
				}
			}

			return c;
		}
	}
}

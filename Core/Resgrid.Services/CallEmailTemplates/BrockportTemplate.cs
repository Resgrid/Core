using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Resgrid.Model.Identity;
using Resgrid.Model;
using Resgrid.Model.Providers;
using System.Threading.Tasks;

namespace Resgrid.Services.CallEmailTemplates
{
	/// <summary>
	/// BROF  B:4911 4  L:BEADLE RD/LAKE RD ,SWE - ON LAKE RD T:29B1   X:VEH ON SIDE IN DITCH. BLUE OR BLACK CHEVY PERSON IN VEC. 
	/// </summary>
	public class BrockportTemplate : ICallEmailTemplate
	{
		public async Task<Call> GenerateCall(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls, List<Unit> units,
			int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes, IGeoLocationProvider geolocationProvider)
		{
			if (email == null)
				return null;

			if (String.IsNullOrEmpty(email.Body) && String.IsNullOrWhiteSpace(email.TextBody))
				return null;

			string body = String.Empty;
			if (!String.IsNullOrWhiteSpace(email.TextBody))
				body = email.TextBody;
			else
				body = email.Body;

			body = body.Replace("-- Confidentiality Notice --", "");
			body =
				body.Replace(
					"This email message, including all the attachments, is for the sole use of the intended recipient(s) and contains confidential information. Unauthorized use or disclosure is prohibited.",
					"");
			body =
				body.Replace(
					"If you are not the intended recipient, you may not use, disclose, copy or disseminate this information.",
					"");
			body =
				body.Replace(
					"If you are not the intended recipient, please contact the sender immediately by reply email and destroy all copies of the original message,",
					"");
			body =
				body.Replace(
					"including attachments.",
					"");
			body = body.Trim();

			var c = new Call();

			c.Notes = email.TextBody;

			if (String.IsNullOrWhiteSpace(c.Notes))
				c.Notes = email.Body;

			string[] data = body.Split(char.Parse(":"));
			c.IncidentNumber = data[1].Replace(" L", "").Trim();
			c.Type = data[3].Replace(" X", "").Trim().Replace("/", "");
			c.Address = data[2].Replace(" T", "").Trim().Replace("/", "");
			c.NatureOfCall = data[4].Trim();
			c.Name = c.Type + " " + c.NatureOfCall;
			c.LoggedOn = DateTime.UtcNow;
			c.Priority = priority;
			c.ReportingUserId = managingUser;
			c.Dispatches = new Collection<CallDispatch>();
			c.CallSource = (int) CallSources.EmailImport;
			c.SourceIdentifier = email.MessageId;

			foreach (var u in users)
			{
				var cd = new CallDispatch();
				cd.UserId = u.UserId;

				c.Dispatches.Add(cd);
			}

			return c;
		}
	}
}

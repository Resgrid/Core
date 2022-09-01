using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;

namespace Resgrid.Services.CallEmailTemplates
{
	public class ResgridEmailTemplate : ICallEmailTemplate
	{
		public async Task<Call> GenerateCall(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls,
			List<Unit> units, int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes, IGeoLocationProvider geolocationProvider)
		{
			//ID | TYPE | PRIORITY | ADDRESS | MAPPAGE | NATURE | NOTES

			if (email == null)
				return null;

			if (String.IsNullOrEmpty(email.Body))
				return null;

			Call c = new Call();
			c.Notes = email.Body;

			string[] data = email.Body.Split(char.Parse("|"));

			if (data.Any() && data.Length >= 5)
			{
				if (!string.IsNullOrEmpty(data[0]))
					c.IncidentNumber = data[0].Trim();

				if (!string.IsNullOrEmpty(data[1]))
					c.Type = data[1].Trim();

				if (string.IsNullOrEmpty(data[2]))
				{
					int prio;
					if (int.TryParse(data[2], out prio))
					{
						c.Priority = prio;
					}
					else
					{
						c.Priority = priority;
					}
				}
				else
				{
					c.Priority = priority;
				}

				if (!string.IsNullOrEmpty(data[4]))
					c.MapPage = data[4];

				c.NatureOfCall = data[5];
				c.Address = data[3];

				StringBuilder title = new StringBuilder();

				title.Append("Email Call ");
				title.Append(((CallPriority)c.Priority).ToString());
				title.Append(" ");

				if (!string.IsNullOrEmpty(c.Type))
				{
					title.Append(c.Type);
					title.Append(" ");
				}

				if (!string.IsNullOrEmpty(c.IncidentNumber))
				{
					title.Append(c.IncidentNumber);
					title.Append(" ");
				}

				c.Name = title.ToString();
			}
			else
			{
				c.Name = email.Subject;
				c.NatureOfCall = email.Body;
				c.Notes = "WARNING: FALLBACK RESGRID EMAIL IMPORT! THIS EMAIL MAY NOT BE THE CORRECT FORMAT FOR THE RESGRID EMAIL TEMPLATE. CONTACT SUPPORT IF THE EMAIL AND TEMPLATE ARE CORRECT." + email.Body;
			}

			c.LoggedOn = DateTime.UtcNow;
			c.ReportingUserId = managingUser;
			c.Dispatches = new Collection<CallDispatch>();
			c.CallSource = (int)CallSources.EmailImport;
			c.SourceIdentifier = email.MessageId;

			foreach (var u in users)
			{
				CallDispatch cd = new CallDispatch();
				cd.UserId = u.UserId;

				c.Dispatches.Add(cd);
			}

			return c;
		}
	}
}

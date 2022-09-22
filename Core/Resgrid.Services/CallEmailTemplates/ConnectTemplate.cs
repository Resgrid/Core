using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Resgrid.Model.Identity;
using Resgrid.Model;
using Resgrid.Model.Providers;
using System.Threading.Tasks;

namespace Resgrid.Services.CallEmailTemplates
{
	public class ConnectTemplate : ICallEmailTemplate
	{
		public async Task<Call> GenerateCall(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls, List<Unit> units,
			int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes, IGeoLocationProvider geolocationProvider)
		{
			if (email == null)
				return null;

			if (String.IsNullOrEmpty(email.Body))
				return null;

			Call c = new Call();
			c.Notes = email.Body;
			c.Name = email.Subject;

			int nonEmptyLineCount = 0;
			string[] rawData = email.Body.Split(new string[] { "\r\n", "\r\n\r\n" }, StringSplitOptions.None);

			foreach (var line in rawData)
			{
				if (line != null && !String.IsNullOrWhiteSpace(line))
				{
					nonEmptyLineCount++;

					switch (nonEmptyLineCount)
					{
						case 1:
							//c.Name = line.Trim();
							c.NatureOfCall = line.Trim();
							break;
						case 2: // Address
							//c.Name = $"{c.Name} {line}";
							c.NatureOfCall = c.NatureOfCall + " " + line;
							break;
						case 3: // Map and Radio Channel
							break;
						case 4: // Cross Street
							break;
						case 5: // Cross Street 2
							break;

					}
				}
			}

			c.LoggedOn = DateTime.UtcNow;
			c.Priority = priority;
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

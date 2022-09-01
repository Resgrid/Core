using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;

namespace Resgrid.Services.CallEmailTemplates
{
	public class FourPartPipeTemplate : ICallEmailTemplate
	{
		public async Task<Call> GenerateCall(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls, List<Unit> units,
			int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes, IGeoLocationProvider geolocationProvider)
		{
			if (email == null)
				return null;

			if (String.IsNullOrEmpty(email.Body))
				return null;

			if (String.IsNullOrEmpty(email.Subject))
				return null;

			string[] data = email.Body.Split(char.Parse("|"));

			Call c = new Call();
			c.Notes = email.Subject + " " + email.Body;

			if (data != null && data.Length >= 4)
				c.NatureOfCall = data[3];
			else
				c.NatureOfCall = email.Subject + " " + email.Body;

			if (data != null && data.Length >= 1)
				c.Name = data[0];

			c.LoggedOn = DateTime.UtcNow;
			c.Priority = priority;
			c.ReportingUserId = managingUser;
			c.Dispatches = new Collection<CallDispatch>();
			c.CallSource = (int)CallSources.EmailImport;
			c.SourceIdentifier = email.MessageId;

			if (data != null && data.Length > 3)
			{
				var geoData = data[2].Replace("[LONGLAT:","").Replace("] - 1:", "");

				if (!String.IsNullOrWhiteSpace(geoData))
				{
					var geoDataArray = geoData.Split(char.Parse(","));

					if (geoDataArray != null && geoDataArray.Length == 2)
					{
						c.GeoLocationData = $"{geoDataArray[1]}.{geoDataArray[0]}";
					}
				}
			}

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

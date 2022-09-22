using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;

namespace Resgrid.Services.CallEmailTemplates
{
	public class ParklandCountyTemplate : ICallEmailTemplate
	{
		public async Task<Call> GenerateCall(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls, List<Unit> units,
			int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes, IGeoLocationProvider geolocationProvider)
		{
			if (email == null)
				return null;

			if (String.IsNullOrEmpty(email.Body))
				return null;

			if (!email.Subject.Contains("Parkland County Incident") && !email.Subject.Contains("Incident Message"))
				return null;

			var c = new Call();
			c.Notes = email.Body;

			try
			{
				var data = new List<string>();
				string[] rawData = email.Body.Split(new string[] { "\r\n", "\r\n\r\n" }, StringSplitOptions.None);

				if (rawData != null && rawData.Any())
				{
					foreach (string s in rawData)
					{
						if (!string.IsNullOrWhiteSpace(s))
							data.Add(s);
					}

					if (data.Count > 0)
					{
						c.Name = data[1].Trim().Replace("Type: ", "");

						try
						{
							var location = data.FirstOrDefault(x => x.StartsWith("Location:"));

							if (!String.IsNullOrWhiteSpace(location))
								c.Address = location.Replace("//", "").Replace("Business Name:", "");

							var coordinates = data.FirstOrDefault(x => x.Contains("(") && x.Contains(")"));

							if (!String.IsNullOrWhiteSpace(coordinates))
							{
								coordinates = coordinates.Replace("Common Place:", "");
								coordinates = coordinates.Trim();
								c.GeoLocationData = coordinates.Replace("(", "").Replace(")", "");
							}
						}
						catch
						{ }
					}
				}
				else
				{
					c.Name = "Call Import Type: Unknown";
				}
			}
			catch
			{
				c.Name = "Call Import Type: Unknown";
			}

			c.NatureOfCall = email.Body;
			c.LoggedOn = DateTime.UtcNow;
			c.Priority = priority;
			c.ReportingUserId = managingUser;
			c.Dispatches = new Collection<CallDispatch>();
			c.CallSource = (int)CallSources.EmailImport;
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

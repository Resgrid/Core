using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Resgrid.Model.Identity;
using Resgrid.Model;
using System.Linq;
using Resgrid.Model.Providers;
using System.Threading.Tasks;

namespace Resgrid.Services.CallEmailTemplates
{
	public class CalFireSCUTemplate : ICallEmailTemplate
	{
		public async Task<Call> GenerateCall(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls, List<Unit> units,
			int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes, IGeoLocationProvider geolocationProvider)
		{
			if (email == null)
				return null;

			if (String.IsNullOrEmpty(email.Body) && String.IsNullOrEmpty(email.TextBody))
				return null;

			string emailBody = email.Body;

			if (String.IsNullOrWhiteSpace(email.Body))
				emailBody = email.TextBody;

			if (emailBody.Contains("CLOSE: Inc#"))
				return null;

			Call c = new Call();
			c.Notes = emailBody;

			string[] data = emailBody.Split(char.Parse(";"));

			if (data == null || data.Length <= 2)
				return null;

			var incidentNumber = data.FirstOrDefault(x => x.Contains("Inc#"));
			if (incidentNumber != null)
				c.IncidentNumber = incidentNumber.Replace("Inc# ", "").Trim();

			c.Type = data[0].Trim();

			try
			{
				var mapSection = data.FirstOrDefault(x => x.Contains("?q="));
				if (mapSection != null)
				{
					int start = mapSection.IndexOf("?q=");
					int end = mapSection.IndexOf(">Map");
					c.GeoLocationData = mapSection.Substring(start + 3, (end - start - 4));
				}
			}
			catch { }

			c.NatureOfCall = data[0].Trim() + "   " + data[2].Trim() + "   " + data[3].Trim();
			c.NatureOfCall = c.NatureOfCall.Trim();
			c.Address = data[1].Trim();
			c.Name = data[0].Trim();
			c.LoggedOn = DateTime.UtcNow;
			c.Priority = priority;
			c.ReportingUserId = managingUser;
			c.Dispatches = new Collection<CallDispatch>();
			c.CallSource = (int)CallSources.EmailImport;
			c.SourceIdentifier = email.MessageId;

			var mapPage = data.FirstOrDefault(x => x.Contains("Map:"));
			if (mapPage != null)
				c.MapPage = mapPage.Replace("Map: ", "").Trim();

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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.AspNet.Identity.EntityFramework6;
using Resgrid.Model;

namespace Resgrid.Services.CallEmailTemplates
{
	public class CalFireEccTemplate: ICallEmailTemplate
	{
		public Call GenerateCall(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls, List<Unit> units, int priority)
		{
			if (email == null)
				return null;

			if (String.IsNullOrEmpty(email.Body))
				return null;

			Call c = new Call();
			c.Notes = email.Body;

			string[] data = email.Body.Split(char.Parse(";"));
			c.IncidentNumber = data[0].Replace("Inc# ", "").Trim();
			c.Type = data[1].Trim();

			try
			{
				int end = data[6].IndexOf(">Map");
				c.GeoLocationData = data[6].Substring(37, end - 38);
			}
			catch { }

			c.NatureOfCall = data[1].Trim() + "   " + data[7];
			c.Address = data[2].Trim();
			c.Name = data[1].Trim() + " " + data[7];
			c.LoggedOn = DateTime.UtcNow;
			c.Priority = priority;
			c.ReportingUserId = managingUser;
			c.Dispatches = new Collection<CallDispatch>();
			c.CallSource = (int) CallSources.EmailImport;
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

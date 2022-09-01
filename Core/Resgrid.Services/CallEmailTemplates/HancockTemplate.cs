using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;

namespace Resgrid.Services.CallEmailTemplates
{
	/// <summary>
	/// (call code),type, address or coordinates,//nature and notes... so our pages look like this 
	/// (29), Medical/EMS, 521 W MAIN CROSS ST # 108, ARLINGTON, OH 45814,// MEDICAL ALARM SHOWS UNIT IS VACANT NO PX NUMBER FOR ACCOUNT.
	/// 4 Sections
	/// </summary>
	public class HancockTemplate : ICallEmailTemplate
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

			var c = new Call();

			c.Notes = email.TextBody;

			if (String.IsNullOrWhiteSpace(c.Notes))
				c.Notes = email.Body;

			string[] data = body.Split(char.Parse(","));

			if (data == null || data.Length <= 2)
				return null;
			
			c.IncidentNumber = data[0].Replace("(", "").Replace(")", "").Trim();
			c.Type = data[1].Trim();

			string address = String.Empty;
			string coordinates = String.Empty;
			int gpsCount = 0;

			for (int i = 2; i < data.Length; i++)
			{
				if (data[i].Contains("//"))
					break;

				decimal myDec;
				if (!decimal.TryParse(data[i].Trim(), out myDec))
				{
					if (String.IsNullOrWhiteSpace(address))
						address = data[i].Trim();
					else
						address += string.Format(", {0}", data[i].Trim());
				}
				else
				{
					if (gpsCount >= 2)
						break;

					if (String.IsNullOrWhiteSpace(coordinates))
						coordinates = data[i].Trim();
					else
						coordinates += string.Format(",{0}", data[i].Trim());

					gpsCount++;
				}
			}

			if (!String.IsNullOrWhiteSpace(address))
				c.Address = address.Trim();

			if (!String.IsNullOrWhiteSpace(coordinates))
				c.GeoLocationData = coordinates.Trim();

			c.NatureOfCall = data[data.Length - 1].Replace("//", "").Trim();
			c.Name = string.Format("{0}-{1}", c.IncidentNumber, c.Type);
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

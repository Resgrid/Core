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
	public class Active911lTemplate : ICallEmailTemplate
	{
		public async Task<Call> GenerateCall(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls,
			List<Unit> units, int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes, IGeoLocationProvider geolocationProvider)
		{
			if (email == null)
				return null;

			if (String.IsNullOrEmpty(email.TextBody))
				return null;


			if (String.IsNullOrEmpty(email.Subject))
				return null;

			if (!email.Subject.ToLower().Contains("active"))
				return null;

			Call c = new Call();

			// For 7966
			if (department == null || department.DepartmentId != 7966)
				c.Notes = email.TextBody;

			c.LoggedOn = DateTime.UtcNow;
			c.Priority = priority;
			c.ReportingUserId = managingUser;
			c.Dispatches = new Collection<CallDispatch>();
			c.CallSource = (int)CallSources.EmailImport;
			c.SourceIdentifier = email.MessageId;

			string[] lines = email.TextBody.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

			var sb = new StringBuilder();
			sb.Append("<p>");
			foreach (var line in lines)
			{
				var trimmedLine = line.Trim();

				if (!String.IsNullOrWhiteSpace(trimmedLine))
				{
					if (trimmedLine.ToLower().StartsWith("call:"))
					{
						c.Name = trimmedLine.Replace("CALL:", "").Trim();
						sb.Append(trimmedLine + "</br>");

						if (department != null && department.DepartmentId == 7966)
						{
							string[] typePrioline = c.Name.Split(new[] { " " }, StringSplitOptions.None);
							c.Type = typePrioline[0].Trim();
							c.Priority = ParseCallPriority(typePrioline[0].Trim(), priority, activePriorities);
						}
					}
					else if (trimmedLine.ToLower().StartsWith("addr:"))
					{
						//c.GeoLocationData = trimmedLine.Replace("ADDR:", "").Trim();
						sb.Append(trimmedLine + "</br>");
					}
					else if (trimmedLine.ToLower().StartsWith("addr1:"))
					{
						c.Address = trimmedLine.Replace("ADDR1:", "").Trim();
						sb.Append(trimmedLine + "</br>");
					}
					else if (trimmedLine.ToLower().StartsWith("id:"))
					{
						c.IncidentNumber = trimmedLine.Replace("ID:", "").Trim();
						c.ExternalIdentifier = c.IncidentNumber;

						if (department != null && department.DepartmentId == 7966)
							c.ReferenceNumber = ParseIncidentNumber(trimmedLine);

						sb.Append(trimmedLine + "</br>");
					}
					else if (trimmedLine.ToLower().StartsWith("map:"))
					{
						c.GeoLocationData = trimmedLine.Replace("MAP:", "").Replace("http://www.google.com/maps/place/", "").Trim();
					}
					else if (trimmedLine.ToLower().StartsWith("date/time:"))
					{
						sb.Append(trimmedLine + "</br>");
					}
					else if (trimmedLine.ToLower().StartsWith("date/time:"))
					{
						sb.Append(trimmedLine + "</br>");
					}
					else if (trimmedLine.ToLower().StartsWith("narr:"))
					{
						sb.Append(trimmedLine.Replace("NARR:", "").Trim() + "</br>");
					}
				}
			}
			sb.Append("</p>");
			c.NatureOfCall = sb.ToString();

			foreach (var u in users)
			{
				CallDispatch cd = new CallDispatch();
				cd.UserId = u.UserId;

				c.Dispatches.Add(cd);
			}

			// Search for an active call
			if (activeCalls != null && activeCalls.Any())
			{
				var activeCall = activeCalls.FirstOrDefault(x => x.IncidentNumber == c.IncidentNumber);

				if (activeCall != null)
				{
					if (department == null || department.DepartmentId != 7966)
						activeCall.Notes = c.Notes;
					else
						c.NatureOfCall = sb.ToString();

					activeCall.LastDispatchedOn = DateTime.UtcNow;

					return activeCall;
				}
			}

			return c;
		}

		// For 7966
		private int ParseCallPriority(string data, int priority, List<DepartmentCallPriority> activePriorities)
		{
			if (activePriorities != null && activePriorities.Any())
			{
				var customPriority = activePriorities.FirstOrDefault(x => x.Name.ToLower() == data.ToLower());

				if (customPriority != null)
					return customPriority.DepartmentCallPriorityId;
			}

			return priority;
		}

		// For 7966
		private string ParseIncidentNumber(string data)
		{
			try
			{
				var fixedData = data.Replace("Incident Numbers:", "").Replace("[", "").Replace("]", "").Trim();
				var lines = fixedData.Split(new[] { ",", }, StringSplitOptions.None);

				var incidentNumber = lines.FirstOrDefault(x => x.Trim().EndsWith("00117"));

				if (!String.IsNullOrWhiteSpace(incidentNumber))
				{
					var strippedIncidentNumber = incidentNumber.Replace("00117", "").Remove(0, 5).TrimStart(char.Parse("0")).Trim();
					return strippedIncidentNumber;
				}
			}
			catch { }

			return null;
		}
	}
}

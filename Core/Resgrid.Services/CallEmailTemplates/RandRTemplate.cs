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
	public class RandRTemplate : ICallEmailTemplate
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

			string[] lines = email.TextBody.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

			Call c = new Call();
			c.Notes = email.TextBody;
			c.Name = email.Subject.Replace("Automatic R&R Notification:", "").Replace("Fw:", "").Replace("FW:", "").Replace("fw:", "").Trim();
			c.LoggedOn = DateTime.UtcNow;
			c.Priority = priority;
			c.ReportingUserId = managingUser;
			c.Dispatches = new Collection<CallDispatch>();
			c.CallSource = (int)CallSources.EmailImport;
			c.SourceIdentifier = email.MessageId;

			var sb = new StringBuilder();
			sb.Append("<p>");
			foreach (var line in lines)
			{
				var trimmedLine = line.Trim();

				if (!String.IsNullOrWhiteSpace(trimmedLine))
				{
					if (trimmedLine.StartsWith("Address:"))
					{
						c.Address = trimmedLine.Replace("Address:", "").Trim();
						sb.Append(trimmedLine + "</br>");
					}
					else if (trimmedLine.StartsWith("Common Name:"))
					{
						if (trimmedLine.Length > 12)
						{
							sb.Append(trimmedLine + "</br>");
						}
					}
					else if (trimmedLine.StartsWith("Cross Streets:"))
					{
						sb.Append(trimmedLine + "</br>");
					}
					else if (trimmedLine.StartsWith("https://www.google.com"))
					{
						sb.Append(trimmedLine + "</br>");
					}
					else if (trimmedLine.StartsWith("Call Date/Time:"))
					{
						sb.Append(trimmedLine + "</br>");
					}
					else if (trimmedLine.StartsWith("Call Type:"))
					{
						sb.Append(trimmedLine + "</br>");
						string[] typePrioline = trimmedLine.Split(new[] { ", ," }, StringSplitOptions.None);

						if (typePrioline.Length == 2)
						{
							c.Type = typePrioline[0].Replace("Call Type:", "").Replace(", ,", "").Trim();
							c.Priority = ParseCallPriority(typePrioline[1].Replace("Call Priority:", "").Replace(", ,", "").Trim(), priority, activePriorities);
						}
					}
					else if (trimmedLine.StartsWith("Fire Area:"))
					{
						sb.Append(trimmedLine + "</br>");
					}
					else if (trimmedLine.StartsWith("Incident Numbers:"))
					{
						sb.Append(trimmedLine + "</br>");
						c.IncidentNumber = ParseIncidentNumber(trimmedLine);
					}
					else if (trimmedLine.StartsWith("Units:"))
					{
						sb.Append(trimmedLine + "</br>");
					}
				}
			}
			sb.Append("</p>");
			c.NatureOfCall = sb.ToString();

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

		private int ParseCallPriority(string data, int priority, List<DepartmentCallPriority> activePriorities)
		{
			if (activePriorities != null && activePriorities.Any())
			{
				var customPriority = activePriorities.FirstOrDefault(x => x.Name == data);

				if (customPriority != null)
					return customPriority.DepartmentCallPriorityId;
			}

			return priority;
		}

		//Incident Numbers: [2020-00003053 AMB_RPS], [2020-00000249 00117], [2020-00056937 AL0010000], [2020-00001122 00111]
		private string ParseIncidentNumber(string data)
		{
			var fixedData = data.Replace("Incident Numbers:", "").Replace("[", "").Replace("]", "").Trim();
			var lines = fixedData.Split(new[] { ",", }, StringSplitOptions.None);

			var incidentNumber = lines.FirstOrDefault(x => x.Trim().EndsWith("00117"));

			if (!String.IsNullOrWhiteSpace(incidentNumber))
			{
				var strippedIncidentNumber = incidentNumber.Replace("00117", "").Remove(0, 5).TrimStart(char.Parse("0")).Trim();
				return strippedIncidentNumber;
			}

			return null;
		}
	}
}

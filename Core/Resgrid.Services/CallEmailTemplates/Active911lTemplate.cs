using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
						c.Name = ValueAfter(trimmedLine, "call:");
						sb.Append(trimmedLine + "</br>");

						if (department != null && department.DepartmentId == 7966)
						{
							// "Medical Chest Pain", "Fire Structure Residential": the first word is the call type. Keep the
							// department's own spelling, and its id, when it has that type.
							var typeWord = c.Name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
							var callType = CallTypeLink.FindByName(callTypes, typeWord);
							c.Type = callType?.Type ?? typeWord;
							if (callType != null)
								c.CallTypeId = callType.CallTypeId;

							// Its priorities are named after incident kinds ("Medical", "Structure Fire", "Brush / Woods Fire"):
							// the type word exactly, else a priority whose words all appear in the line, else the default.
							c.Priority = CallPriorityMatch.Find(activePriorities, c.Name, typeWord)?.DepartmentCallPriorityId ?? priority;
						}
					}
					else if (trimmedLine.ToLower().StartsWith("addr:"))
					{
						//c.GeoLocationData = trimmedLine.Replace("ADDR:", "").Trim();
						sb.Append(trimmedLine + "</br>");
					}
					else if (trimmedLine.ToLower().StartsWith("addr1:"))
					{
						c.Address = ValueAfter(trimmedLine, "addr1:");
						sb.Append(trimmedLine + "</br>");
					}
					else if (trimmedLine.ToLower().StartsWith("id:"))
					{
						c.IncidentNumber = ValueAfter(trimmedLine, "id:");
						c.ExternalIdentifier = c.IncidentNumber;

						if (department != null && department.DepartmentId == 7966)
							c.ReferenceNumber = ParseIncidentNumber(trimmedLine);

						sb.Append(trimmedLine + "</br>");
					}
					else if (trimmedLine.ToLower().StartsWith("map:"))
					{
						c.GeoLocationData = ValueAfter(trimmedLine, "map:").Replace("http://www.google.com/maps/place/", "").Trim();
					}
					else if (trimmedLine.ToLower().StartsWith("date/time:"))
					{
						sb.Append(trimmedLine + "</br>");
					}
					else if (trimmedLine.ToLower().StartsWith("narr:"))
					{
						sb.Append(ValueAfter(trimmedLine, "narr:") + "</br>");
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

			// Search for an active call. Only by an incident number: a message without one must not attach to whichever open
			// call also has none.
			if (activeCalls != null && activeCalls.Any() && !String.IsNullOrWhiteSpace(c.IncidentNumber))
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

		/// <summary>The text after a field prefix such as "CALL:", whatever its case (the field was found case-insensitively).</summary>
		private static string ValueAfter(string line, string prefix)
		{
			return line.Length > prefix.Length ? line.Substring(prefix.Length).Trim() : String.Empty;
		}

		// For 7966
		private string ParseIncidentNumber(string data)
		{
			try
			{
				// The current feed sends "ID: 00117-2026-00000474": the reference is the sequence after the agency and
				// year, as the older "...00117" numbers below gave.
				var current = Regex.Match(data, @"\b00117-\d{4}-0*(\d+)\b");
				if (current.Success)
					return current.Groups[1].Value;

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

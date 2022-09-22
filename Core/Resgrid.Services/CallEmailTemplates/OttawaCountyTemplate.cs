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
	/* 
	 CFS#: 5555555555
	 CallType: SQUAD
	 Address: 555 MAIN ST, GENOA, OH 43430
	 Details: 
	 BACK INJURY FROM FALL/ MAIN DOOR
	 */
	public class OttawaCountyTemplate : ICallEmailTemplate
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

			c.Notes = email.TextBody;
			c.LoggedOn = DateTime.UtcNow;
			c.Priority = priority;
			c.ReportingUserId = managingUser;
			c.Dispatches = new Collection<CallDispatch>();
			c.CallSource = (int)CallSources.EmailImport;
			c.SourceIdentifier = email.MessageId;

			string[] lines = email.TextBody.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

			int dispatchLine = 0;
			var sb = new StringBuilder();
			for (int i = 0; i < lines.Length; i++)
			{
				if (!String.IsNullOrEmpty(lines[i]))
				{
					if (lines[i].StartsWith("CFS#:"))
						c.ExternalIdentifier = lines[i].Replace("CFS#:", "").Trim();
					else if (lines[i].StartsWith("Address:"))
					{
						var address = lines[i].Replace("Address:", "").Trim();
						c.Address = address;

						var location = await geolocationProvider.GetLatLonFromAddress(address);

						if (!String.IsNullOrWhiteSpace(location))
							c.GeoLocationData = location;
					}
					else if (lines[i].StartsWith("CallType:"))
					{
						var callTypeString = lines[i].Replace("CallType:", "").Trim();
						var parsedCallType = ParseCallType(callTypeString, callTypes);

						if (parsedCallType != null)
							c.Type = parsedCallType;
					}
					else if (lines[i].StartsWith("Details:"))
					{
						dispatchLine = i;

						var nature = lines[i].Replace("Details:", "").Trim();
						sb.Append(nature);
					}
					else if (dispatchLine > 0)
					{
						if (!String.IsNullOrWhiteSpace(lines[i]))
						{
							sb.Append(" ");
							sb.Append(lines[i]);
						}

					}

				}
			}

			c.NatureOfCall = sb.ToString();

			foreach (var u in users)
			{
				CallDispatch cd = new CallDispatch();
				cd.UserId = u.UserId;

				c.Dispatches.Add(cd);
			}

			return c;
		}

		private string ParseCallType(string data, List<CallType> callTypes)
		{
			if (callTypes != null && callTypes.Any())
			{
				var customType = callTypes.FirstOrDefault(x => x.Type.ToLower() == data.ToLower());

				if (customType != null)
					return customType.Type;
			}

			return null;
		}
	}
}

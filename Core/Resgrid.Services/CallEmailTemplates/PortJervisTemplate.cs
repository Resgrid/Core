using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Resgrid.Model;
using Resgrid.Model.Identity;
using System.Linq;
using Resgrid.Framework;
using Resgrid.Model.Providers;
using System.Threading.Tasks;

namespace Resgrid.Services.CallEmailTemplates
{
	public class PortJervisTemplate : ICallEmailTemplate
	{
		public async Task<Call> GenerateCall(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls, List<Unit> units,
			int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes, IGeoLocationProvider geolocationProvider)
		{
			if (email == null)
				return null;

			if (String.IsNullOrEmpty(email.Body))
				return null;

			//if (String.IsNullOrEmpty(email.Subject))
			//	return null;

			Call c = new Call();
			c.Notes = email.Subject + " " + email.Body;
			c.NatureOfCall = email.Body;
			c.Name = "Call Email Import";
			c.LoggedOn = DateTime.UtcNow;
			c.Priority = priority;
			c.ReportingUserId = managingUser;
			c.Dispatches = new Collection<CallDispatch>();
			c.CallSource = (int)CallSources.EmailImport;
			c.SourceIdentifier = email.MessageId;

			string[] rawData = email.Body.Split(new string[] { "\r\n", "\r\n\r\n" }, StringSplitOptions.None);

			if (rawData != null && rawData.Any())
			{
				foreach (string s in rawData)
				{
					if (!string.IsNullOrWhiteSpace(s) && s.StartsWith("La", StringComparison.InvariantCultureIgnoreCase))
					{
						string[] geoData = s.Split(new string[] { "  " }, StringSplitOptions.None);

						if (geoData != null && geoData.Length == 2)
						{
							var latDMS = geoData[0].Replace(" grader ", ",").Replace("La = ", "").Replace("'N", "").Trim();
							var lonDMS = geoData[1].Replace(" grader ", ",").Replace("Lo = ", "").Replace("'E", "").Trim();

							if (!String.IsNullOrWhiteSpace(latDMS) && !String.IsNullOrWhiteSpace(lonDMS))
							{
								string[] latValues = latDMS.Split(new string[] { "," }, StringSplitOptions.None);
								string[] lonValues = lonDMS.Split(new string[] { "," }, StringSplitOptions.None);

								double lat = 0;
								if (latValues != null && latValues.Length == 3)
								{
									lat = LocationHelpers.ConvertDegreesToDecimal(latValues[0].Trim(), latValues[1].Trim(), latValues[2].Trim());
								}

								double lon = 0;
								if (lonValues != null && lonValues.Length == 3)
								{
									lon = LocationHelpers.ConvertDegreesToDecimal(lonValues[0].Trim(), lonValues[1].Trim(), lonValues[2].Trim());
								}


								if (lat != 0 && lon != 0)
								{
									c.GeoLocationData = $"{lat},{lon}";
								}
							}
						}
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

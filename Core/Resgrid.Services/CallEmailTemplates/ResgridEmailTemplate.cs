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
	public class ResgridEmailTemplate : ICallEmailTemplate
	{
		public async Task<Call> GenerateCall(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls,
			List<Unit> units, int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes, IGeoLocationProvider geolocationProvider)
		{
			//ID | TYPE | PRIORITY | ADDRESS | MAPPAGE | NATURE | NOTES

			if (email == null)
				return null;

			if (String.IsNullOrEmpty(email.Body))
				return null;

			Call c = new Call();
			c.Notes = email.Body;

			// Seed the department default up front so every exit path (including the
			// fallback below) leaves a priority the department actually owns on the call.
			c.Priority = priority;

			string[] data = email.Body.Split(char.Parse("|"));

			// ADDRESS and NATURE are the two required values and they sit at index 3 and 5,
			// so a body needs at least 6 segments to be a Resgrid format message. NOTES
			// (index 6) is optional and anything past it is treated as part of the notes.
			if (data.Length >= 6)
			{
				c.IncidentNumber = GetValue(data, 0);
				c.Type = ParseCallType(GetValue(data, 1), callTypes);
				c.Priority = ParseCallPriority(GetValue(data, 2), priority, activePriorities);
				c.MapPage = GetValue(data, 4);

				// NATURE is a non-nullable column but CADs do send the segment empty. Fall back to
				// the call type and then the subject so the dispatch still lands, GetValue hands
				// back a null for a blank segment and that used to fail the insert.
				c.NatureOfCall = GetValue(data, 5) ?? GetValue(data, 1) ?? email.Subject;

				// Re-join everything from index 6 on, a pipe inside the notes text shouldn't
				// truncate them. When NOTES isn't supplied the raw body stays in Notes, which
				// is the behavior imports have always had.
				if (data.Length > 6)
				{
					var notes = String.Join("|", data.Skip(6)).Trim();

					if (!String.IsNullOrWhiteSpace(notes))
						c.Notes = notes;
				}

				var address = GetValue(data, 3);

				if (!String.IsNullOrEmpty(address))
				{
					c.Address = address;

					try
					{
						var geolocation = await geolocationProvider.GetLatLonFromAddress(c.Address);


						if (geolocation != null)
							c.GeoLocationData = geolocation;
					}
					catch (Exception ex)
					{
						Resgrid.Framework.Logging.LogException( ex,
						$"Failed to geocode address '{c.Address}' for email {email.MessageId}");
					}
				}
				StringBuilder title = new StringBuilder();

				title.Append("Email Call ");

				var priorityName = GetCallPriorityName(c.Priority, activePriorities);

				if (!String.IsNullOrEmpty(priorityName))
				{
					title.Append(priorityName);
					title.Append(" ");
				}

				if (!string.IsNullOrEmpty(c.Type))
				{
					title.Append(c.Type);
					title.Append(" ");
				}

				if (!string.IsNullOrEmpty(c.IncidentNumber))
				{
					title.Append(c.IncidentNumber);
					title.Append(" ");
				}

				c.Name = title.ToString();
			}
			else
			{
				c.Name = email.Subject;
				c.NatureOfCall = email.Body;
				c.Notes = "WARNING: FALLBACK RESGRID EMAIL IMPORT! THIS EMAIL MAY NOT BE THE CORRECT FORMAT FOR THE RESGRID EMAIL TEMPLATE. CONTACT SUPPORT IF THE EMAIL AND TEMPLATE ARE CORRECT." + email.Body;
			}

			c.LoggedOn = DateTime.UtcNow;
			c.ReportingUserId = managingUser;
			c.Dispatches = new Collection<CallDispatch>();
			c.CallSource = (int)CallSources.EmailImport;
			c.SourceIdentifier = email.MessageId;

			foreach (var u in users)
			{
				CallDispatch cd = new CallDispatch();
				cd.UserId = u.UserId;

				c.Dispatches.Add(cd);
			}

			return c;
		}

		private static string GetValue(string[] data, int index)
		{
			if (data == null || index < 0 || index >= data.Length)
				return null;

			var value = data[index];

			if (String.IsNullOrWhiteSpace(value))
				return null;

			return value.Trim();
		}

		/// <summary>
		/// TYPE is documented as free text so whatever the CAD sends is kept, but when the
		/// department has Custom Call Types the value is normalized to the casing of the
		/// configured type so protocol triggers, filters and reports match on it.
		/// </summary>
		private static string ParseCallType(string data, List<CallType> callTypes)
		{
			if (String.IsNullOrWhiteSpace(data))
				return null;

			if (callTypes != null && callTypes.Any())
			{
				var customType = callTypes.FirstOrDefault(x => !String.IsNullOrWhiteSpace(x.Type) &&
															   String.Equals(x.Type.Trim(), data, StringComparison.OrdinalIgnoreCase));

				if (customType != null)
					return customType.Type;
			}

			return data;
		}

		/// <summary>
		/// PRIORITY accepts the priority name or its identifier. Departments on the system
		/// priorities keep the documented Low = 0, Medium = 1, High = 2, Emergency = 3
		/// integers (those are their identifiers), departments with Custom Call Priorities
		/// can send the priority name instead of an internal identifier they can't see.
		/// Anything that doesn't resolve falls back to the department default, an identifier
		/// the department doesn't own would leave dispatch without a priority to resolve.
		/// </summary>
		private static int ParseCallPriority(string data, int priority, List<DepartmentCallPriority> activePriorities)
		{
			if (String.IsNullOrWhiteSpace(data))
				return priority;

			if (activePriorities != null && activePriorities.Any())
			{
				var namedPriority = activePriorities.FirstOrDefault(x => !x.IsDeleted && !String.IsNullOrWhiteSpace(x.Name) &&
																		 String.Equals(x.Name.Trim(), data, StringComparison.OrdinalIgnoreCase));

				if (namedPriority != null)
					return namedPriority.DepartmentCallPriorityId;

				int parsedPriorityId;
				if (int.TryParse(data, out parsedPriorityId))
				{
					var idPriority = activePriorities.FirstOrDefault(x => !x.IsDeleted && x.DepartmentCallPriorityId == parsedPriorityId);

					if (idPriority != null)
						return idPriority.DepartmentCallPriorityId;
				}

				return priority;
			}

			// No priority list was supplied by the caller, fall back to the built in priorities.
			int parsedPriority;
			if (int.TryParse(data, out parsedPriority) && Enum.IsDefined(typeof(CallPriority), parsedPriority))
				return parsedPriority;

			CallPriority namedSystemPriority;
			if (Enum.TryParse<CallPriority>(data, true, out namedSystemPriority) && Enum.IsDefined(typeof(CallPriority), namedSystemPriority))
				return (int)namedSystemPriority;

			return priority;
		}

		private static string GetCallPriorityName(int priority, List<DepartmentCallPriority> activePriorities)
		{
			if (activePriorities != null && activePriorities.Any())
			{
				var match = activePriorities.FirstOrDefault(x => x.DepartmentCallPriorityId == priority);

				if (match != null && !String.IsNullOrWhiteSpace(match.Name))
					return match.Name.Trim();
			}

			if (Enum.IsDefined(typeof(CallPriority), priority))
				return ((CallPriority)priority).ToString();

			return String.Empty;
		}
	}
}

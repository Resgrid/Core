using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;

namespace Resgrid.Services.CallEmailTemplates
{
	public class LowestoftCoastGuardTemplate : ICallEmailTemplate
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

			Call c = new Call();
			c.Notes = email.Subject + " " + email.Body;
			c.Name = email.Subject;

			// "priority, nature, place[, instructions …]", e.g. "3, Persons in water, Southwold, Muster at CRE".
			string[] data = email.Body.Split(char.Parse(","));

			// The page's priority, resolved against the department's priorities (name or identifier, else the built-in
			// 0-3); anything the department doesn't own falls back to its default rather than a dangling identifier.
			c.Priority = ResgridEmailTemplate.ParseCallPriority(data[0].Trim(), priority, activePriorities);

			if (data.Length >= 2)
			{
				var nature = data[1].Trim();
				if (nature.Length > 3)
					c.NatureOfCall = nature;
			}
			else
			{
				c.NatureOfCall = email.Body;
			}

			if (data.Length >= 3)
			{
				var place = data[2].Trim();
				if (place.Length > 3)
					c.Address = place.IndexOf("United Kingdom", StringComparison.OrdinalIgnoreCase) >= 0 ? place : place + ", United Kingdom";
			}

			c.LoggedOn = DateTime.UtcNow;
			c.ReportingUserId = managingUser;
			c.Dispatches = new Collection<CallDispatch>();
			c.CallSource = (int)CallSources.EmailImport;
			c.SourceIdentifier = email.MessageId;

			if (email.DispatchAudio != null)
			{
				c.Attachments = new Collection<CallAttachment>();

				CallAttachment ca = new CallAttachment();
				ca.FileName = email.DispatchAudioFileName;
				ca.Data = email.DispatchAudio;
				ca.CallAttachmentType = (int)CallAttachmentTypes.DispatchAudio;

				c.Attachments.Add(ca);
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

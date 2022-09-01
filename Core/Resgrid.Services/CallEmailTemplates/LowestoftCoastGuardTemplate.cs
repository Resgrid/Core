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

			string[] data = email.Body.Split(char.Parse(","));

			if (data.Length >= 1)
			{
				int tryPriority;

				if (int.TryParse(data[0], out tryPriority))
					c.Priority = tryPriority;
			}
			else
			{
				c.Priority = (int)CallPriority.High;
			}

			if (data.Length >= 2)
			{
				if (data[1].Length > 3)
					c.NatureOfCall = data[1];
			}
			else
			{
				c.NatureOfCall = email.Body;
			}

			if (data.Length >= 3)
			{
				if (data[2].Length > 3)
				{
					string address = String.Empty;

					if (!data[2].Contains("United Kingdom"))
					{
						address = data[2] + "United Kingdom";
					}

					c.Address = address;
				}
			}

			c.LoggedOn = DateTime.UtcNow;
			c.Priority = priority;
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

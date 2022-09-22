using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;

namespace Resgrid.Services.CallEmailTemplates
{
	public class UnionFireTemplate : ICallEmailTemplate
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

			if (!email.Subject.Contains("Perform Page"))
				return null;

			Call c = new Call();
			c.Notes = email.Subject + " " + email.Body;
			c.NatureOfCall = email.Body;

			List<string> data = new List<string>();
			string[] rawData = email.Body.Split(new string[] { "\r\n", "\r\n\r\n" }, StringSplitOptions.None);

			foreach (string s in rawData)
			{
				if (!string.IsNullOrWhiteSpace(s))
					data.Add(s);
			}

			if (data.Count >= 1)
				c.Name = data[0];
			else
				c.Name = email.Subject;

			if (data.Count == 5)
			{
				string firstLine = string.Empty;

				if (data[1].ToUpper().Contains("AND"))
				{
					string[] firstLineParts = data[1].Split(new string[] { "AND" }, StringSplitOptions.None);

					if (!string.IsNullOrWhiteSpace(firstLineParts[0]))
						firstLine = firstLineParts[0];
				}
				else if (data[1].ToUpper().Contains("/"))
				{
					string[] firstLineParts = data[1].Split(new string[] { "/" }, StringSplitOptions.None);

					if (!string.IsNullOrWhiteSpace(firstLineParts[0]))
						firstLine = firstLineParts[0];
				}
				else
				{
					firstLine = data[1];
				}

				c.Address = string.Format("{0} {1}", firstLine, data[2]);
			}
			else if (data.Count == 6)
			{
				c.Address = string.Format("{0} {1}", data[2], data[3]);
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

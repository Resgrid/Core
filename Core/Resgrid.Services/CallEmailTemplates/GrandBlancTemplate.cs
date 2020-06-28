using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Services.CallEmailTemplates
{
	public class GrandBlancTemplate : ICallEmailTemplate
	{
		public Call GenerateCall(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls, List<Unit> units, int priority)
		{
			if (email == null)
				return null;

			if (String.IsNullOrEmpty(email.Body))
				return null;

			if (String.IsNullOrEmpty(email.Subject))
				return null;

			if (!email.Subject.Contains("Page Received"))
				return null;

			Call c = new Call();
			c.Notes = email.Subject + " " + email.Body;
			c.NatureOfCall = email.Subject + " " + email.Body;
			c.Name = email.Subject;
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

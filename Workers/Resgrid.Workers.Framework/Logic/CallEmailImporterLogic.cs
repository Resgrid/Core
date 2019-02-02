using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autofac;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Workers.Framework.Logic
{
	public class CallEmailImporterLogic
	{
		private ICallsService _callsService;
		private IQueueService _queueService;
		private IDepartmentsService _departmentsService;
		private ICallEmailProvider _callEmailProvider;
		private IUserProfileService _userProfileService;
		private IDepartmentSettingsService _departmentSettingsService;
		private IUnitsService _unitsService;

		public Tuple<bool, string> Process(CallEmailQueueItem item)
		{
			bool success = true;
			string result = "";
			_callEmailProvider = Bootstrapper.GetKernel().Resolve<ICallEmailProvider>();

			if (!String.IsNullOrWhiteSpace(item?.EmailSettings?.Hostname))
			{
				CallEmailsResult emailResult = _callEmailProvider.GetAllCallEmailsFromServer(item.EmailSettings);

				if (emailResult?.Emails != null && emailResult.Emails.Count > 0)
				{
					var calls = new List<Call>();

					_callsService = Bootstrapper.GetKernel().Resolve<ICallsService>();
					_queueService = Bootstrapper.GetKernel().Resolve<IQueueService>();
					_departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
					_userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
					_departmentSettingsService = Bootstrapper.GetKernel().Resolve<IDepartmentSettingsService>();
					_unitsService = Bootstrapper.GetKernel().Resolve<IUnitsService>();

					// Ran into an issue where the department users didn't come back. We can't put the email back in the POP
					// email box so just added some simple retry logic here.
					List<IdentityUser> departmentUsers = _departmentsService.GetAllUsersForDepartment(item.EmailSettings.DepartmentId, true);
					var profiles = _userProfileService.GetAllProfilesForDepartment(item.EmailSettings.DepartmentId);

					int retry = 0;
					while (retry < 3 && departmentUsers == null)
					{
						Thread.Sleep(150);
						departmentUsers = _departmentsService.GetAllUsersForDepartment(item.EmailSettings.DepartmentId, true);
						retry++;
					}

					foreach (var email in emailResult.Emails)
					{
						var activeCalls = _callsService.GetActiveCallsByDepartment(item.EmailSettings.Department.DepartmentId);
						var units = _unitsService.GetUnitsForDepartment(item.EmailSettings.Department.DepartmentId);

						var priorities = _callsService.GetActiveCallPrioritesForDepartment(item.EmailSettings.Department.DepartmentId);
						int defaultPriority = (int)CallPriority.High;

						if (priorities != null && priorities.Any())
						{
							var defaultPrio = priorities.FirstOrDefault(x => x.IsDefault && x.IsDeleted == false);

							if (defaultPrio != null)
								defaultPriority = defaultPrio.DepartmentCallPriorityId;
						}

						var call = _callsService.GenerateCallFromEmail(item.EmailSettings.FormatType, email,
							item.EmailSettings.Department.ManagingUserId, departmentUsers, item.EmailSettings.Department, activeCalls, units, defaultPriority);

						if (call != null)
						{
							call.DepartmentId = item.EmailSettings.DepartmentId;

							if (!calls.Any(x => x.Name == call.Name && x.NatureOfCall == call.NatureOfCall))
								calls.Add(call);
						}
					}

					if (calls.Any())
					{
						var departmentTextNumber = _departmentSettingsService.GetTextToCallNumberForDepartment(item.EmailSettings.DepartmentId);
						foreach (var call in calls)
						{
							try
							{
								// Adding this in here to try and fix the error below with ObjectContext issues. 
								var newCall = CreateNewCallFromCall(call);

								if (newCall.Dispatches != null && newCall.Dispatches.Any())
								{
									// We've been having this error here:
									//      The relationship between the two objects cannot be defined because they are attached to different ObjectContext objects.
									// So I'm wrapping this in a try catch to prevent all calls form being dropped.
									var savedCall = _callsService.SaveCall(newCall);
									var cqi = new CallQueueItem();
									cqi.Call = savedCall;

									cqi.Profiles = profiles.Values.ToList();
									cqi.DepartmentTextNumber = departmentTextNumber;

									_queueService.EnqueueCallBroadcast(cqi);
								}
							}
							catch (Exception ex)
							{
								result = ex.ToString();
								Logging.LogException(ex);
							}

						}
					}

					_departmentsService.SaveDepartmentEmailSettings(emailResult.EmailSettings);

					_callsService = null;
					_queueService = null;
					_departmentsService = null;
					_callEmailProvider = null;
					_userProfileService = null;
					_departmentSettingsService = null;
				}
			}

			return new Tuple<bool, string>(success, result);
		}

		private Call CreateNewCallFromCall(Call call)
		{
			Call newCall = new Call();
			newCall.Number = call.Number;
			newCall.DepartmentId = call.DepartmentId;
			newCall.ReportingUserId = call.ReportingUserId;
			newCall.Priority = call.Priority;
			newCall.IsCritical = call.IsCritical;
			newCall.Type = call.Type;
			newCall.IncidentNumber = call.IncidentNumber;
			newCall.Name = call.Name;
			newCall.NatureOfCall = call.NatureOfCall;
			newCall.MapPage = call.MapPage;
			newCall.Notes = call.Notes;
			newCall.CompletedNotes = call.CompletedNotes;
			newCall.Address = call.Address;
			newCall.GeoLocationData = call.GeoLocationData;
			newCall.LoggedOn = call.LoggedOn;
			newCall.ClosedByUserId = call.ClosedByUserId;
			newCall.ClosedOn = call.ClosedOn;
			newCall.State = call.State;
			newCall.IsDeleted = call.IsDeleted;
			newCall.CallSource = call.CallSource;
			newCall.SourceIdentifier = call.SourceIdentifier;
			newCall.State = call.State;

			if (call.Dispatches != null && call.Dispatches.Any())
			{
				newCall.Dispatches = new List<CallDispatch>();

				foreach (var callDispatch in call.Dispatches)
				{
					var cd = new CallDispatch();
					cd.UserId = callDispatch.UserId;

					newCall.Dispatches.Add(cd);
				}
			}

			if (call.Attachments != null && call.Attachments.Any())
			{
				newCall.Attachments = new List<CallAttachment>();

				foreach (var callAttahment in call.Attachments)
				{
					var ca = new CallAttachment();
					ca.CallAttachmentType = callAttahment.CallAttachmentType;
					ca.Data = callAttahment.Data;
					ca.FileName = callAttahment.FileName;

					newCall.Attachments.Add(ca);
				}
			}

			return newCall;
		}
	}
}

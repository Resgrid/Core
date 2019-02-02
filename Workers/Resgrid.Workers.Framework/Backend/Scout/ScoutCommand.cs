using System;
using System.Net;
using System.Text;
using Resgrid.Model;
using Resgrid.Model.Services;
using RestSharp;

namespace Resgrid.Workers.Framework.Backend.Scout
{
	internal class StatusResult
	{
		/// <summary>
		/// The UserId GUID/UUID for the user status being return
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// The full name of the user for the status being returned
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// The current action/status type for the user
		/// </summary>
		public ActionTypes ActionType { get; set; }

		/// <summary>
		/// The current staffing level (state) type for the user
		/// </summary>
		public UserStateTypes StateType { get; set; }

		/// <summary>
		/// The timestamp of the last state/staffing level. This is converted UTC to the departments, or users, TimeZone.
		/// </summary>
		public DateTime StateTimestamp { get; set; }

		/// <summary>
		/// The current action/status destination id for the user
		/// </summary>
		public string DestinationId { get; set; }

		/// <summary>
		/// The current action/status destination name for the user
		/// </summary>
		public string DestinationName { get; set; }

		/// <summary>
		/// The timestamp of the last action. This is converted UTC to the departments, or users, TimeZone.
		/// </summary>
		public DateTime Timestamp { get; set; }
	}

	internal class StatusInput
	{
		/// <summary>
		/// UserId (GUID/UUID) of the User to set. This field will be ignored if the input is used on a 
		/// function that is setting status for the current user.
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// The ActionType/Status of the user to set for the user.
		/// </summary>
		public ActionTypes ActionType { get; set; }

		public int RespondingTo { get; set; }

		public string Geolocation { get; set; }
	}

	public class ScoutCommand : ICommand<ScoutQueueItem>
	{
		private readonly IEmailService _emailService;
		private readonly IJobsService _jobsService;

		public ScoutCommand(IEmailService emailService, IJobsService jobsService)
		{
			_emailService = emailService;
			_jobsService = jobsService;
			Continue = true;
		}

		public bool Continue { get; set; }

		public void Run(ScoutQueueItem item)
		{
			var client = new RestClient(Config.SystemBehaviorConfig.ResgridApiBaseUrl);

			var setStatusRequest = new RestRequest("api/v2/Status/SetCurrentStatus", Method.POST);
			var getStatusRequest = new RestRequest("api/v2/Status/GetCurrentStatus", Method.GET);

			var rnd = new Random();
			var type = (ActionTypes)rnd.Next(0, 3);

			var statusInput = new StatusInput();
			statusInput.ActionType = type;
			setStatusRequest.AddObject(statusInput);

			var setStatusResponse = client.Execute(setStatusRequest);
			var getStatusResponse = client.Execute<StatusResult>(getStatusRequest);

			if (setStatusResponse.StatusCode != HttpStatusCode.Created)
			{
				var systemNotificaiton = new EmailNotification();
				systemNotificaiton.Body = "Scout Failure! Was not able to set a user status via the API. Please check and/or restart the Resgrid API Cloud Service instances and ensure Azure is running properly and not suffering from a service outtage.";
				systemNotificaiton.From = "systemcheck@resgrid.com";
				systemNotificaiton.Name = "Api Scout";
				systemNotificaiton.Subject = string.Format("[RGSYS] Api Scout Failure: {0}", DateTime.UtcNow);

				_emailService.Notify(systemNotificaiton);
				_jobsService.SetJobAsChecked(JobTypes.Scout);

				return;
			}

			if (string.IsNullOrWhiteSpace(getStatusResponse.Content) || getStatusResponse.Data == null || getStatusResponse.Data.ActionType != type)
			{
				var systemNotificaiton = new EmailNotification();
				systemNotificaiton.Body = "Scout Failure! Did not receive content back from the Resgrid API or did not recieve the correct ActionType back. Please check and/or restart the Resgrid API Cloud Service instances and ensure Azure is running properly and not suffering from a service outtage.";
				systemNotificaiton.From = "systemcheck@resgrid.com";
				systemNotificaiton.Name = "Api Scout";
				systemNotificaiton.Subject = string.Format("[RGSYS] Api Scout Failure: {0}", DateTime.UtcNow);

				_emailService.Notify(systemNotificaiton);
				_jobsService.SetJobAsChecked(JobTypes.Scout);

				return;
			}

			if (setStatusResponse.Content.Contains("Authorization has been denied for this request") || getStatusResponse.Content.Contains("Authorization has been denied for this request"))
			{
				var systemNotificaiton = new EmailNotification();
				systemNotificaiton.Body = "Scout Failure! Recieved an unauthroized response from the Resgrid API. Please check and/or restart the Resgrid API Cloud Service instances and ensure Azure is running properly and not suffering from a service outtage.";
				systemNotificaiton.From = "systemcheck@resgrid.com";
				systemNotificaiton.Name = "Api Scout";
				systemNotificaiton.Subject = string.Format("[RGSYS] Api Scout Failure: {0}", DateTime.UtcNow);

				_emailService.Notify(systemNotificaiton);
			}

			_jobsService.SetJobAsChecked(JobTypes.Scout);
		}
	}
}

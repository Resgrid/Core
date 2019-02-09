
using RabbitMQ.Client;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using System;

namespace Resgrid.Providers.Bus.Rabbit
{
	public class RabbitTopicProvider
	{
		public RabbitTopicProvider()
		{
			VerifyAndCreateClients();
		}

		public bool PersonnelStatusChanged(UserStatusEvent message)
		{
			return SendMessage(Topics.EventingTopic, new
			{
				Id = Guid.NewGuid(),
				Type = EventingTypes.PersonnelStatusUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.Status.DepartmentId,
				ItemId = message.Status.ActionLogId
			}.SerializeJson());
		}

		public bool PersonnelStaffingChanged(UserStaffingEvent message)
		{
			return SendMessage(Topics.EventingTopic, new
			{
				Id = Guid.NewGuid(),
				Type = EventingTypes.PersonnelStaffingUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Staffing.UserStateId
			}.SerializeJson());
		}

		public bool UnitStatusChanged(UnitStatusEvent message)
		{
			return SendMessage(Topics.EventingTopic, new
			{
				Id = Guid.NewGuid(),
				Type = EventingTypes.UnitStatusUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Status.UnitStateId
			}.SerializeJson());
		}

		public bool CallAdded(CallAddedEvent message)
		{
			return SendMessage(Topics.EventingTopic, new
			{
				Id = Guid.NewGuid(),
				Type = EventingTypes.CallsUpdated,
				TimeStamp = DateTime.UtcNow,
				DepartmentId = message.DepartmentId,
				ItemId = message.Call.CallId
			}.SerializeJson());
		}

		private static void VerifyAndCreateClients()
		{
			var factory = new ConnectionFactory() { HostName = "localhost" };
			using (var connection = factory.CreateConnection())
			{
				using (var channel = connection.CreateModel())
				{
					
				}
			}
		}

		private bool SendMessage(string topicName, string message)
		{
			VerifyAndCreateClients();

			return true;
		}
	}
}

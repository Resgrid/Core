using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using System.Threading;
using System;
using System.Threading.Tasks;
using Resgrid.Config;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Web.Services.Models;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus.Rabbit;
using Resgrid.Web.Eventing.Hubs.Models;
using Resgrid.Model.Events;
using Microsoft.AspNetCore.SignalR;
using Resgrid.Web.Eventing.Hubs;

namespace Resgrid.Web.Eventing
{
	public class Worker : IHostedService
	{
		private readonly IHubContext<EventingHub> _eventingHub;
		private readonly IHubContext<GeolocationHub> _geolocationHub;
		private readonly IServiceProvider _serviceProvider;

		public Worker(IServiceProvider serviceProvider, IHubContext<EventingHub> eventingHub, IHubContext<GeolocationHub> geolocationHub)
		{
			_serviceProvider = serviceProvider;
			_eventingHub = eventingHub;
			_geolocationHub = geolocationHub;
		}

		public async Task StartAsync(CancellationToken cancellationToken)
		{
			using var scope = _serviceProvider.CreateScope();

			var rabbitInboundEventProvider = scope.ServiceProvider.GetRequiredService<IRabbitInboundEventProvider>();

			rabbitInboundEventProvider.RegisterForEvents(PersonnelStatusUpdated, UnitStatusUpdated, CallsUpdated,
			PersonnelStaffingUpdated, CallAdded, CallClosed, PersonnelLocationUpdated, UnitLocationUpdated);

			await rabbitInboundEventProvider.Start();
		}

		public async Task PersonnelStatusUpdated(int departmentId, string id)
		{
			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("personnelStatusUpdated", id);
		}

		public async Task PersonnelStaffingUpdated(int departmentId, string id)
		{
			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("personnelStaffingUpdated", id);
		}

		public async Task UnitStatusUpdated(int departmentId, string id)
		{
			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("unitStatusUpdated", id);
		}

		public async Task CallsUpdated(int departmentId, string id)
		{
			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("callsUpdated", id);
		}

		public async Task DepartmentUpdated(int departmentId)
		{
			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("departmentUpdated");
		}

		public async Task CallAdded(int departmentId, string id)
		{
			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("callAdded", id);
		}

		public async Task CallClosed(int departmentId, string id)
		{
			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("callClosed", id);
		}

		public async Task PersonnelLocationUpdated(int departmentId, PersonnelLocationUpdatedEvent update)
		{
			var group = _geolocationHub.Clients.Group(departmentId.ToString());

			var location = new PersonnelLocationUpdate();
			location.DepartmentId = update.DepartmentId;
			location.UserId = update.UserId;
			location.Latitude = update.Latitude;
			location.Longitude = update.Longitude;
			location.RecordId = update.RecordId;

			if (group != null)
				await group.SendAsync("onPersonnelLocationUpdated", location);
		}

		public async Task UnitLocationUpdated(int departmentId, UnitLocationUpdatedEvent update)
		{
			var group = _geolocationHub.Clients.Group(departmentId.ToString());

			var location = new UnitLocationUpdate();
			location.DepartmentId = update.DepartmentId;
			location.UnitId = update.UnitId;
			location.Latitude = update.Latitude;
			location.Longitude = update.Longitude;
			location.RecordId = update.RecordId;

			if (group != null)
				await group.SendAsync("onUnitLocationUpdated", location);
		}

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}
}

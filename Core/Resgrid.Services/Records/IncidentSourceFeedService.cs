using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// RMS-3 source feeds for incident report prefill: the Incident Command key-time snapshot and the Call's
	/// contact/place snapshot. Both are read-once captures with provenance (RMS plan sections 4.2 and 4.3);
	/// neither holds a live reference into its source module, and a source that cannot be read yields
	/// nothing rather than an exception, because a feed outage must never block an officer from starting a
	/// report.
	/// </summary>
	public class IncidentSourceFeedService : IIncidentSourceFeedService
	{
		private readonly IIncidentReportingService _reporting;
		private readonly IIncidentCommandService _commands;
		private readonly IContactsService _contacts;
		private readonly IMappingService _mapping;

		public IncidentSourceFeedService(IIncidentReportingService reporting, IIncidentCommandService commands, IContactsService contacts, IMappingService mapping)
		{
			_reporting = reporting;
			_commands = commands;
			_contacts = contacts;
			_mapping = mapping;
		}

		public async Task<IncidentCommandKeyTimes> GetCommandKeyTimesAsync(int departmentId, int callId)
		{
			if (callId <= 0)
				return null;

			try
			{
				var command = await _commands.GetCommandForCallAsync(departmentId, callId);
				if (command == null || command.DepartmentId != departmentId)
					return null;

				var times = await _reporting.GetIncidentTimesReportAsync(departmentId, callId);
				if (times == null)
					return null;

				return new IncidentCommandKeyTimes
				{
					CallId = callId,
					IncidentCommandId = command.IncidentCommandId,
					EstablishedOn = times.CommandEstablishedOn,
					FirstResourceAssignedOn = times.FirstResourceAssignedOn,
					FirstBenchmarkCompletedOn = times.FirstBenchmarkCompletedOn,
					LastBenchmarkCompletedOn = times.LastBenchmarkCompletedOn,
					ClosedOn = times.CommandClosedOn,
					MutualAidResourceCount = times.MutualAidResourceCount,
					Benchmarks = (times.Benchmarks ?? new List<BenchmarkTime>())
						.Where(b => b != null && b.CompletedOn.HasValue)
						.OrderBy(b => b.CompletedOn)
						.Select(b => new IncidentCommandBenchmark { Name = b.Name, CompletedOn = b.CompletedOn })
						.ToList(),
					CapturedOn = DateTime.UtcNow
				};
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Command key times could not be read for call {callId}; the report starts without them.");
				return null;
			}
		}

		public async Task<IncidentPreplanSnapshot> GetPreplanSnapshotAsync(int departmentId, Call call)
		{
			var snapshot = new IncidentPreplanSnapshot { CallId = call?.CallId ?? 0, CapturedOn = DateTime.UtcNow };
			if (call == null || call.DepartmentId != departmentId)
				return snapshot;

			foreach (var link in (call.Contacts ?? new List<CallContact>()).Where(c => c != null && !string.IsNullOrWhiteSpace(c.ContactId)).OrderBy(c => c.CallContactType))
			{
				try
				{
					if (link.DepartmentId != 0 && link.DepartmentId != departmentId)
						continue;
					var contact = await _contacts.GetContactByIdAsync(link.ContactId);
					if (contact == null || contact.DepartmentId != departmentId)
						continue;

					var name = contact.ContactType == 1
						? contact.CompanyName
						: string.Join(" ", new[] { contact.FirstName, contact.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
					if (string.IsNullOrWhiteSpace(name))
						name = contact.OtherName;

					string category = contact.Category?.Name;
					if (category == null && !string.IsNullOrWhiteSpace(contact.ContactCategoryId))
						category = (await _contacts.GetContactCategoryByIdAsync(contact.ContactCategoryId))?.Name;

					snapshot.Contacts.Add(new IncidentPreplanContact
					{
						ContactId = contact.ContactId,
						DisplayName = string.IsNullOrWhiteSpace(name) ? contact.ContactId : name.Trim(),
						ContactType = contact.ContactType == 1 ? "Company" : "Person",
						CategoryName = category,
						Role = link.GetContactTypeName()
					});
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Contact {link.ContactId} could not be read for the call {call.CallId} preplan snapshot.");
				}
			}

			if (call.DestinationPoiId.HasValue && call.DestinationPoiId.Value > 0)
			{
				try
				{
					var poi = await _mapping.GetDestinationPOIByIdAsync(departmentId, call.DestinationPoiId.Value);
					if (poi != null)
					{
						var typeName = poi.Type?.Name;
						if (typeName == null && poi.PoiTypeId > 0)
						{
							var type = await _mapping.GetTypeByIdAsync(poi.PoiTypeId);
							typeName = type != null && type.DepartmentId == departmentId ? type.Name : null;
						}

						snapshot.Place = new IncidentPreplanPlace
						{
							PoiId = poi.PoiId,
							Name = poi.Name,
							TypeName = typeName,
							Address = poi.Address,
							Latitude = poi.Latitude,
							Longitude = poi.Longitude
						};
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Destination POI {call.DestinationPoiId} could not be read for the call {call.CallId} preplan snapshot.");
				}
			}

			return snapshot;
		}
	}
}

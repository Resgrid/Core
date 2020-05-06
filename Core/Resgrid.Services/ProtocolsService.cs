using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class ProtocolsService : IProtocolsService
	{
		private readonly IGenericDataRepository<DispatchProtocol> _dispatchProtocolRepository;
		private readonly IGenericDataRepository<DispatchProtocolAttachment> _dispatchProtocolAttachmentlRepository;

		public ProtocolsService(IGenericDataRepository<DispatchProtocol> dispatchProtocolRepository, IGenericDataRepository<DispatchProtocolAttachment> dispatchProtocolAttachmentlRepository)
		{
			_dispatchProtocolRepository = dispatchProtocolRepository;
			_dispatchProtocolAttachmentlRepository = dispatchProtocolAttachmentlRepository;
		}

		public List<DispatchProtocol> GetAllProtocolsForDepartment(int departmentId)
		{
			return _dispatchProtocolRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public DispatchProtocol SaveProtocol(DispatchProtocol protocol)
		{
			_dispatchProtocolRepository.SaveOrUpdate(protocol);

			return protocol;
		}

		public DispatchProtocol GetProcotolById(int id)
		{
			return _dispatchProtocolRepository.GetAll().FirstOrDefault(x => x.DispatchProtocolId == id);
		}

		public void DeleteProtocol(int id)
		{
			var procotol = GetProcotolById(id);
			_dispatchProtocolRepository.DeleteOnSubmit(procotol);
		}

		public List<DispatchProtocol> ProcessTriggers(List<DispatchProtocol> protocols, Call call)
		{
			if (protocols == null || call == null)
				return null;

			foreach (var protocol in protocols)
			{
				var activeTriggers = DetermineActiveTriggers(protocol, call);

				if (activeTriggers != null && activeTriggers.Any())
				{
					if (protocol.Questions != null && protocol.Questions.Any())
						protocol.State = ProtocolStates.Pending;
					else
						protocol.State = ProtocolStates.Active;
				}
				else
				{
					protocol.State = ProtocolStates.Inactive;
				}
			}

			return protocols;
		}

		public DispatchProtocolAttachment GetAttachmentById(int protocolAttachmentId)
		{
			return _dispatchProtocolAttachmentlRepository.GetAll().FirstOrDefault(x => x.DispatchProtocolAttachmentId == protocolAttachmentId);
		}

		public List<DispatchProtocolTrigger> DetermineActiveTriggers(DispatchProtocol protocol, Call call)
		{
			List<DispatchProtocolTrigger> triggers = new List<DispatchProtocolTrigger>();

			if (protocol == null || protocol.Triggers == null || !protocol.Triggers.Any())
				return null;

			if (call == null)
				return null;

			if (protocol.IsDisabled)
				return null;

			// Getting all null start and end triggers
			var nullStartEndTimeTriggers = from trigger in protocol.Triggers
										   where trigger.StartsOn == null && trigger.EndsOn == null
										   select trigger;

			// Null Start, not null End and End date valid
			var nullStartValidEndTimeTriggers = from trigger in protocol.Triggers
										   where trigger.StartsOn == null && trigger.EndsOn != null &&
										   trigger.EndsOn >= DateTime.UtcNow
										   select trigger;

			// Null End, not null Start and Start date valid
			var nullEndValidStartTimeTriggers = from trigger in protocol.Triggers
												where trigger.EndsOn == null && trigger.StartsOn != null &&
												trigger.StartsOn <= DateTime.UtcNow
												select trigger;

			// Start and End, valid dates
			var validStartEndTimeTriggers = from trigger in protocol.Triggers
								where trigger.StartsOn != null && trigger.EndsOn != null &&
								(trigger.StartsOn <= DateTime.UtcNow && trigger.EndsOn >= DateTime.UtcNow)
								select trigger;

			var validTriggers = new List<DispatchProtocolTrigger>();
			validTriggers.AddRange(nullStartEndTimeTriggers);
			validTriggers.AddRange(nullStartValidEndTimeTriggers);
			validTriggers.AddRange(nullEndValidStartTimeTriggers);
			validTriggers.AddRange(validStartEndTimeTriggers);

			if (validTriggers == null || !validTriggers.Any())
				return null;

			var priorityTriggers = validTriggers.Where(x => x.Type == (int)ProtocolTriggerTypes.CallPriorty && x.Priority == call.Priority);

			if (priorityTriggers != null && priorityTriggers.Any())
				triggers.AddRange(priorityTriggers);

			var typeTriggers = validTriggers.Where(x => x.Type == (int)ProtocolTriggerTypes.CallType && x.CallType == call.Type);

			if (typeTriggers != null && typeTriggers.Any())
				triggers.AddRange(typeTriggers);

			var typeAndPrioityTriggers = validTriggers.Where(x => x.Type == (int)ProtocolTriggerTypes.CallPriortyAndType && x.Priority == call.Priority && x.CallType == call.Type);

			if (typeAndPrioityTriggers != null && typeAndPrioityTriggers.Any())
				triggers.AddRange(typeAndPrioityTriggers);

			return triggers;
		}
	}
}

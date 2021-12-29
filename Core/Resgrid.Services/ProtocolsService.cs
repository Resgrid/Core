using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class ProtocolsService : IProtocolsService
	{
		private readonly IDispatchProtocolRepository _dispatchProtocolRepository;
		private readonly IDispatchProtocolAttachmentRepository _dispatchProtocolAttachmentRepository;
		private readonly IDispatchProtocolQuestionsRepository _dispatchProtocolQuestionsRepository;
		private readonly IDispatchProtocolTriggersRepository _dispatchProtocolTriggersRepository;
		private readonly IDispatchProtocolQuestionAnswersRepository _dispatchProtocolQuestionAnswersRepository;

		public ProtocolsService(IDispatchProtocolRepository dispatchProtocolRepository, IDispatchProtocolAttachmentRepository dispatchProtocolAttachmentRepository,
			IDispatchProtocolQuestionsRepository dispatchProtocolQuestionsRepository, IDispatchProtocolTriggersRepository dispatchProtocolTriggersRepository,
			IDispatchProtocolQuestionAnswersRepository dispatchProtocolQuestionAnswersRepository)
		{
			_dispatchProtocolRepository = dispatchProtocolRepository;
			_dispatchProtocolAttachmentRepository = dispatchProtocolAttachmentRepository;
			_dispatchProtocolQuestionsRepository = dispatchProtocolQuestionsRepository;
			_dispatchProtocolTriggersRepository = dispatchProtocolTriggersRepository;
			_dispatchProtocolQuestionAnswersRepository = dispatchProtocolQuestionAnswersRepository;
		}

		public async Task<List<DispatchProtocol>> GetAllProtocolsForDepartmentAsync(int departmentId)
		{
			var items = await _dispatchProtocolRepository.GetAllByDepartmentIdAsync(departmentId);

			foreach (var dispatchProtocol in items)
			{
				dispatchProtocol.Attachments = (await _dispatchProtocolAttachmentRepository.GetDispatchProtocolAttachmentByProtocolIdAsync(dispatchProtocol.DispatchProtocolId)).ToList();
				dispatchProtocol.Questions = (await _dispatchProtocolQuestionsRepository.GetDispatchProtocolQuestionsByProtocolIdAsync(dispatchProtocol.DispatchProtocolId)).ToList();
				dispatchProtocol.Triggers = (await _dispatchProtocolTriggersRepository.GetDispatchProtocolTriggersByProtocolIdAsync(dispatchProtocol.DispatchProtocolId)).ToList();
			}

			return items.ToList();
		}

		public async Task<DispatchProtocol> SaveProtocolAsync(DispatchProtocol protocol, CancellationToken cancellationToken = default(CancellationToken))
		{
			var saved = await _dispatchProtocolRepository.SaveOrUpdateAsync(protocol, cancellationToken);

			if (saved.Questions != null)
			{
				foreach (var q in saved.Questions)
				{
					if (q.Answers != null)
					{
						foreach (var a in q.Answers)
						{
							if (a.DispatchProtocolQuestionAnswerId <= 0)
							{
								a.DispatchProtocolQuestionId = q.DispatchProtocolQuestionId;
								await _dispatchProtocolQuestionAnswersRepository.SaveOrUpdateAsync(a, cancellationToken, true);
							}
						}	
					}	
				}
			}

			return saved;
		}

		public async Task<DispatchProtocol> GetProtocolByIdAsync(int id)
		{
			var protocol = await _dispatchProtocolRepository.GetDispatchProtocolByIdAsync(id);

			if (protocol != null)
			{
				var attachments = await _dispatchProtocolAttachmentRepository.GetDispatchProtocolAttachmentByProtocolIdAsync(id);
				if (attachments != null && attachments.Any())
					protocol.Attachments = attachments.ToList();
				else
					protocol.Attachments = new List<DispatchProtocolAttachment>();

				var questions = await _dispatchProtocolQuestionsRepository.GetDispatchProtocolQuestionsByProtocolIdAsync(id);
				if (questions != null && questions.Any())
					protocol.Questions = questions.ToList();
				else
					protocol.Questions = new List<DispatchProtocolQuestion>();

				var triggers = await _dispatchProtocolTriggersRepository.GetDispatchProtocolTriggersByProtocolIdAsync(id);
				if (triggers != null && triggers.Any())
					protocol.Triggers = triggers.ToList();
				else
					protocol.Triggers = new List<DispatchProtocolTrigger>();
			}

			return protocol;
		}

		public async Task<bool> DeleteProtocol(int id, CancellationToken cancellationToken = default(CancellationToken))
		{
			var procotol = await GetProtocolByIdAsync(id);
			return await _dispatchProtocolRepository.DeleteAsync(procotol, cancellationToken);
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

		public async Task<DispatchProtocolAttachment> GetAttachmentByIdAsync(int protocolAttachmentId)
		{
			return await _dispatchProtocolAttachmentRepository.GetByIdAsync(protocolAttachmentId);
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

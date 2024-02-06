// Ignore Spelling: Workshift

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Repositories.DataRepository;

namespace Resgrid.Services
{
	public class WorkShiftsService : IWorkShiftsService
	{
		private readonly IWorkshiftsRepository _workshiftsRepository;
		private readonly IWorkshiftDaysRepository _workshiftDaysRepository;
		private readonly IWorkshiftEntitysRepository _workshiftEntitysRepository;
		private readonly IWorkshiftFillsRepository _workshiftFillsRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUnitsService _unitsService;
		private readonly IEventAggregator _eventAggregator;

		public WorkShiftsService(IWorkshiftsRepository workshiftsRepository, IWorkshiftDaysRepository workshiftDaysRepository,
			IWorkshiftEntitysRepository workshiftEntitysRepository, IWorkshiftFillsRepository workshiftFillsRepository,
			IDepartmentsService departmentsService, IUnitsService unitsService, IEventAggregator eventAggregator)
		{
			_workshiftsRepository = workshiftsRepository;
			_workshiftDaysRepository = workshiftDaysRepository;
			_workshiftEntitysRepository = workshiftEntitysRepository;
			_workshiftFillsRepository = workshiftFillsRepository;
			_departmentsService = departmentsService;
			_unitsService = unitsService;
			_eventAggregator = eventAggregator;
		}

		public async Task<Workshift> AddWorkshiftAsync(Workshift workshift, CancellationToken cancellationToken = default(CancellationToken))
		{
			workshift.AddedOn = DateTime.UtcNow;
			workshift.Start = workshift.Start.ToUniversalTime();
			workshift.End = workshift.End.ToUniversalTime();

			var savedWorkItem = await _workshiftsRepository.SaveOrUpdateAsync(workshift, cancellationToken, true);

			if (workshift.Entities != null && workshift.Entities.Any())
			{
				foreach (var entity in workshift.Entities)
				{
					entity.WorkshiftId = savedWorkItem.WorkshiftId;
					var savedEntity = await _workshiftEntitysRepository.SaveOrUpdateAsync(entity, cancellationToken, true);
				}
			}

			savedWorkItem.Days = new List<WorkshiftDay>();
			var totalDays = (workshift.End - workshift.Start).TotalDays;
			for (int i = 0; i < totalDays; i++)
			{
				var day = new WorkshiftDay();
				day.WorkshiftId = savedWorkItem.WorkshiftId;
				day.Day = workshift.Start.AddDays(i);

				var savedDay = await _workshiftDaysRepository.SaveOrUpdateAsync(day, cancellationToken, true);
				savedWorkItem.Days.Add(savedDay);
			}

			return savedWorkItem;
		}

		public async Task<Workshift> EditWorkshiftAsync(Workshift workshift, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken))
		{
			var originalWorkshift = await GetWorkshiftByIdAsync(workshift.WorkshiftId);

			workshift.Start = workshift.Start.ToUniversalTime();
			workshift.End = workshift.End.ToUniversalTime();

			var savedWorkItem = await _workshiftsRepository.SaveOrUpdateAsync(workshift, cancellationToken, true);

			// Clean up any old entities
			if (originalWorkshift != null && originalWorkshift.Entities != null && originalWorkshift.Entities.Any())
			{
				foreach (var entity in originalWorkshift.Entities)
				{
					await _workshiftEntitysRepository.DeleteAsync(entity, cancellationToken);
				}
			}
			
			if (workshift.Entities != null && workshift.Entities.Any())
			{
				foreach (var entity in workshift.Entities)
				{
					entity.WorkshiftId = savedWorkItem.WorkshiftId;
					var savedEntity = await _workshiftEntitysRepository.SaveOrUpdateAsync(entity, cancellationToken, true);
				}
			}

			// Clean up any old days
			if (originalWorkshift != null && originalWorkshift.Days != null && originalWorkshift.Days.Any())
			{
				foreach (var day in originalWorkshift.Days)
				{
					await _workshiftDaysRepository.DeleteAsync(day, cancellationToken);
				}
			}

			savedWorkItem.Days = new List<WorkshiftDay>();
			var totalDays = (workshift.End - workshift.Start).TotalDays;
			for (int i = 0; i < totalDays; i++)
			{
				var day = new WorkshiftDay();
				day.WorkshiftId = savedWorkItem.WorkshiftId;
				day.Day = workshift.Start.AddDays(i);

				var savedDay = await _workshiftDaysRepository.SaveOrUpdateAsync(day, cancellationToken, true);
				savedWorkItem.Days.Add(savedDay);
			}

			var auditEvent = new AuditEvent();
			auditEvent.Before = originalWorkshift.CloneJsonToString();
			auditEvent.DepartmentId = originalWorkshift.DepartmentId;
			auditEvent.UserId = userId;
			auditEvent.Type = AuditLogTypes.UpdateStaticShift;
			auditEvent.Successful = true;
			auditEvent.IpAddress = ipAddress;
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = userAgent;
			auditEvent.After = workshift.CloneJsonToString();
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			return savedWorkItem;
		}

		public async Task<List<Workshift>> GetAllWorkshiftsByDepartmentAsync(int departmentId)
		{
			var items = await _workshiftsRepository.GetAllWorkshiftAndDaysByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<Workshift>();
		}

		public async Task<Workshift> GetWorkshiftByIdAsync(string workshiftId)
		{
			var workshift = await _workshiftsRepository.GetWorkshiftByIdAsync(workshiftId);

			if (workshift != null)
			{
				workshift.Entities = (await _workshiftEntitysRepository.GetWorkshiftEntitiesByWorkshiftIdAsync(workshiftId))?.ToList();
				workshift.Fills = (await _workshiftFillsRepository.GetWorkshiftFillsByWorkshiftIdAsync(workshiftId))?.ToList();
			}

			return workshift;
		}

		public async Task<Workshift> DeleteWorkshiftByIdAsync(string workshiftId, string userId, int departmentId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken))
		{
			var workshift = await _workshiftsRepository.GetWorkshiftByIdAsync(workshiftId);

			if (workshift != null)
			{
				var auditEvent = new AuditEvent();
				auditEvent.Before = workshift.CloneJsonToString();
				auditEvent.DepartmentId = departmentId;
				auditEvent.UserId = userId;
				auditEvent.Type = AuditLogTypes.DeleteStaticShift;
				auditEvent.Successful = true;
				auditEvent.IpAddress = ipAddress;
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = userAgent;

				workshift.DeletedOn = DateTime.UtcNow;
				workshift.DeletedById = userId;
				var workshift2 = await _workshiftsRepository.SaveOrUpdateAsync(workshift, cancellationToken, true);

				auditEvent.After = workshift2.CloneJsonToString();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);
			}

			return workshift;
		}

		public async Task<WorkshiftDay> GetWorkshiftDayByIdAsync(string workshiftDayId)
		{
			return await _workshiftDaysRepository.GetByIdAsync(workshiftDayId);
		}
	}
}

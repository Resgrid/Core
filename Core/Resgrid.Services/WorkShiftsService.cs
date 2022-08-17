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
	public class WorkShiftsService : IWorkShiftsService
	{
		private readonly IWorkshiftsRepository _workshiftsRepository;
		private readonly IWorkshiftDaysRepository _workshiftDaysRepository;
		private readonly IWorkshiftEntitysRepository _workshiftEntitysRepository;
		private readonly IWorkshiftFillsRepository _workshiftFillsRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUnitsService _unitsService;

		public WorkShiftsService(IWorkshiftsRepository workshiftsRepository, IWorkshiftDaysRepository workshiftDaysRepository,
			IWorkshiftEntitysRepository workshiftEntitysRepository, IWorkshiftFillsRepository workshiftFillsRepository,
			IDepartmentsService departmentsService, IUnitsService unitsService)
		{
			_workshiftsRepository = workshiftsRepository;
			_workshiftDaysRepository = workshiftDaysRepository;
			_workshiftEntitysRepository = workshiftEntitysRepository;
			_workshiftFillsRepository = workshiftFillsRepository;
			_departmentsService = departmentsService;
			_unitsService = unitsService;
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

		public async Task<WorkshiftDay> GetWorkshiftDayByIdAsync(string workshiftDayId)
		{
			return await _workshiftDaysRepository.GetByIdAsync(workshiftDayId);
		}
	}
}

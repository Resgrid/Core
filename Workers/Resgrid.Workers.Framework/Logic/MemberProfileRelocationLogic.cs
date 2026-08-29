using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Drains the legacy member-profile relocation backlog (ADP plan section 5.1): identification
	/// numbers and addresses that still live on the global UserProfiles row and have to move onto the
	/// department-scoped DepartmentMemberSensitiveData row.
	///
	/// M0134 moves everything it safely can in SQL at deploy time, but it cannot touch a department
	/// already enrolled in ADP — plaintext written into an enrolled row would poison it. This sweep
	/// covers exactly the remainder: enrolled departments (the move goes through the ADP write path
	/// and is enveloped as it lands) and members who joined after the migration ran. Once the backlog
	/// reads zero the sweep costs one indexed query, and zero is the precondition for the contract
	/// migration that drops the legacy columns.
	///
	/// Departments mid-migration are deliberately skipped: their relocation runs as the first step of
	/// the encryption night instead, inside the department's own window and lock.
	/// </summary>
	public sealed class MemberProfileRelocationLogic
	{
		/// <summary>
		/// Bound on one pass so a large backlog is drained over several sweeps rather than in one
		/// long-running job. Whatever is deferred is named in the summary — a silent cap would read
		/// as "the backlog is empty" when it is not.
		/// </summary>
		private const int MaxDepartmentsPerPass = 25;

		private readonly IMemberProfileRelocationService _relocationService;
		private readonly IDepartmentDataProtectionService _protectionService;

		public MemberProfileRelocationLogic()
			: this(
				Bootstrapper.GetKernel().Resolve<IMemberProfileRelocationService>(),
				Bootstrapper.GetKernel().Resolve<IDepartmentDataProtectionService>())
		{
		}

		public MemberProfileRelocationLogic(IMemberProfileRelocationService relocationService,
			IDepartmentDataProtectionService protectionService)
		{
			_relocationService = relocationService ?? throw new ArgumentNullException(nameof(relocationService));
			_protectionService = protectionService ?? throw new ArgumentNullException(nameof(protectionService));
		}

		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			try
			{
				var outstanding = await _relocationService.GetDepartmentIdsWithOutstandingDataAsync();
				if (outstanding == null || outstanding.Count == 0)
					return new Tuple<bool, string>(true, "no outstanding member profile relocations");

				var summary = new List<string>();
				var deferred = 0;
				var processed = 0;

				foreach (var departmentId in outstanding)
				{
					cancellationToken.ThrowIfCancellationRequested();

					if (processed >= MaxDepartmentsPerPass)
					{
						deferred++;
						continue;
					}

					// Only steady states. A department mid-enrollment, mid-rotation or mid-offboarding
					// is moving its whole corpus already; relocating it from here would race that run,
					// so the encryption night does it as its own first step instead.
					var state = await _protectionService.GetStateAsync(departmentId);
					if (state != DepartmentDataProtectionState.Disabled && state != DepartmentDataProtectionState.Enabled)
					{
						deferred++;
						continue;
					}

					processed++;
					var result = await _relocationService.RelocateDepartmentAsync(departmentId, cancellationToken);
					if (result.DidWork)
						summary.Add(result.ToString());
				}

				if (deferred > 0)
					summary.Add($"{deferred} department(s) deferred to a later pass");

				return new Tuple<bool, string>(true, summary.Count > 0
					? string.Join("; ", summary)
					: "no member profile relocations were eligible this pass");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new Tuple<bool, string>(false, ex.ToString());
			}
		}
	}
}

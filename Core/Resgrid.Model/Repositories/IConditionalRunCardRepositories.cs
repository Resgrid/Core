using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Decision graph nodes of run card alarm levels (conditional run cards).</summary>
	public interface IRunCardDecisionNodesRepository : IRepository<RunCardDecisionNode>
	{
		Task<IEnumerable<RunCardDecisionNode>> GetNodesByRunCardIdAsync(int runCardId);
	}

	/// <summary>A department's reusable response packages.</summary>
	public interface IResponsePackagesRepository : IRepository<ResponsePackage>
	{
	}

	/// <summary>Rows of response packages.</summary>
	public interface IResponsePackageRowsRepository : IRepository<ResponsePackageRow>
	{
		Task<IEnumerable<ResponsePackageRow>> GetRowsByDepartmentIdAsync(int departmentId);
	}

	/// <summary>"Any of" targets of package rows and condition operands. GetAllByDepartmentIdAsync returns every target of a department.</summary>
	public interface IRunCardTargetsRepository : IRepository<RunCardTarget>
	{
	}

	/// <summary>Pinned run card decisions per call (append-only).</summary>
	public interface IRunCardDecisionsRepository : IRepository<RunCardDecision>
	{
		Task<IEnumerable<RunCardDecision>> GetDecisionsByCallIdAsync(int callId);
	}

	/// <summary>Required resources still awaited by calls.</summary>
	public interface ICallOutstandingRequirementsRepository : IRepository<CallOutstandingRequirement>
	{
		Task<IEnumerable<CallOutstandingRequirement>> GetOpenByDepartmentIdAsync(int departmentId);

		Task<IEnumerable<CallOutstandingRequirement>> GetByCallIdAsync(int callId);

		/// <summary>
		/// Claims an open, unclaimed requirement for automatic dispatch of one unit. Only one caller sees the row change,
		/// so two freed units cannot both be sent to it. Returns false when it was already claimed or resolved.
		/// </summary>
		Task<bool> TryClaimAsync(int callOutstandingRequirementId, int departmentId, int unitId, DateTime claimedOn, CancellationToken cancellationToken = default);

		/// <summary>Gives back this unit's claim (the dispatch failed), so the requirement is alerted again.</summary>
		Task<bool> ReleaseClaimAsync(int callOutstandingRequirementId, int departmentId, int unitId, CancellationToken cancellationToken = default);
	}
}

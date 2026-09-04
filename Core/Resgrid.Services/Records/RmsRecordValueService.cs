using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// RMS-1 value seam (plan section 5.9.1): the only caller of the details repository. Every write passes
	/// <see cref="PrepareForStorage"/>, which is where Protected Data enrollment will clone the row into its
	/// protected persistence shape; today it only enforces the inert contract so nothing can pre-empt it.
	/// </summary>
	public class RmsRecordValueService : IRmsRecordValueService
	{
		private readonly IRmsOperationalRecordDetailsRepository _details;

		public RmsRecordValueService(IRmsOperationalRecordDetailsRepository details)
		{
			_details = details;
		}

		public Task<RmsOperationalRecordDetail> GetDraftAsync(int departmentId, string recordId)
		{
			return _details.GetDraftAsync(departmentId, recordId);
		}

		public Task<RmsOperationalRecordDetail> GetByRevisionAsync(int departmentId, string recordId, string revisionId)
		{
			return _details.GetByRevisionAsync(departmentId, recordId, revisionId);
		}

		public Task<IEnumerable<RmsOperationalRecordDetail>> GetDraftsForRecordsAsync(int departmentId, IEnumerable<string> recordIds)
		{
			return _details.GetDraftsForRecordsAsync(departmentId, recordIds);
		}

		public Task<RmsOperationalRecordDetail> InsertAsync(RmsOperationalRecordDetail details, CancellationToken cancellationToken = default)
		{
			PrepareForStorage(details);
			return _details.InsertAsync(details, cancellationToken, true);
		}

		public Task<RmsOperationalRecordDetail> UpdateAsync(RmsOperationalRecordDetail details, CancellationToken cancellationToken = default)
		{
			PrepareForStorage(details);
			return _details.UpdateAsync(details, cancellationToken, true);
		}

		public Task<RmsOperationalRecordDetail> SaveOrUpdateAsync(RmsOperationalRecordDetail details, CancellationToken cancellationToken = default)
		{
			PrepareForStorage(details);
			return _details.SaveOrUpdateAsync(details, cancellationToken, true);
		}

		/// <summary>
		/// The inert protection contract. An unprotected row carries no envelope and catalog version 0; a row
		/// marked protected must carry its envelope (the typed columns are what enrollment nulls out). Anything
		/// else is a caller writing around the enrollment path and is refused.
		/// </summary>
		public static void PrepareForStorage(RmsOperationalRecordDetail details)
		{
			if (details == null)
				throw new ArgumentNullException(nameof(details));

			if (!details.IsProtected)
			{
				if (!string.IsNullOrEmpty(details.ProtectedEnvelope))
					throw new InvalidOperationException("A protected envelope was supplied for a record detail row that is not marked protected; envelopes are written only through Protected Data enrollment.");

				details.ProtectedCatalogVersion = 0;
				return;
			}

			if (string.IsNullOrEmpty(details.ProtectedEnvelope))
				throw new InvalidOperationException("A record detail row marked protected must carry its envelope.");
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class AffiliateService : IAffiliateService
	{
		private readonly IAffiliateRepository _affiliateRepository;
		private readonly IEmailService _emailService;

		public AffiliateService(IAffiliateRepository affiliateRepository, IEmailService emailService)
		{
			_affiliateRepository = affiliateRepository;
			_emailService = emailService;
		}

		public async Task<List<Affiliate>> GetAllAsync()
		{
			var affiliates = await _affiliateRepository.GetAllAsync();
			return affiliates.ToList();
		}

		public async Task<Affiliate> GetAffiliateByIdAsync(int affiliateId)
		{
			var affiliates = await _affiliateRepository.GetAllAsync();

			return affiliates.FirstOrDefault(x => x.AffiliateId == affiliateId);
		}

		public async Task<Affiliate> GetAffiliateByCodeAsync(string affiliateCode)
		{
			var affiliates = await _affiliateRepository.GetAllAsync();

			return affiliates.FirstOrDefault(x => x.AffiliateCode == affiliateCode);
		}

		public async Task<Affiliate> GetActiveAffiliateByCodeAsync(string affiliateCode)
		{
			var affiliates = await _affiliateRepository.GetAllAsync();

			return affiliates.FirstOrDefault(x => x.AffiliateCode == affiliateCode && x.Active);
		}

		public async Task<Affiliate> SaveAffiliateAsync(Affiliate affiliate, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _affiliateRepository.SaveOrUpdateAsync(affiliate, cancellationToken);
		}

		public async Task<Affiliate> CreateNewAffiliateAsync(Affiliate affiliate, CancellationToken cancellationToken = default(CancellationToken))
		{
			affiliate.AffiliateCode = RandomGenerator.GenerateRandomString(6, 6, false, false, true,
				true, true, false, null, await GetAllAffiliateCodesAsync());
			affiliate.CreatedOn = DateTime.UtcNow;
			affiliate.Approved = false;
			affiliate.Active = false;

			return await SaveAffiliateAsync(affiliate, cancellationToken);
		}

		public async Task<HashSet<string>> GetAllAffiliateCodesAsync()
		{
			var affiliates = await _affiliateRepository.GetAllAsync();
			return new HashSet<string>(affiliates.Select(x => x.AffiliateCode));
		}
	}
}

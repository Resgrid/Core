using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class AffiliateService : IAffiliateService
	{
		private readonly IGenericDataRepository<Affiliate> _affiliateRepository;
		private readonly IEmailService _emailService;

		public AffiliateService(IGenericDataRepository<Affiliate> affiliateRepository, IEmailService emailService)
		{
			_affiliateRepository = affiliateRepository;
			_emailService = emailService;
		}

		public List<Affiliate> GetAll()
		{
			return _affiliateRepository.GetAll().ToList();
		}

		public Affiliate GetAffiliateById(int affiliateId)
		{
			return _affiliateRepository.GetAll().FirstOrDefault(x => x.AffiliateId == affiliateId);
		}

		public Affiliate GetAffiliateByCode(string affiliateCode)
		{
			return _affiliateRepository.GetAll().FirstOrDefault(x => x.AffiliateCode == affiliateCode);
		}

		public Affiliate GetActiveAffiliateByCode(string affiliateCode)
		{
			return _affiliateRepository.GetAll().FirstOrDefault(x => x.AffiliateCode == affiliateCode && x.Active);
		}

		public Affiliate SaveAffiliate(Affiliate affiliate)
		{
			_affiliateRepository.SaveOrUpdate(affiliate);

			return affiliate;
		}

		public Affiliate CreateNewAffiliate(Affiliate affiliate)
		{
			affiliate.AffiliateCode = RandomGenerator.GenerateRandomString(6, 6, false, false, true, true, true, false, null, GetAllAffiliateCodes());
			affiliate.CreatedOn = DateTime.UtcNow;
			affiliate.Approved = false;
			affiliate.Active = false;

			return SaveAffiliate(affiliate);
		}

		public HashSet<string> GetAllAffiliateCodes()
		{
			return new HashSet<string>(_affiliateRepository.GetAll().Select(x => x.AffiliateCode));
		}

		public void SendAffiliateApplicationRejection(Affiliate affiliate)
		{
			_emailService.SendAffiliateRejectionEmail(affiliate);
		}

		public void SendAffiliateApplicationApproval(Affiliate affiliate)
		{
			_emailService.SendAffiliateApprovalEmail(affiliate);
		}
	}
}
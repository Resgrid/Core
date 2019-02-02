using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IAffiliateService
	{
		Affiliate GetAffiliateById(int affiliateId);
		Affiliate GetAffiliateByCode(string affiliateCode);
		Affiliate SaveAffiliate(Affiliate affiliate);
		HashSet<string> GetAllAffiliateCodes();
		Affiliate CreateNewAffiliate(Affiliate affiliate);
		List<Affiliate> GetAll();
		Affiliate GetActiveAffiliateByCode(string affiliateCode);
		void SendAffiliateApplicationRejection(Affiliate affiliate);
		void SendAffiliateApplicationApproval(Affiliate affiliate);
	}
}
namespace Resgrid.Model.Services
{
	public interface IGeoService
	{
		double GetPersonnelEtaInSeconds(ActionLog log);
		double GetEtaInSeconds(string start, string destination);
	}
}
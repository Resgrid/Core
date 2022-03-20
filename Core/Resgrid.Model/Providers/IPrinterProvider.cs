using Resgrid.Model.Providers.Models.PrintNode;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IPrinterProvider
	{
		Task<Whoami> Whoami(string apiKey);
		Task<List<Computer>> GetComputers(string apiKey);
		Task<List<Printer>> GetPrinters(string apiKey);
		Task<List<PrintJob>> GetPrintJobs(string apiKey);
		Task<bool> SubmitPrintJob(string apiKey, int printerId, string title, string url);
	}
}

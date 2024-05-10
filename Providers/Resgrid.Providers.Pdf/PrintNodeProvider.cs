using Newtonsoft.Json;
using Resgrid.Model.Providers;
using Resgrid.Model.Providers.Models.PrintNode;
using RestSharp;
using RestSharp.Authenticators;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Resgrid.Providers.PdfProvider
{
	public class PrintNodeProvider: IPrinterProvider
	{
		public async Task<Whoami> Whoami(string apiKey)
		{
			var options = new RestClientOptions(Config.PrintConfig.PrintNodeBaseUrl)
			{
				Authenticator = new HttpBasicAuthenticator(apiKey, "")
			};
			var client = new RestClient(options);

			var request = new RestRequest("/whoami", Method.Get);

			var response = await client.ExecuteAsync(request);

			if (response.StatusCode == HttpStatusCode.OK)
				return JsonConvert.DeserializeObject<Whoami>(response.Content);

			return null;
		}

		public async Task<List<Computer>> GetComputers(string apiKey)
		{
			var options = new RestClientOptions(Config.PrintConfig.PrintNodeBaseUrl)
			{
				Authenticator = new HttpBasicAuthenticator(apiKey, "")
			};
			var client = new RestClient(options);

			var request = new RestRequest("/computers", Method.Get);

			var response = await client.ExecuteAsync(request);

			if (response.StatusCode == HttpStatusCode.OK)
				return JsonConvert.DeserializeObject<List<Computer>>(response.Content);

			return null;
		}

		public async Task<List<Printer>> GetPrinters(string apiKey)
		{
			var options = new RestClientOptions(Config.PrintConfig.PrintNodeBaseUrl)
			{
				Authenticator = new HttpBasicAuthenticator(apiKey, "")
			};
			var client = new RestClient(options);

			var request = new RestRequest("/printers", Method.Get);

			var response = await client.ExecuteAsync(request);

			if (response.StatusCode == HttpStatusCode.OK)
				return JsonConvert.DeserializeObject<List<Printer>>(response.Content);

			return null;
		}

		public async Task<List<PrintJob>> GetPrintJobs(string apiKey)
		{
			var options = new RestClientOptions(Config.PrintConfig.PrintNodeBaseUrl)
			{
				Authenticator = new HttpBasicAuthenticator(apiKey, "")
			};
			var client = new RestClient(options);

			var request = new RestRequest("/printjobs", Method.Get);

			var response = await client.ExecuteAsync(request);

			if (response.StatusCode == HttpStatusCode.OK)
				return JsonConvert.DeserializeObject<List<PrintJob>>(response.Content);

			return null;

			//return "";//Get("PrintJobs");
		}

		public async Task<bool> SubmitPrintJob(string apiKey, int printerId, string title, string url)
		{
			var options = new RestClientOptions(Config.PrintConfig.PrintNodeBaseUrl)
			{
				Authenticator = new HttpBasicAuthenticator(apiKey, "")
			};
			var client = new RestClient(options);

			var request = new RestRequest("/printjobs", Method.Post);
			//request.AddHeader("Accept", "application/json");
			//request.AddHeader("Content-Type", "application/json; charset=utf-8");
			request.AddHeader("Content-type", "application/json");

			var body = JsonConvert.SerializeObject(new
			{
				printerId = printerId,
				title = title,
				contentType = "pdf_uri",
				content = url,
				source = "Resgrid Print Job"
			});

			var param = new BodyParameter("application/json", body, "application/json; charset=utf-8");
			request.AddParameter(param);

			//request.AddJsonBody(new
			//{
			//	printerId = printerId,
			//	title = title,
			//	contentType = "raw_uri",
			//	content = url,
			//	source = "Resgrid Print Job"
			//});

			var response = await client.ExecuteAsync(request);


			if (response.StatusCode == HttpStatusCode.Created)
				return true;

			return false;
		}

	}
}

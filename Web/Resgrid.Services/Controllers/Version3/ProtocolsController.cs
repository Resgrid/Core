using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Resgrid.Model.Services;
using System.Web.Http;
using Resgrid.Web.Services.Controllers.Version3.Models.Protocols;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against dispatch protocols that are established in a department
	/// </summary>
	public class ProtocolsController : V3AuthenticatedApiControllerbase
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IProtocolsService _protocolsService;

		public ProtocolsController(IDepartmentsService departmentsService, IProtocolsService protocolsService)
		{
			_departmentsService = departmentsService;
			_protocolsService = protocolsService;
		}

		/// <summary>
		/// Get's all the protocols for a department
		/// </summary>
		/// <returns>List of ProtocolResult objects.</returns>
		[AcceptVerbs("GET")]
		public List<ProtocolResult> GetAllProtocols()
		{
			var results = new List<ProtocolResult>();
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);


			var protocols = _protocolsService.GetAllProtocolsForDepartment(DepartmentId);

			foreach (var p in protocols)
			{
				results.Add(ProtocolResult.Convert(p));
			}

			return results;
		}

		public HttpResponseMessage Options()
		{
			var response = new HttpResponseMessage();
			response.StatusCode = HttpStatusCode.OK;
			response.Headers.Add("Access-Control-Allow-Origin", "*");
			response.Headers.Add("Access-Control-Request-Headers", "*");
			response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

			return response;
		}
	}
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using System.Linq;
using Resgrid.Model;
using Resgrid.Web.Services.Models.v4.Autofills;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Call Priorities, for example Low, Medium, High. Call Priorities can be system provided ones or custom for a department
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class AutofillsController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IAutofillsService _autofillsService;

		public AutofillsController(IAutofillsService autofillsService)
		{
			_autofillsService = autofillsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all the autofills in a department
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllAutofills")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<ActionResult<GetAutofillsResult>> GetAllAutofills()
		{
			var result = new GetAutofillsResult();

			var autofills = await _autofillsService.GetAllAutofillsForDepartmentAsync(DepartmentId);

			if (autofills != null && autofills.Any())
			{
				foreach (var a in autofills)
				{
					result.Data.Add(ConvertAutofillResultData(a));
				}

				result.Data = result.Data.OrderBy(x => x.Sort).ToList();

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets the autofills for a specific type
		/// </summary>
		/// <param name="type">Type to get</param>
		/// <returns></returns>
		[HttpGet("GetAutofillsForType")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<ActionResult<GetAutofillsResult>> GetAutofillsForType(int type)
		{
			var result = new GetAutofillsResult();

			var autofills = await _autofillsService.GetAllAutofillsForDepartmentByTypeAsync(DepartmentId, (AutofillTypes)type);

			if (autofills != null && autofills.Any())
			{
				foreach (var a in autofills)
				{
					result.Data.Add(ConvertAutofillResultData(a));
				}

				result.Data = result.Data.OrderBy(x => x.Sort).ToList();

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		public static AutofillResultData ConvertAutofillResultData(Autofill a)
		{
			var result = new AutofillResultData();

			result.AutofillId = a.AutofillId;
			result.DepartmentId = a.DepartmentId;
			result.Type = a.Type;
			result.Sort = a.Sort;
			result.Name = a.Name;
			result.Data = a.Data;
			result.AddedByUserId = a.AddedByUserId;
			result.AddedOn = a.AddedOn.ToString();

			return result;
		}
	}
}

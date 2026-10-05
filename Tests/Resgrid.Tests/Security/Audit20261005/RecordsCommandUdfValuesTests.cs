using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.UserDefinedFields;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05, 3.7 (endpoint part): v4 UserDefinedFields/Values (and the pre-filled schema) read or write the
	/// values of one entity, so they take that entity's own view or edit rule before per-field visibility applies. The
	/// shared FilterValuesVisibleToUserAsync helper applies ViewUdfFields and per-field Visibility for other entity reads.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class RecordsCommandUdfValuesTests
	{
		private const int Dept = 12;
		private const string Member = "member";
		private const string FieldId = "field-1";

		private Mock<IUserDefinedFieldsService> _udf;
		private Mock<Resgrid.Model.Services.IAuthorizationService> _authorization;
		private Mock<IDepartmentsService> _departments;
		private Mock<IContactsService> _contacts;
		private System.Diagnostics.Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_udf = new Mock<IUserDefinedFieldsService>();
			_udf.Setup(u => u.GetVisibleFieldsForActiveDefinitionAsync(Dept, It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(new List<UdfField> { new UdfField { UdfFieldId = FieldId, Name = "hazmat", IsEnabled = true } });
			_udf.Setup(u => u.GetFieldValuesForEntityAsync(Dept, It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, int type, string id) => new List<UdfFieldValue> { new UdfFieldValue { UdfFieldId = FieldId, EntityId = id, EntityType = type, Value = "Class 3" } });
			_udf.Setup(u => u.SaveFieldValuesForEntityAsync(Dept, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<List<UdfFieldValue>>(), Member, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new Dictionary<string, List<string>>());
			_authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			_departments = new Mock<IDepartmentsService>();
			_contacts = new Mock<IContactsService>();
			_activity = new System.Diagnostics.Activity(nameof(RecordsCommandUdfValuesTests)).Start();
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		private UserDefinedFieldsController Controller(params Claim[] extra)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, Member), new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()) }.Concat(extra), "test"))
			};
			http.Connection.RemoteIpAddress = IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return new UserDefinedFieldsController(_udf.Object, Mock.Of<IUdfRenderingService>(), Mock.Of<IEventAggregator>(), Mock.Of<IProtectedReadService>(),
				_authorization.Object, _departments.Object, _contacts.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		private static SaveUdfFieldValuesInput Save(UdfEntityType type, string id)
			=> new SaveUdfFieldValuesInput { EntityType = (int)type, EntityId = id, Values = new List<UdfFieldValueInput> { new UdfFieldValueInput { UdfFieldId = FieldId, Value = "Class 8" } } };

		[Test]
		public async Task Values_of_a_call_the_caller_cannot_view_are_refused()
		{
			var result = await Controller().GetFieldValues((int)UdfEntityType.Call, "42", CancellationToken.None);

			result.Result.Should().BeOfType<ForbidResult>();
			_udf.Verify(u => u.GetFieldValuesForEntityAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Values_of_a_visible_call_are_returned()
		{
			_authorization.Setup(a => a.CanUserViewCallAsync(Member, 42)).ReturnsAsync(true);

			var result = await Controller().GetFieldValues((int)UdfEntityType.Call, "42", CancellationToken.None);

			((UdfFieldValuesResult)((OkObjectResult)result.Result).Value).Data.Should().ContainSingle(v => v.Value == "Class 3");
		}

		[Test]
		public async Task Writing_a_calls_values_takes_the_call_edit_rule()
		{
			_authorization.Setup(a => a.CanUserViewCallAsync(Member, 42)).ReturnsAsync(true);

			(await Controller().SaveFieldValues(Save(UdfEntityType.Call, "42"), CancellationToken.None)).Should().BeOfType<ForbidResult>("viewing a call is not editing it");
			_udf.Verify(u => u.SaveFieldValuesForEntityAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<List<UdfFieldValue>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

			_authorization.Setup(a => a.CanUserEditCallAsync(Member, 42)).ReturnsAsync(true);
			(await Controller().SaveFieldValues(Save(UdfEntityType.Call, "42"), CancellationToken.None)).Should().BeOfType<OkResult>();
		}

		[Test]
		public async Task Writing_a_units_values_takes_the_unit_modify_rule()
		{
			_authorization.Setup(a => a.CanUserViewUnitAsync(Member, 5)).ReturnsAsync(true);
			(await Controller().SaveFieldValues(Save(UdfEntityType.Unit, "5"), CancellationToken.None)).Should().BeOfType<ForbidResult>();

			_authorization.Setup(a => a.CanUserModifyUnitAsync(Member, 5)).ReturnsAsync(true);
			(await Controller().SaveFieldValues(Save(UdfEntityType.Unit, "5"), CancellationToken.None)).Should().BeOfType<OkResult>();
		}

		[Test]
		public async Task A_person_outside_the_department_is_refused_even_when_the_matrix_fails_open()
		{
			_authorization.Setup(a => a.CanUserViewPersonViaMatrixAsync(It.IsAny<string>(), Member, Dept)).ReturnsAsync(true);

			(await Controller().GetFieldValues((int)UdfEntityType.Personnel, "stranger", CancellationToken.None)).Result.Should().BeOfType<ForbidResult>();

			_departments.Setup(d => d.GetDepartmentMemberAsync("colleague", Dept, It.IsAny<bool>())).ReturnsAsync(new DepartmentMember { UserId = "colleague", DepartmentId = Dept });
			(await Controller().GetFieldValues((int)UdfEntityType.Personnel, "colleague", CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
		}

		[Test]
		public async Task Writing_a_persons_values_takes_the_profile_edit_rule()
		{
			_departments.Setup(d => d.GetDepartmentMemberAsync("colleague", Dept, It.IsAny<bool>())).ReturnsAsync(new DepartmentMember { UserId = "colleague", DepartmentId = Dept });

			(await Controller().SaveFieldValues(Save(UdfEntityType.Personnel, "colleague"), CancellationToken.None)).Should().BeOfType<ForbidResult>();

			_authorization.Setup(a => a.CanUserEditProfileAsync(Member, Dept, "colleague")).ReturnsAsync(true);
			(await Controller().SaveFieldValues(Save(UdfEntityType.Personnel, "colleague"), CancellationToken.None)).Should().BeOfType<OkResult>();
		}

		[Test]
		public async Task A_contact_needs_the_department_and_the_contacts_claim()
		{
			_contacts.Setup(c => c.GetContactByIdAsync("foreign")).ReturnsAsync(new Contact { ContactId = "foreign", DepartmentId = 99 });
			_contacts.Setup(c => c.GetContactByIdAsync("ours")).ReturnsAsync(new Contact { ContactId = "ours", DepartmentId = Dept });
			var viewer = new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View);

			(await Controller(viewer).GetFieldValues((int)UdfEntityType.Contact, "foreign", CancellationToken.None)).Result.Should().BeOfType<ForbidResult>();
			(await Controller().GetFieldValues((int)UdfEntityType.Contact, "ours", CancellationToken.None)).Result.Should().BeOfType<ForbidResult>();
			(await Controller(viewer).GetFieldValues((int)UdfEntityType.Contact, "ours", CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
			(await Controller(viewer).SaveFieldValues(Save(UdfEntityType.Contact, "ours"), CancellationToken.None)).Should().BeOfType<ForbidResult>("viewing contacts is not updating them");
			(await Controller(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Update)).SaveFieldValues(Save(UdfEntityType.Contact, "ours"), CancellationToken.None))
				.Should().BeOfType<OkResult>();
		}

		[Test]
		public async Task Record_values_and_the_prefilled_schema_follow_the_same_entity_rule()
		{
			(await Controller().GetFieldValues((int)UdfEntityType.Record, "rec-1", CancellationToken.None)).Result.Should().BeOfType<ForbidResult>();
			(await Controller().GetSchemaForEntity((int)UdfEntityType.Call, "42", CancellationToken.None)).Result.Should().BeOfType<ForbidResult>();
		}

		#region FilterValuesVisibleToUserAsync

		private static UserDefinedFieldsService Service()
		{
			var definitions = new Mock<IUdfDefinitionRepository>();
			definitions.Setup(d => d.GetActiveDefinitionByDepartmentAndEntityTypeAsync(Dept, (int)UdfEntityType.Unit))
				.ReturnsAsync(new UdfDefinition { UdfDefinitionId = "def-1", DepartmentId = Dept, EntityType = (int)UdfEntityType.Unit, IsActive = true });
			var fields = new Mock<IUdfFieldRepository>();
			fields.Setup(f => f.GetFieldsByDefinitionIdAsync("def-1")).ReturnsAsync(new List<UdfField>
			{
				new UdfField { UdfFieldId = "everyone", IsEnabled = true, Visibility = (int)UdfFieldVisibility.Everyone },
				new UdfField { UdfFieldId = "officers", IsEnabled = true, Visibility = (int)UdfFieldVisibility.DepartmentAndGroupAdmins },
				new UdfField { UdfFieldId = "admins", IsEnabled = true, Visibility = (int)UdfFieldVisibility.DepartmentAdminsOnly },
				new UdfField { UdfFieldId = "disabled", IsEnabled = false, Visibility = (int)UdfFieldVisibility.Everyone }
			});
			return new UserDefinedFieldsService(definitions.Object, fields.Object, Mock.Of<IUdfFieldValueRepository>(), Mock.Of<IUnitOfWork>());
		}

		private static List<UdfFieldValue> Values()
			=> new[] { "everyone", "officers", "admins", "disabled", "superseded" }.Select(id => new UdfFieldValue { UdfFieldId = id, EntityId = "5", Value = id }).ToList();

		[Test]
		public async Task The_filter_keeps_only_fields_the_callers_role_may_see()
		{
			var service = Service();

			(await service.FilterValuesVisibleToUserAsync(Dept, (int)UdfEntityType.Unit, Values(), true, false, false)).Select(v => v.UdfFieldId).Should().BeEquivalentTo("everyone");
			(await service.FilterValuesVisibleToUserAsync(Dept, (int)UdfEntityType.Unit, Values(), true, false, true)).Select(v => v.UdfFieldId).Should().BeEquivalentTo("everyone", "officers");
			(await service.FilterValuesVisibleToUserAsync(Dept, (int)UdfEntityType.Unit, Values(), true, true, false)).Select(v => v.UdfFieldId).Should().BeEquivalentTo("everyone", "officers", "admins");
		}

		[Test]
		public async Task The_filter_returns_nothing_without_ViewUdfFields()
		{
			(await Service().FilterValuesVisibleToUserAsync(Dept, (int)UdfEntityType.Unit, Values(), false, true, true)).Should().BeEmpty();
		}

		#endregion
	}
}

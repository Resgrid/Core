using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NodaTime;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using TimeZoneConverter;

namespace Resgrid.Web.Helpers
{
    /// <summary>Department-local presentation and HTML input conversion. Stored instants remain UTC.</summary>
    public sealed class DepartmentTime
    {
        private readonly Department _department;
        private readonly DateTimeZone _zone;
        public DepartmentTime(Department department)
        {
            _department = department ?? new Department();
            _zone = Resolve(_department.TimeZone);
        }
        /// <summary>Blank keeps the legacy Pacific default; an identifier neither TZDB nor the Windows map knows falls back to UTC rather than failing every page.</summary>
        public static DateTimeZone Resolve(string timeZone)
        {
            var id = DateTimeHelpers.ConvertTimeZoneString(string.IsNullOrWhiteSpace(timeZone) ? "Pacific Standard Time" : timeZone);
            var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(id);
            if (zone == null && TZConvert.TryWindowsToIana(id, out var iana)) zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(iana);
            return zone ?? DateTimeZone.Utc;
        }
        public static DepartmentTime From(ViewDataDictionary data) => data[nameof(DepartmentTime)] as DepartmentTime
            ?? throw new InvalidOperationException("Department time context was not initialized.");
        public string ZoneId => _zone.Id;
        public DateTime Now => Local(DateTime.UtcNow);
        public DateTime Today => Now.Date;
        public DateTime Local(DateTime utc) => Instant.FromDateTimeUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).InZone(_zone).ToDateTimeUnspecified();
        public DateTime? Local(DateTime? utc) => utc.HasValue ? Local(utc.Value) : null;
        public string Format(DateTime? utc) => utc.HasValue ? Local(utc.Value).FormatForDepartment(_department) : null;
        public string Date(DateTime? utc) => utc.HasValue ? Local(utc.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        public string Input(DateTime? utc) => utc.HasValue ? Local(utc.Value).ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) : null;
        public DateTime ToUtc(DateTime local) => local.Kind == DateTimeKind.Utc ? local
            : local.Kind == DateTimeKind.Local ? local.ToUniversalTime()
            : LocalDateTime.FromDateTime(local).InZoneLeniently(_zone).ToDateTimeUtc();
        public DateTime? ToUtc(DateTime? local) => local.HasValue ? ToUtc(local.Value) : null;
        public DateTime? Parse(string value) => string.IsNullOrWhiteSpace(value) ? null
            : DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? ToUtc(date) : null;

        public RecordValueInput RecordInput(RecordValueInput input, RecordDefinitionSchema schema)
        {
            if (schema?.FindField(input.FieldKey)?.Type != RmsFieldType.DateTime) return input;
            var utc = Parse(input.Value);
            if (!utc.HasValue) return input; // Keep invalid text for the schema validator.
            return new RecordValueInput
            {
                SectionKey = input.SectionKey, FieldKey = input.FieldKey, RowKey = input.RowKey, Ordinal = input.Ordinal,
                Value = utc.Value.ToString("O", CultureInfo.InvariantCulture), Values = input.Values,
                ReferenceType = input.ReferenceType, ReferenceId = input.ReferenceId, UnitCode = input.UnitCode,
                CurrencyCode = input.CurrencyCode, OffsetMinutes = (int)(Local(utc.Value) - DateTime.SpecifyKind(utc.Value, DateTimeKind.Unspecified)).TotalMinutes
            };
        }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class DepartmentLocalTimeAttribute : TypeFilterAttribute
    {
        public DepartmentLocalTimeAttribute() : base(typeof(DepartmentLocalTimeFilter)) { }
    }

    public sealed class DepartmentLocalTimeFilter : IAsyncActionFilter
    {
        private readonly IDepartmentsService _departments;
        public DepartmentLocalTimeFilter(IDepartmentsService departments) { _departments = departments; }
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (context.Controller is Controller controller)
                controller.ViewData[nameof(DepartmentTime)] = new DepartmentTime(await _departments.GetDepartmentByIdAsync(ClaimsAuthorizationHelper.GetDepartmentId(), false));
            await next();
        }
    }
}

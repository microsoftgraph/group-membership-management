// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;

namespace WebApi.Controllers.v1.SqlMembershipSources
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/sqlMembershipSources")]
    public class SqlMembershipSourcesController : ControllerBase
    {
        private readonly IRequestHandler<GetDefaultSqlMembershipSourceRequest, GetDefaultSqlMembershipSourceResponse> _getDefaultSqlMembershipSourceHandler;
        private readonly IRequestHandler<GetDefaultSqlMembershipSourceAttributesRequest, GetDefaultSqlMembershipSourceAttributesResponse> _getDefaultSqlMembershipSourceAttributesHandler;
        private readonly IRequestHandler<GetDefaultSqlMembershipSourceAttributeMappingsRequest, GetDefaultSqlMembershipSourceAttributeMappingsResponse> _getDefaultSqlMembershipSourceAttributeMappingsHandler;
        private readonly IRequestHandler<ResolveDefaultSqlMembershipSourceAttributeMappingsRequest, ResolveDefaultSqlMembershipSourceAttributeMappingsResponse> _resolveDefaultSqlMembershipSourceAttributeMappingsHandler;
        private readonly IRequestHandler<GetDefaultSqlMembershipSourceAttributeValuesRequest, GetDefaultSqlMembershipSourceAttributeValuesResponse> _getDefaultSqlMembershipSourceAttributeValuesHandler;
        private readonly IRequestHandler<PatchDefaultSqlMembershipSourceCustomLabelRequest, NullResponse> _patchDefaultSqlMembershipSourceCustomLabelHandler;
        private readonly IRequestHandler<PatchDefaultSqlMembershipSourceAttributesRequest, NullResponse> _patchDefaultSqlMembershipSourceAttributesHandler;
        private readonly IRequestHandler<GetSqlValidationRequest, GetSqlValidationResponse> _getSqlValidationHandler;

        public SqlMembershipSourcesController(
            IRequestHandler<GetDefaultSqlMembershipSourceRequest, GetDefaultSqlMembershipSourceResponse> getDefaultSqlMembershipSourceHandler,
            IRequestHandler<GetDefaultSqlMembershipSourceAttributesRequest, GetDefaultSqlMembershipSourceAttributesResponse> getDefaultSqlMembershipSourceAttributesHandler,
            IRequestHandler<GetDefaultSqlMembershipSourceAttributeMappingsRequest, GetDefaultSqlMembershipSourceAttributeMappingsResponse> getDefaultSqlMembershipSourceAttributeMappingsHandler,
            IRequestHandler<ResolveDefaultSqlMembershipSourceAttributeMappingsRequest, ResolveDefaultSqlMembershipSourceAttributeMappingsResponse> resolveDefaultSqlMembershipSourceAttributeMappingsHandler,
            IRequestHandler<GetDefaultSqlMembershipSourceAttributeValuesRequest, GetDefaultSqlMembershipSourceAttributeValuesResponse> getDefaultSqlMembershipSourceAttributeValuesHandler,
            IRequestHandler<PatchDefaultSqlMembershipSourceCustomLabelRequest, NullResponse> patchDefaultSqlMembershipSourceCustomLabelHandler,
            IRequestHandler<PatchDefaultSqlMembershipSourceAttributesRequest, NullResponse> patchDefaultSqlMembershipSourceAttributesHandler,
            IRequestHandler<GetSqlValidationRequest, GetSqlValidationResponse> getSqlValidationHandler)
        {
            _getDefaultSqlMembershipSourceHandler = getDefaultSqlMembershipSourceHandler ?? throw new ArgumentNullException(nameof(getDefaultSqlMembershipSourceHandler));
            _getDefaultSqlMembershipSourceAttributesHandler = getDefaultSqlMembershipSourceAttributesHandler ?? throw new ArgumentNullException(nameof(getDefaultSqlMembershipSourceAttributesHandler));
            _getDefaultSqlMembershipSourceAttributeMappingsHandler = getDefaultSqlMembershipSourceAttributeMappingsHandler ?? throw new ArgumentNullException(nameof(getDefaultSqlMembershipSourceAttributeMappingsHandler));
            _resolveDefaultSqlMembershipSourceAttributeMappingsHandler = resolveDefaultSqlMembershipSourceAttributeMappingsHandler ?? throw new ArgumentNullException(nameof(resolveDefaultSqlMembershipSourceAttributeMappingsHandler));
            _getDefaultSqlMembershipSourceAttributeValuesHandler = getDefaultSqlMembershipSourceAttributeValuesHandler ?? throw new ArgumentNullException(nameof(getDefaultSqlMembershipSourceAttributeValuesHandler));
            _patchDefaultSqlMembershipSourceCustomLabelHandler = patchDefaultSqlMembershipSourceCustomLabelHandler ?? throw new ArgumentNullException(nameof(patchDefaultSqlMembershipSourceCustomLabelHandler));
            _patchDefaultSqlMembershipSourceAttributesHandler = patchDefaultSqlMembershipSourceAttributesHandler ?? throw new ArgumentNullException(nameof(patchDefaultSqlMembershipSourceAttributesHandler));
            _getSqlValidationHandler = getSqlValidationHandler ?? throw new ArgumentNullException(nameof(getSqlValidationHandler));
        }

        [Authorize()]
        [HttpGet("default")]
        public async Task<IActionResult> GetDefaultSourceAsync()
        {
            try
            {
                var response = await _getDefaultSqlMembershipSourceHandler.ExecuteAsync(new GetDefaultSqlMembershipSourceRequest());
                return Ok(response.Model);
            }
            catch (Exception ex)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }
        }

        [Authorize()]
        [HttpGet("defaultAttributes")]
        public async Task<IActionResult> GetDefaultSourceAttributesAsync()
        {
            try
            {
                var response = await _getDefaultSqlMembershipSourceAttributesHandler.ExecuteAsync(new GetDefaultSqlMembershipSourceAttributesRequest());
                return Ok(response.Attributes);
            }
            catch (Exception ex)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }
        }

        [Authorize()]
        [HttpGet("attributeMappings/{attribute}")]
        public async Task<IActionResult> GetDefaultSourceAttributeMappingsAsync([FromRoute] string attribute, [FromQuery] string? search = null, [FromQuery] int? top = null)
        {
            try
            {
                var response = await _getDefaultSqlMembershipSourceAttributeMappingsHandler.ExecuteAsync(new GetDefaultSqlMembershipSourceAttributeMappingsRequest(attribute, search, top));
                return Ok(response.Model);
            }
            catch (Exception ex)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }
        }

        /// <summary>
        /// Resolves specific attribute codes to their descriptions. Codes travel in the body so a job with
        /// many saved values cannot exceed URL length limits.
        /// </summary>
        [Authorize()]
        [HttpPost("attributeMappings/{attribute}/resolve")]
        public async Task<IActionResult> ResolveDefaultSourceAttributeMappingsAsync([FromRoute] string attribute, [FromBody] List<string> codes)
        {
            try
            {
                var response = await _resolveDefaultSqlMembershipSourceAttributeMappingsHandler.ExecuteAsync(
                    new ResolveDefaultSqlMembershipSourceAttributeMappingsRequest(attribute, codes ?? new List<string>()));
                return Ok(response.Mappings);
            }
            catch (Exception)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }
        }

        [Authorize(Roles = $"{Models.Roles.CUSTOM_MEMBERSHIP_PROVIDER_ADMINISTRATOR}, {Models.Roles.CUSTOM_MEMBERSHIP_PROVIDER_READER}")]
        [HttpGet("attributeValues/{attribute}")]
        public async Task<IActionResult> GetDefaultSourceAttributeValuesAsync([FromRoute] string attribute, [FromQuery] bool hasMapping)
        {
            try
            {
                var response = await _getDefaultSqlMembershipSourceAttributeValuesHandler.ExecuteAsync(new GetDefaultSqlMembershipSourceAttributeValuesRequest(attribute, hasMapping));
                return Ok(response.Values);
            }
            catch (Exception ex)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }
        }

        [Authorize(Roles = Models.Roles.CUSTOM_MEMBERSHIP_PROVIDER_ADMINISTRATOR)]
        [HttpPatch("default")]
        public async Task<IActionResult> PatchDefaultSourceCustomLabelAsync([FromBody] string customLabel)
        {
            try
            {
                await _patchDefaultSqlMembershipSourceCustomLabelHandler.ExecuteAsync(new PatchDefaultSqlMembershipSourceCustomLabelRequest(customLabel));
                return NoContent();
            }
            catch (Exception)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }
        }

        [Authorize(Roles = Models.Roles.CUSTOM_MEMBERSHIP_PROVIDER_ADMINISTRATOR)]
        [HttpPatch("defaultAttributes")]
        public async Task<IActionResult> PatchDefaultSourceAttributesAsync([FromBody] List<SqlMembershipAttribute> attributes)
        {
            try
            {
                await _patchDefaultSqlMembershipSourceAttributesHandler.ExecuteAsync(new PatchDefaultSqlMembershipSourceAttributesRequest(attributes));
                return NoContent();
            }
            catch (Exception)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }
        }

        [Authorize(Roles = Models.Roles.JOB_OWNER_WRITER + "," + Models.Roles.JOB_TENANT_WRITER)]
        [HttpPost("validateFilters")]
        public async Task<IActionResult> ValidateSqlFilterAsync([FromBody] Dictionary<int, string> sqlFilters)
        {
            try
            {
                var response = await _getSqlValidationHandler.ExecuteAsync(new GetSqlValidationRequest(sqlFilters));
                return Ok(response);
            }
            catch (Exception)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }
        }
    }
}


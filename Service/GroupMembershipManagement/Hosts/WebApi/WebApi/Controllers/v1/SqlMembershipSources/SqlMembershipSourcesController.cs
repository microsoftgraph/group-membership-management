// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
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
        private readonly IRequestHandler<PatchDefaultSqlMembershipSourceCustomLabelRequest, NullResponse> _patchDefaultSqlMembershipSourceCustomLabelHandler;
        private readonly IRequestHandler<PatchDefaultSqlMembershipSourceAttributesRequest, NullResponse> _patchDefaultSqlMembershipSourceAttributesHandler;

        public SqlMembershipSourcesController(
            IRequestHandler<GetDefaultSqlMembershipSourceRequest, GetDefaultSqlMembershipSourceResponse> getDefaultSqlMembershipSourceHandler,
            IRequestHandler<GetDefaultSqlMembershipSourceAttributesRequest, GetDefaultSqlMembershipSourceAttributesResponse> getDefaultSqlMembershipSourceAttributesHandler,
            IRequestHandler<PatchDefaultSqlMembershipSourceCustomLabelRequest, NullResponse> patchDefaultSqlMembershipSourceCustomLabelHandler,
            IRequestHandler<PatchDefaultSqlMembershipSourceAttributesRequest, NullResponse> patchDefaultSqlMembershipSourceAttributesHandler)
        {
            _getDefaultSqlMembershipSourceHandler = getDefaultSqlMembershipSourceHandler ?? throw new ArgumentNullException(nameof(getDefaultSqlMembershipSourceHandler));
            _getDefaultSqlMembershipSourceAttributesHandler = getDefaultSqlMembershipSourceAttributesHandler ?? throw new ArgumentNullException(nameof(getDefaultSqlMembershipSourceAttributesHandler));
            _patchDefaultSqlMembershipSourceCustomLabelHandler = patchDefaultSqlMembershipSourceCustomLabelHandler ?? throw new ArgumentNullException(nameof(patchDefaultSqlMembershipSourceCustomLabelHandler));
            _patchDefaultSqlMembershipSourceAttributesHandler = patchDefaultSqlMembershipSourceAttributesHandler ?? throw new ArgumentNullException(nameof(patchDefaultSqlMembershipSourceAttributesHandler));
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
                return StatusCode(500);
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
                return StatusCode(500);
            }
        }
    }
}


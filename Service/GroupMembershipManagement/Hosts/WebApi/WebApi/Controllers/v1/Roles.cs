// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using WebApi.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace WebApi.Controllers.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class Roles : ControllerBase
    {
        private WebAPISettings _settings;

        public Roles(IOptionsSnapshot<WebAPISettings> configuration)
        {
            _settings = configuration.Value;
        }

        [Authorize]
        [HttpGet()]
        public bool GetIsAdmin()
        {
            return User.IsInRole("Admin");
        }

        [HttpGet(template:"Test")]
        public string GetSetting()
        {
            return $"Sentinel:{_settings.Sentinel}, Test:{_settings.Test}";
        }
    }
}



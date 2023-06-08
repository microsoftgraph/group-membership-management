// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Mvc;
using Repositories.Contracts;

namespace WebApi.Controllers.v1.Admin
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/admin")]
    public class DatabaseMigrationController : ControllerBase
    {
        private readonly IDatabaseMigrationsRepository _databaseMigrationsRepository;

        public DatabaseMigrationController(IDatabaseMigrationsRepository databaseMigrationsRepository)
        {
            _databaseMigrationsRepository = databaseMigrationsRepository ?? throw new ArgumentNullException(nameof(databaseMigrationsRepository));
        }

        // GET Admin/DatabaseMigration
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            await _databaseMigrationsRepository.MigrateDatabaseAsync();
            return Ok();
        }
    }
}
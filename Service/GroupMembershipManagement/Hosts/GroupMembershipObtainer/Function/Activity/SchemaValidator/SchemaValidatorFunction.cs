// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using NJsonSchema;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class SchemaValidatorFunction
    {
        private readonly ILogger<SchemaValidatorFunction> _logger;
        private readonly SchemaProvider _schemaProvider = null;

        public SchemaValidatorFunction(ILogger<SchemaValidatorFunction> logger, SchemaProvider schemaProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _schemaProvider = schemaProvider ?? throw new ArgumentNullException(nameof(schemaProvider));
        }

        [Function(nameof(SchemaValidatorFunction))]
        public async Task<bool> ValidateSchemasAsync([ActivityTrigger] SchemaValidatorRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(SchemaValidatorFunction));

                var isValidJson = true;

                if (_schemaProvider.Schemas.Count == 0)
                {
                    _logger.NoSchemasLoaded();
                    return isValidJson;
                }

                if (_schemaProvider.Schemas.TryGetValue(Schema.GroupMembershipSchema, out string value))
                {
                    var schema = await JsonSchema.FromJsonAsync(value);
                    var errors = schema.Validate(request.Query);
                    if (errors.Count > 0)
                    {
                        _logger.SchemaValidationFailed(errors.ToString());
                        isValidJson = false;
                    }
                }

                else
                {
                    _logger.NoGroupMembershipSchema();
                    return isValidJson;
                }

                _logger.FunctionCompleted(nameof(SchemaValidatorFunction));
                return isValidJson;
            }
        }
    }
}
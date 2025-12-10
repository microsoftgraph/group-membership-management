// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;
using NJsonSchema;

namespace SqlMembershipObtainer
{
    public class SchemaValidatorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly SchemaProvider _schemaProvider = null;

        public SchemaValidatorFunction(ILoggingRepository loggingRepository, SchemaProvider schemaProvider)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _schemaProvider = schemaProvider ?? throw new ArgumentNullException(nameof(schemaProvider));
        }

        [Function(nameof(SchemaValidatorFunction))]
        public async Task<bool> ValidateSchemasAsync([ActivityTrigger] SchemaValidatorRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SchemaValidatorFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            var isValidJson = true;

            if (_schemaProvider.Schemas.Count == 0)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = request.RunId,
                    Message = $"No json schemas have been loaded. Skipping schema validation."
                });

                return isValidJson;
            }

            if (_schemaProvider.Schemas.TryGetValue(Schema.SqlMembershipSchema, out string value))
            {
                var schema = await JsonSchema.FromJsonAsync(value);
                var errors = schema.Validate(request.Query);
                if (errors.Count > 0)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Query not valid: {errors}", RunId = request.RunId }, VerbosityLevel.DEBUG);
                    isValidJson = false;
                }
            }

            else
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = request.RunId,
                    Message = $"No SqlMembership schema has been loaded. Skipping schema validation."
                });

                return isValidJson;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SchemaValidatorFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return isValidJson;
        }
    }
}
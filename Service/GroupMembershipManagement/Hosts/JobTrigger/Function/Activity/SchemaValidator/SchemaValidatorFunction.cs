// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.JobTrigger;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Newtonsoft.Json;
using NJsonSchema;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace JobTrigger.Activity.SchemaValidator
{
    public class SchemaValidatorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IJobTriggerService _jobTriggerService = null;
        private readonly JsonSchemaProvider _schemaProvider = null;

        public SchemaValidatorFunction(
            ILoggingRepository loggingRepository,
            IJobTriggerService jobTriggerService,
            JsonSchemaProvider jsonSchemaProvider)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
            _schemaProvider = jsonSchemaProvider ?? throw new ArgumentNullException(nameof(jsonSchemaProvider));
        }

        [FunctionName(nameof(SchemaValidatorFunction))]
        public async Task<bool> ValidateSchemasAsync([ActivityTrigger] SyncJob syncJob)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SchemaValidatorFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);

            var isValidJson = true;

            if (_schemaProvider.Schemas.Count == 0)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = syncJob.RunId.GetValueOrDefault(),
                    Message = $"No json schemas have been loaded. Skipping schema validation."
                });

                return isValidJson;
            }

            var properties = typeof(SyncJob).GetProperties();
            foreach (var schemaKV in _schemaProvider.Schemas)
            {
                var schema = await JsonSchema.FromJsonAsync(schemaKV.Value);
                var property = properties.FirstOrDefault(x => x.Name.Equals(schemaKV.Key, StringComparison.InvariantCultureIgnoreCase));
                if (property != null)
                {
                    try
                    {
                        var result = schema.Validate(Convert.ToString(property.GetValue(syncJob)));
                        if (result.Count > 0)
                        {
                            await _loggingRepository.LogMessageAsync(new LogMessage
                            {
                                RunId = syncJob.RunId.GetValueOrDefault(),
                                Message = $"Schema is not valid for property: {schemaKV.Key}."
                            });

                            isValidJson = false;
                            break;
                        }
                    }
                    catch (JsonReaderException je)
                    {
                        await _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            RunId = syncJob.RunId.GetValueOrDefault(),
                            Message = $"Unable to parse json for property: {property.Name}.\n{je}"
                        });

                        isValidJson = false;
                        break;
                    }
                }
                else
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        RunId = syncJob.RunId.GetValueOrDefault(),
                        Message = $"Skipping schema validation for property: {schemaKV.Key} as it does not exist in SyncJob."
                    });
                }
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SchemaValidatorFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);

            return isValidJson;
        }
    }
}

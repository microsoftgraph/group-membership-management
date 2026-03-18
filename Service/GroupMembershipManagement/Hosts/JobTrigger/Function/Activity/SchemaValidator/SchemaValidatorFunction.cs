// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.JobTrigger;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using NJsonSchema;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace JobTrigger.Activity.SchemaValidator
{
    public class SchemaValidatorFunction
    {
        private readonly ILogger<SchemaValidatorFunction> _logger;
        private readonly IJobTriggerService _jobTriggerService;
        private readonly JsonSchemaProvider _schemaProvider;

        public SchemaValidatorFunction(
            ILogger<SchemaValidatorFunction> logger,
            IJobTriggerService jobTriggerService,
            JsonSchemaProvider jsonSchemaProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
            _schemaProvider = jsonSchemaProvider ?? throw new ArgumentNullException(nameof(jsonSchemaProvider));
        }

        [Function(nameof(SchemaValidatorFunction))]
        public async Task<bool> ValidateSchemasAsync([ActivityTrigger] SyncJob syncJob)
        {
            using (_logger.BeginSyncJobScope(syncJob))
            {
                _logger.SchemaValidatorStarted(nameof(SchemaValidatorFunction));
                var isValidJson = true;

                if (_schemaProvider.Schemas.Count == 0)
                {
                    _logger.NoJsonSchemasLoaded();
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
                                _logger.SchemaNotValid(schemaKV.Key);
                                isValidJson = false;
                                break;
                            }
                        }
                        catch (Exception e)
                        {
                            if (e is JsonException || e.GetType().Name == "JsonReaderException")
                            {
                                _logger.UnableToParseJson(property.Name, e);
                                isValidJson = false;
                                break;
                            }

                            throw;
                        }
                    }
                    else
                    {
                        _logger.SkippingSchemaValidation(schemaKV.Key);
                    }
                }

                _logger.SchemaValidatorCompleted(nameof(SchemaValidatorFunction));
                return isValidJson;
            }
        }
    }
}

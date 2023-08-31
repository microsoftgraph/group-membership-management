// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.JobTrigger;
using System;
using System.IO;
using System.Linq;

namespace Services.Tests.Helpers
{
    internal static class SchemaProviderFactory
    {
        public static JsonSchemaProvider CreateJsonSchemaProvider()
        {
            var jsonSchemaProvider = new JsonSchemaProvider();

            var jobTriggerFolderName = "JobTrigger";
            var schemaFolderName = "JsonSchemas";
            var currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var jsonSchemaDirectory = Path.Combine(currentDirectory, schemaFolderName);

            if (!Directory.Exists(jsonSchemaDirectory))
            {

                var jobtriggerIndex = currentDirectory.IndexOf(jobTriggerFolderName, StringComparison.InvariantCultureIgnoreCase);
                if (jobtriggerIndex != -1)
                {
                    currentDirectory = currentDirectory.Substring(0, jobtriggerIndex + jobTriggerFolderName.Length);
                    var directories = Directory.EnumerateDirectories(currentDirectory,
                                                                     schemaFolderName,
                                                                     new EnumerationOptions
                                                                     {
                                                                         MatchCasing = MatchCasing.CaseInsensitive,
                                                                         RecurseSubdirectories = true
                                                                     });

                    if (directories.Any())
                    {
                        jsonSchemaDirectory = directories.FirstOrDefault();
                    }
                }
            }

            if (jsonSchemaDirectory != null && Directory.Exists(jsonSchemaDirectory))
            {
                var files = Directory.EnumerateFiles(jsonSchemaDirectory);
                foreach (var file in files)
                {
                    jsonSchemaProvider.Schemas.Add(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
                }
            }

            return jsonSchemaProvider;
        }
    }
}

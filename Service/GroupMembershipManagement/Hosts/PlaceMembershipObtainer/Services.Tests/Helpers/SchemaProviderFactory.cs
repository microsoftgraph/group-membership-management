// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.IO;

namespace Tests.Services.Helpers
{
    internal static class SchemaProviderFactory
    {
        public static SchemaProvider CreateJsonSchemaProvider()
        {
            var jsonSchemaProvider = new SchemaProvider();
            var schemaFolderName = "Schemas";

            var currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var jsonSchemaDirectory = Path.Combine(currentDirectory, schemaFolderName);


            if (jsonSchemaDirectory != null && Directory.Exists(jsonSchemaDirectory))
            {
                var files = Directory.EnumerateFiles(jsonSchemaDirectory);
                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    jsonSchemaProvider.Schemas.Add((Schema)Enum.Parse(typeof(Schema), fileName), File.ReadAllText(file));
                }
            }

            return jsonSchemaProvider;
        }
    }
}
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Text.Json;
using System.Text.Encodings.Web;
using Models;
using System.Text.Json.Serialization;

public static class SyncJobSerializationHelper
{
    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReferenceHandler = ReferenceHandler.Preserve,
        WriteIndented = false
    };

    public static string SerializeSyncJob(SyncJob syncJob)
    {
        return JsonSerializer.Serialize(syncJob, SerializerOptions);
    }
}
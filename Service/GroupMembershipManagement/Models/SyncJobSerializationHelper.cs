// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Text.Json;
using System.Text.Encodings.Web;
using Models;

public static class SyncJobSerializationHelper
{
    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static string SerializeSyncJob(SyncJob syncJob)
    {
        return JsonSerializer.Serialize(syncJob, SerializerOptions);
    }
}
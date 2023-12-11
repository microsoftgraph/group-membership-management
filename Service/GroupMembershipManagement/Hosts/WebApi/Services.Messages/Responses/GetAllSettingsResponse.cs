// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;
using WebApi.Models.DTOs;

namespace Services.Messages.Responses
{
    public class GetAllSettingsResponse : ResponseBase
    {
        public GetAllSettingsResponse()
        {
            Settings = new List<Setting>();
        }
        public List<Setting> Settings { get; }
    }
}

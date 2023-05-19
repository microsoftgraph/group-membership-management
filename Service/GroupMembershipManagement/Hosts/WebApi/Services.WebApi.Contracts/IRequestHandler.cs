// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.Contracts
{
    public interface IRequestHandler<RequestBase, ResponseBase>
    {
        Task<ResponseBase> ExecuteAsync(RequestBase request);
    }
}
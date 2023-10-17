// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.WebApi.Contracts
{
    public interface IValidator<T>
    {
        public ValidationResponse Validate(T objectToValidate);
    }
}

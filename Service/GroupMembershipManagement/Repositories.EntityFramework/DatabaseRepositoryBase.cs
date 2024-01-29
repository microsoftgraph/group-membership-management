// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.EntityFramework
{
    public abstract class DatabaseRepositoryBase<TModel, TEntity>
    {
        protected abstract TEntity ModelToEntity(TModel model);
        protected abstract TModel EntityToModel(TEntity entity);
    }
}

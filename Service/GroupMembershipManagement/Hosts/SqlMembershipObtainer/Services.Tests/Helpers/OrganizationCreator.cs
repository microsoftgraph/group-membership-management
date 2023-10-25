// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using SqlMembershipObtainer.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Services.Tests.Helpers
{
    public class OrganizationCreator
    {
        private List<int> _entityIds = new List<int>();

        internal List<OrganizationLevel> GenerateOrganizationHierarchy(int maxLevel = 10)
        {
            var levels = GenerateOrganizationHierarchyLevel(1, maxLevel)
                            .GroupBy(x => x.LevelId)
                            .Select(x => new OrganizationLevel { LevelId = x.Key, Entities = x.SelectMany(e => e.Entities).ToList() })
                            .ToList();

            foreach (var level in levels.OrderBy(x => x.LevelId))
            {
                foreach (var entity in level.Entities)
                {
                    var childCount = levels.Where(l => l.LevelId == (level.LevelId + 1)).SelectMany(e => e.Entities).Count(e => e.ReportsToPersonnelNbr == entity.RowKey);
                    entity.Childcount = childCount;
                    entity.HaschildrenInd = childCount > 0 ? "1" : "0";
                }
            }

            return levels;
        }

        private string GetNextId()
        {
            if (_entityIds.Count == 0)
            {
                _entityIds.Add(10000);
                return _entityIds[0].ToString();
            }

            var newNumber = _entityIds.Last() + 1;
            _entityIds.Add(newNumber);
            return newNumber.ToString();
        }

        private List<OrganizationLevel> GenerateOrganizationHierarchyLevel(int currentLevel, int maxLevel = 10, string managerId = null)
        {
            if (maxLevel == 0 || currentLevel > maxLevel)
            {
                return null;
            }

            var levels = new List<OrganizationLevel>();

            if (currentLevel == 1)
            {
                var partitionKey = "0";
                var rowKey = GetNextId();
                var azureObjectId = Guid.NewGuid().ToString();
                var rootLevel = new OrganizationLevel
                {
                    LevelId = 1,
                    Entities = new List<PersonEntity> { new PersonEntity(partitionKey, rowKey) { AzureObjectId = azureObjectId } }
                };

                levels.Add(rootLevel);

                var childLevels = GenerateOrganizationHierarchyLevel(rootLevel.LevelId + 1, maxLevel, rowKey);
                if (childLevels != null)
                    levels.AddRange(childLevels.Where(x => x != null));

                return levels;
            }

            var level = new OrganizationLevel
            {
                LevelId = currentLevel,
                Entities = new List<PersonEntity>()
            };

            for (int i = 0; i < 2; i++)
            {
                var rowKey = GetNextId();
                var azureObjectId = Guid.NewGuid().ToString();
                level.Entities.Add(new PersonEntity(managerId, rowKey) { AzureObjectId = azureObjectId });
            }

            levels.Add(level);

            foreach (var entity in level.Entities)
            {
                var childLevels = GenerateOrganizationHierarchyLevel(currentLevel + 1, maxLevel, entity.RowKey);
                if (childLevels != null)
                    levels.AddRange(childLevels.Where(x => x != null));
            }

            return levels;
        }
    }
}

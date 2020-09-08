// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Cosmos.Table;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Entities
{
    [ExcludeFromCodeCoverage]
    public class PersonEntity : TableEntity
    {
        public PersonEntity()
        {
        }

        public PersonEntity(string personnelNumber, string key)
        {
            PartitionKey = personnelNumber;
            RowKey = key;
        }

        public string Business { get; set; }
        public int Childcount { get; set; }
        public string Email { get; set; }
        public DateTime FirstRegularHireDate { get; set; }
        public string HaschildrenInd { get; set; }
        public string JobTitleName { get; set; }
        public string LocationAreaCode { get; set; }
        public DateTime MostRecentHireDate { get; set; }
        public string NodeType { get; set; }
        public string Organization { get; set; }
        public int ParentNodeID { get; set; }
        public int PayScaleStockLevelNbr { get; set; }
        public string PersonStatusCode { get; set; }
        public string PersonStatusDesc { get; set; }
        public string PersonnelSubAreaCode { get; set; }
        public string StaffingResourceTypeCategoryDesc { get; set; }
        public string StaffingResourceTypeGroupDesc { get; set; }
        public string StandardTitle { get; set; }
        public string SupervisorInd { get; set; }
        public int TreeNodeID { get; set; }
    }
}

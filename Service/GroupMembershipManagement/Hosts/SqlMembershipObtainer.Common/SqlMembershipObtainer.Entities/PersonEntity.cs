// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Diagnostics.CodeAnalysis;

namespace SqlMembershipObtainer.Entities
{
    [ExcludeFromCodeCoverage]
    public class PersonEntity : TableEntity
    {
        public PersonEntity()
        {
        }

        public PersonEntity(string reportsToPersonnelNbr, string personnelNumber)
        {
            PartitionKey = reportsToPersonnelNbr;
            RowKey = personnelNumber;
        }

        public string ReportsToPersonnelNbr { get { return PartitionKey; } }
        public string PersonnelNumber { get { return RowKey; } }
        public string Business { get; set; }
        public int Childcount { get; set; }
        public string Email { get; set; }
        public DateTime FirstRegularHireDate { get; set; }
        public string HaschildrenInd { get; set; }
        public string JobTitleName { get; set; }
        public string LocationAreaCode { get; set; }
        public DateTime MostRecentHireDate { get; set; }
        public string Organization { get; set; }
        public int PayScaleStockLevelNbr { get; set; }
        public string PersonStatusCode { get; set; }
        public string PersonStatusDesc { get; set; }
        public string PersonnelSubAreaCode { get; set; }
        public string StaffingResourceTypeCategoryDesc { get; set; }
        public string StaffingResourceTypeGroupDesc { get; set; }
        public string StandardTitle { get; set; }
        public string SupervisorInd { get; set; }
        public string CompanyCode { get; set; }
        public string AzureObjectId { get; set; }
        public string VerticalCode { get; set; }
        public string VerticalDesc { get; set; }
        public string ProjectCode { get; set; }
        public string ProjectName { get; set; }
        public string LocationAreaDetail { get; set; }
        public string PersonnelSubAreaDesc { get; set; }
    }
}

# OwnershipReader function

OwnershipReader function is a membership provider which retrieves the owners of all the groups managed by GMM and synchronizes them with a specific Azure M365 group or security group which acts as the destination group. Different filters can be applied to retrieve the owners by job type.

## Setting an OwnershipReader job

OwnershipReader function also uses a JSON object to define the query, like the one used by SecurityGroup function. The query specifies the rules that will dictate which jobs will be retrieved to subsequently retrieve the owners associated with each jobs' destination group.

There are a couple of reserved words that can be used as filters, "All", "Hybrid".

- All  
This filter will retrieve the owners of all the destination groups defined in the jobs table.
- Hybrid  
This filter will retrieve the owners of all the destination groups where the job's query is hybrid, meaning those that have multiple query parts where the “type” attribute is set to two or more unique source types.  
- You can also set the individual types you would like to retrieve. Either by aggregating single or hybrid types.

Let's look at some examples.
```
Retrieves all jobs.

[
    {
        "type": "GroupOwnership",
        "source":
        [
            "All"
        ]
    }
]
```
```
Retrieves all jobs which query is hybrid, 2 or more unique source types.

[
    {
        "type": "GroupOwnership",
        "source":
        [
            "Hybrid"
        ]
    }
]
```
```
Retrieves all jobs matching the specified source types, 1 or more can be specified, all of those that are specified need to exist in the job's query.

In this example a single source must exist in the job's query, in this case a "SecurityGroup" type.
[
    {
        "type": "GroupOwnership",
        "source":
        [
            "SecurityGroup"
        ]
    }
]

In this example all 3 sources must exist in the job's query

[
    {
        "type": "GroupOwnership",
        "source":
        [
            "SecurityGroup","CustomType1","CustomType2"
        ]
    }
]
```

By specifying the source types in the "source" property array, the function will validate that every single source type is present in the job's query before it can be processed.
The other possible way to define which types to retrieve is to aggregate individual or hybrid jobs. Let's see some examples.

Let's pretend that we want to retrieve all jobs where we have a single source part called "SecurityGroup" and a single source part called "CustomType1". We cannot use the previous format since that would mean that the job would need to be hybrid comprised of "SecurityGroup" and "CustomType1" which is not the case.

Our table would have at least two jobs, and their queries would look like these respectively.
```
Query - JOB1
[
    {
        "type": "SecurityGroup",
        "source": "00000000-0000-0000-0000-000000000000",
    }
]

Query - JOB2
[
    {
        "type": "CustomType1",
        "source":
        {
            "ids":
            [
                1
            ]
        }
    }
]
```
To retrieve these jobs we would need to aggregate these two individual source type. Therefore the job definition would look like:
```
In this example the function will retrieve all those jobs where their query has an individual source "securityGroup" and "CustomType1", once it has done that it will aggregate both results in a single one.

[
    {
        "type": "GroupOwnership",
        "source":
        [
            "SecurityGroup"
        ]
    },
    {
        "type": "GroupOwnership",
        "source":
        [
            "CustomType1"
        ]
    }
]
```

You can aggregate any combination of single or hybrid types.

## Grant Permissions

This step needs to be completed after all the resources have beend deployed to your Azure tenant.

See [Post-Deployment tasks](../../../../../README.md#post-deployment-tasks)

Running the script mentioned in the Post-Deployment tasks section will grant the OwnershipReader system identity access to the resources it needs.



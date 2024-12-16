# No-Op Enforcement Strategy for NonProdService

## Objective
Ensure NonProdService generates syncs that have no-op percentages and churn similar to production.

---

## Key Variables
1. **`jobIndex`**: Index of a job in the table.
   - Range: `0 through 4999` (5000 jobs).
2. **`dayOfYear`**: Day of the year.
   - Calculated using the following:
     - `DATEPART(dayofyear, GETDATE())`  (day of year, up to 365)
3. **`P`**: The period for a single run of the sync job.
   - Controls how often the sync job switches state.
   - `1/P = % of ops`
   - `1 - 1/P = % of no-ops`

---

## Enforcement of No-Op States
We divide the sync job execution time into two states:
- **State 0 (S0): Yes-op runs**
- **State 1 (S1): No-op runs**

State conditions are determined based on `jobIndex`, `dayOfYear`, and the period `P`.

### State Conditions
1. **State 0 (Yes-op)**:
   ```sql
   ({jobIndex % (2*P)} + dayOfYear) % (2*P) < P
   ```
2. **State 1 (No-op)**:
   ```sql
   ({jobIndex % (2*P)} + dayOfYear) % (2*P) >= P
   ```

---

## SQL Query Changes

### Old Query
```json
[
    {
        "type": "SqlMembership",
        "source": {
            "filter": "(EmployeeId > 0 AND EmployeeId <= groupSize AND DATEPART(ms, GETDATE()) < 500) OR (EmployeeId > offset AND EmployeeId <= groupSize + offset AND DATEPART(ms, GETDATE()) >= 500)"
        }
    }
]
```

### New Query
```json
[
    {
        "type": "SqlMembership",
        "source": {
            "filter": "((jobIndex + dayOfYear) % (2*P) < P AND EmployeeId > 0 AND EmployeeId <= groupSize) OR ((jobIndex + dayOfYear) % (2*P) >= P AND EmployeeId > offset AND EmployeeId <= groupSize + offset)"
        }
    }
]
```

---

## How It Works
1. Jobs are divided into states based on a combination of `jobIndex` and `dayOfYear`.
2. **Period `P`** determines the duration for which jobs remain in a particular state:
   - For a job in **State 0 (Yes-op)**, the condition ensures it runs actual operations.
   - For a job in **State 1 (No-op)**, the condition ensures it avoids operations (no-ops).
3. The composite index calculation ensures balanced enforcement of no-op percentages across all jobs.

---

## Example
Given:
- `jobIndex = 50`
- `P = 7`

The following calculations determine state:
```math
State 0: (jobIndex \% (2*P) + dayOfYear) \% (2*P) < P
```
```math
State 1: (jobIndex \% (2*P) + dayOfYear) \% (2*P) >= P
```
For day 14:
- ( (50 % 14 + 14) % 14 = 7 )
- State = **State 1 (No-op)**

---

## Final Conditions Summary
- **State 0 (Yes-op)**: Jobs meeting the condition `{jobIndex % (2*P)} + dayOfYear) % (2*P) < P`
- **State 1 (No-op)**: Jobs meeting the condition `{jobIndex % (2*P)} + dayOfYear) % (2*P) >= P`

This ensures a controlled enforcement of no-ops in sync jobs, simulating production behavior.
## Reference
- Whiteboard for idea: [Whiteboard Link](https://microsoft-my.sharepoint.com/:wb:/p/danielluo/EQyaIE4eM-RKinwCbZLAZd4Bi2MVlS1sZdBK_xALsVmL8A?e=0hqrcj)

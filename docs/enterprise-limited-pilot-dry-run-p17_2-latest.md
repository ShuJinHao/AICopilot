# AICopilot Limited Pilot Dry-Run P17.2 Acceptance

- GeneratedAt: 2026-05-26 11:36:26
- Repository: <local-repo>
- LocalHeadAtGeneration: a75e4b85518dc52fc3853eb1f41de5defba12781
- Branch: main
- SubmittedStateNote: local-only total review package; no push, PR, or remote CI evidence was requested for this refresh
- PullRequest: local-only
- PullRequestHeadAtGeneration: local-only
- GitHubCIAtGeneration: simulation-rc status=SKIPPED conclusion=LOCAL_ONLY
- GitHubCIDetails: GitHub PR check skipped for local-only total review package
- ExternalReviewEvidence: 5.5 Pro ReviewPending
- ExternalReviewBlockingPolicy: evidence-only for P17.2 dry-run material
- DryRunDecision: DryRunEvidenceReady
- ExecutionPermission: not granted
- Boundary: P17.2 runs fake/fixture dry-run rehearsal only; it does not execute a real Pilot and is not GA
- Default State: query_cloud_data_readonly remains disabled, hidden, and non-executable
- Forbidden: Cloud write, Recipe/version, free SQL, raw payload, raw business records, token/API key/connection string output

## Summary

- P17.1 Authorization Inheritance Check: PASSED
- P17.2 Scope And Evidence Package Check: PASSED
- P17.2 Dry-Run Runner Coverage Check: PASSED
- P17.2 Dry-Run Safety Check: PASSED
- GitHub PR Evidence Check Skipped For Local Review: PASSED
- P17.2 No Execution Claim Check: PASSED

## Positive Dry-Run Evidence

| Path | Endpoint | Source Mode | Boundary | Approval | DurationMs | RowCount | Truncated | QueryHash | ResultHash | ArtifactRefs |
| --- | --- | --- | --- | --- | ---: | ---: | --- | --- | --- | --- |
| FixedTemplate | devices | CloudReadonlyProductionPilotDryRun | LimitedPilotDryRunFixedScenario | ToolApproved,FinalApproved | 131 | 13 | False | 99d41511576217fa | b48360a1a6afa9d9 | dryrun-fixedtemplate-devices-artifact |
| FixedTemplate | capacity_summary | CloudReadonlyProductionPilotDryRun | LimitedPilotDryRunFixedScenario | ToolApproved,FinalApproved | 142 | 16 | False | a7b35e655cc90731 | d48be3e1306b238e | dryrun-fixedtemplate-capacity_summary-artifact |
| FixedTemplate | device_logs | CloudReadonlyProductionPilotDryRun | LimitedPilotDryRunFixedScenario | ToolApproved,FinalApproved | 153 | 19 | True | 4ba350a9b4f2313d | efecafc76758e5da | dryrun-fixedtemplate-device_logs-artifact |
| FixedTemplate | pass_station_records | CloudReadonlyProductionPilotDryRun | LimitedPilotDryRunFixedScenario | ToolApproved,FinalApproved | 164 | 22 | True | fd1f46838db8e03b | 13c2276e72021f70 | dryrun-fixedtemplate-pass_station_records-artifact |
| ControlledGoal | devices | CloudReadonlyProductionControlledPilotDryRun | LimitedPilotDryRunControlledGoal | ToolApproved,FinalApproved | 175 | 25 | False | 3b471fee7e796a39 | 80c9f11e5cbf565c | dryrun-controlledgoal-devices-artifact |
| ControlledGoal | capacity_summary | CloudReadonlyProductionControlledPilotDryRun | LimitedPilotDryRunControlledGoal | ToolApproved,FinalApproved | 186 | 28 | False | 35f5fd9be6bb1139 | 2c852041bcb60fbc | dryrun-controlledgoal-capacity_summary-artifact |
| ControlledGoal | device_logs | CloudReadonlyProductionControlledPilotDryRun | LimitedPilotDryRunControlledGoal | ToolApproved,FinalApproved | 197 | 31 | True | 127a414fcb938898 | 08740e730be54eb5 | dryrun-controlledgoal-device_logs-artifact |
| ControlledGoal | pass_station_records | CloudReadonlyProductionControlledPilotDryRun | LimitedPilotDryRunControlledGoal | ToolApproved,FinalApproved | 208 | 34 | True | f80bb0b3ef296a15 | 7d76562dc983ad24 | dryrun-controlledgoal-pass_station_records-artifact |

## Refusal Evidence

| Case | Status | Reason |
| --- | --- | --- |
| RecipeEndpoint | BlockedByPolicy | RecipeForbidden |
| RecipeVersionEndpoint | BlockedByPolicy | RecipeVersionForbidden |
| CloudWritePath | BlockedByPolicy | CloudWriteForbidden |
| UnknownEndpoint | BlockedByPolicy | EndpointNotAllowlisted |
| OverMaxRows | BlockedByPolicy | MaxRowsExceeded |
| OverTimeRange | BlockedByPolicy | TimeRangeExceeded |

## Emergency Stop And Rollback Evidence

| Path | Emergency Stop State | Status | Reason |
| --- | --- | --- | --- |
| FixedTemplate | Active | Rejected | EmergencyStopActive |
| ControlledGoal | Active | Rejected | EmergencyStopActive |
| FixedTemplate | Cleared | RequiresGateWindowApproval | NoAutomaticExecution |
| ControlledGoal | Cleared | RequiresGateWindowApproval | NoAutomaticExecution |

- Rollback Pilot Window evidence: WindowDisablePlanRecorded
- Rollback credential evidence: CredentialRevocationPlanRecorded
- Rollback ledger evidence: HashOnlyLedgerPreserved
- Rollback real credential touched: False

## Details

### P17.1 Authorization Inheritance Check

```text
P17.1 authorization evidence is present and remains non-executing.
```

### P17.2 Scope And Evidence Package Check

```text
P17.2 scope and evidence package markers passed.
```

### P17.2 Dry-Run Runner Coverage Check

```text
Dry-run runner produced 8 positive evidence item(s), 6 refusal item(s), 4 emergency-stop item(s).
```

### P17.2 Dry-Run Safety Check

```text
Dry-run evidence is hash-only and contains no real endpoint, credential, raw payload, or raw business records.
```

### GitHub PR Evidence Check Skipped For Local Review

```text
GitHub PR evidence check skipped because this is a local-only total review package; no push or PR was requested.
```

### P17.2 No Execution Claim Check

```text
P17.2 material records fake/fixture dry-run only.
```

## Remaining Risk

- P17.2 does not execute a real Pilot.
- Real endpoint/token use remains outside P17.2 and requires a future explicit approval.
- External 5.5 Pro review state is evidence only for this dry-run package and must still be considered before any future execution.
- Future execution still requires approved Pilot Window, approved chain, rollback, emergency stop, and approved runtime credential configuration.


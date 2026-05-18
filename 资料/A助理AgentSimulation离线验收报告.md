# A Assistant Agent Runtime Offline Simulation Acceptance Report

- StartedAt: 2026-05-18T13:40:32.8822863+08:00
- EndedAt: 2026-05-18T13:42:25.0294737+08:00
- Scope: AICopilot backend Batch 0-4
- Cloud/Edge touched: No
- Frontend touched: No
- Real Cloud access introduced: No
- Shell capability introduced: No
- Arbitrary server path write introduced: No
- Simulation source marker: sourceMode=Simulation, isSimulation=true, sourceLabel=模拟 Cloud 只读数据

## Commands

- `.\scripts\Test-AgentSimulationScope.ps1 -ChangedFiles $changedFiles`
- `dotnet build src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug`
- `dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=AgentSimulationAcceptance&Runtime!=DockerRequired"`
- `dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "FullyQualifiedName~AgentSimulationAcceptanceTests"`


## Results

- PASS: scope guard
- PASS: backend test project build
- PASS: agent simulation unit tests
- PASS: agent simulation Docker acceptance


## Notes

- CloudReadonly defaults remain Disabled in appsettings.
- CloudAiRead remains disabled by default.
- The Docker acceptance test enables only the AICopilot Tool Registry entry for `query_cloud_data_readonly` and runs with `CloudReadonly__Mode=Simulation`.

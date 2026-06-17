# A助理部署配置说明

日期：2026-05-18

## 默认配置

- `CloudReadonly.Mode=Disabled`
- `CloudAiRead.Enabled=false`

以上默认值不得为了 Simulation 发布候选而改成真实 Cloud 读取。

开发期收口后，不再通过普通 Real CloudReadonly 双轨入口接生产 Cloud 数据。真实 Cloud 只读读取必须走当前批准的 Cloud AiRead / P12 / P13 受控入口，并由部署指南记录具体环境变量。

## Simulation 联调

Simulation 联调只允许通过环境变量或联调配置临时启用：

```text
CloudReadonly__Mode=Simulation
CloudReadonly__Simulation__Enabled=true
CloudReadonly__Simulation__SeedData=true
CloudReadonly__Simulation__DataSet=ManufacturingDemo
CloudReadonly__Simulation__AlwaysMarkAsSimulation=true
CloudAiRead__Enabled=false
```

Simulation 只用于联调和演示，不作为生产验收结果。

## 前端

- `VITE_API_BASE_URL` 默认 `/api`。
- 产物下载必须使用后端返回的 `downloadUrl`。
- 不允许前端配置任意服务器写入路径。

## Docker/Aspire

- Docker 可用：运行 integration acceptance。
- Docker 不可用：自动 skip Docker-required acceptance，并在报告中写明。

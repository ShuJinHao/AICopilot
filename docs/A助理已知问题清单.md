# A助理已知问题清单

日期：2026-05-19

- 真实 Cloud AiRead / P12 / P13 受控只读尚未完成完整验收。
- Cloud/Edge 未改，本阶段也不应修改。
- Simulation 数据不是生产数据，所有报告和图表必须保留 Simulation 标签。
- PDF/PPTX/XLSX 模板仍是 v1。
- MCP 目前以治理视图为主，完整自动发现/同步后续继续。
- 密钥保护当前可用，但生产建议升级到 AES-GCM / Data Protection / KMS。
- 前端如遇浏览器性能问题，优先使用下载或轻量预览。
- Docker/Aspire 依赖已在 CI 中按“可用则运行、不可用则 skip 并记录”处理。
- Integration 分支已创建并完成核心 Simulation 联调；后续如需长期复用本机 Aspire 持久化 Postgres 卷，需要使用与旧卷一致的 `pg-password`，或显式选择新的卷名/临时非持久化环境，禁止为了联调直接删除既有卷。
- `npm audit --audit-level=high` 当前不阻断；仍有 `brace-expansion` moderate 级别告警，后续依赖维护时处理。

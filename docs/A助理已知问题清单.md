# A助理已知问题清单

日期：2026-05-18

- 真实 CloudReadonly 尚未启用。
- Cloud/Edge 未改，本阶段也不应修改。
- Simulation 数据不是生产数据，所有报告和图表必须保留 Simulation 标签。
- PDF/PPTX/XLSX 模板仍是 v1。
- MCP 目前以治理视图为主，完整自动发现/同步后续继续。
- 密钥保护当前可用，但生产建议升级到 AES-GCM / Data Protection / KMS。
- 前端如遇浏览器性能问题，优先使用下载或轻量预览。
- Docker/Aspire 依赖已在 CI 中按“可用则运行、不可用则 skip 并记录”处理。
- Integration 分支尚未创建并完成真实前后端联调。

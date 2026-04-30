# AICopilot.Services.CrossCutting

This project contains service-layer execution infrastructure only.

It is intentionally separate from `AICopilot.Services.Contracts`:
- `Services.Contracts` is for stable contracts such as DTOs, service interfaces, permissions, request/response models, and shared business-facing types.
- `Services.CrossCutting` is for runtime service-layer mechanics such as authorization attributes, MediatR pipeline behaviors, problem exceptions, and serialization helpers.

Rules:
- Do not place domain entities, business aggregates, persistence code, AI runtime adapters, or Cloud business integration here.
- Do not add Cloud write behavior here.
- If a type is part of an external contract, put it in `Services.Contracts` instead.
- If a type executes infrastructure or SDK behavior, keep it in the owning infrastructure project instead.

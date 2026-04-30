# 🚀 AICopilot 项目部署与维护指南

## 一、 环境准备

在开始构建前，确保 .NET SDK 和 Aspire 工作负载已正确安装。

* **安装 Aspire 工作负载**（解决命令找不到的问题）：
```powershell
dotnet workload install aspire

```


* **检查 Docker 状态**：确保 Docker Desktop 已启动且运行在 **Linux Containers** 模式。

---

## 二、 后端服务发布 (.NET 原生镜像)

利用 .NET 10 的 SDK 直接生成镜像，无需编写 Dockerfile。

### 1. 单个服务手动发布 (手动挡)

适用于在子项目目录下（如 `HttpApi`）进行快速验证。

```powershell
# 核心命令：指定系统(linux)、架构(x64)、目标(PublishContainer)
dotnet publish --os linux -a x64 /t:PublishContainer -p:ContainerRegistry=localhost

```

> **注意**：`-a` 是架构缩写，`-arch` 可能在部分版本中无法被 MSBuild 识别。

### 2. AppHost 一键全家桶发布 (自动挡)

在 `AICopilot.AppHost` 目录下执行，会自动带动所有配置好的后端项目。

```powershell
dotnet publish /p:PublishProfile=DefaultContainer /p:ContainerRegistry=localhost

```

---

## 三、 前端服务发布 (Dockerfile 模式)

由于 Vue/Vite 项目无法直接用 .NET SDK 打包，需使用标准 Docker 构建。

### 1. 构建前端镜像

在项目**根目录**下执行：

```powershell
docker build -t shushu/aicopilot-webui:latest src/vues/AICopilot.Web

```

### 2. Nginx 反向代理逻辑 (nginx.conf.template)

前端通过 Nginx 转发请求解决跨域，关键配置：

```nginx
location /api/ {
    proxy_pass ${AICOPILOT_HTTPAPI_HTTP}/api/;
}

```

---

## 四、 容器运行与编排 (Docker Compose)

在 `artifacts` 目录下操作，统一管理数据库、消息队列和业务服务。

### 1. 启动服务

```powershell
# 启动所有服务并在后台运行
docker compose up -d

```

### 2. 常用管理命令

* **查看运行状态**：`docker compose ps`
* **查看实时日志**（排查报错关键）：`docker compose logs -f aicopilot-httpapi`
* **停止并清理容器**：`docker compose down`
* **重启特定服务**：`docker compose restart aicopilot-httpapi`

---

## 五、 关键故障排查 (运维必看)

### 1. 解决 MCP 服务导致的 API 崩溃

由于后端镜像采用了 **Chiseled (精简)** 镜像，环境内没有 `npx` 导致启动报错。
**补救方案**：直接在数据库中关闭 MCP 启用开关。

```powershell
# 方式 A：通过 Docker 命令行直接修改
docker exec -it artifacts-postgres-1 psql -U postgres -d ai-copilot -c "UPDATE mcp_server_info SET is_enabled = false;"

# 方式 B：VS 内部/图形化工具连接后执行 SQL
UPDATE mcp_server_info SET is_enabled = false;

```

### 2. 数据库连接失败排查

* **外部工具连不上**：检查 `docker-compose.yaml` 中 `postgres` 节点是否为 **`ports: - "5432:5432"`**（如果是 `expose` 则外部无法访问）。
* **密码错误**：检查 `.env` 文件中的 `PG_PASSWORD` 变量。

---

## 💡 核心经验总结

1. **前后端分离优势**：后端追求极致轻量（Native AOT + Chiseled），前端追求部署灵活（Nginx 反向代理），这是目前最稳健的架构。 
2. **终端操作细节**：在 Visual Studio 的 PowerShell 终端里，**选中文字按回车(Enter)即复制**，**千万别按 Ctrl+C**（会中止正在构建的任务）。
3. **配置保存**：VS Code 或 VS 编辑 YAML 后一定要确认 **已保存**，否则 Docker 读取的是旧配置。


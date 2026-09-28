# JxTerminal.Server

轻量级同步与备份服务端，用于支持 [JxTerminal](https://github.com/j4587698/JxTerminal) 跨设备同步连接信息、设置、代码片段、SSH 密钥与命令历史。

## 特性

- **零知识端到端加密（E2EE）**：敏感机密（服务器密码、私钥、AI API Key）在客户端通过用户密码派生的 `SyncKey`（PBKDF2 + AES-256-GCM）加密后再传输，服务端无法窥探任何明文机密。
- **单文件数据存储**：使用嵌入式文档数据库 [TinyDb](https://www.nuget.org/packages/TinyDb/)，所有数据持久化在单文件 `data/server.db`，无外部数据库依赖。
- **JWT 身份验证**：安全的用户注册与登录认证机制。
- **开箱即用容器化部署**：支持 Docker 与 Docker Compose 快速运行。

---

## 快速开始

### 方式一：Docker Compose（推荐）

1. 克隆或下载本项目：
   ```bash
   git clone https://github.com/j4587698/JxTerminal.Server.git
   cd JxTerminal.Server
   ```

2. 启动服务：
   ```bash
   docker compose up -d
   ```

3. 查看运行状态：
   ```bash
   curl http://localhost:5000/api/health
   ```
   输出：`{"status":"ok","time":"..."}`

---

### 方式二：.NET 命令行直接运行

要求：已安装 [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

```bash
dotnet run -c Release --urls "http://0.0.0.0:5000"
```

---

## 环境变量与配置

可在 `appsettings.json` 或通过 Docker 环境变量覆盖配置：

| 配置项 | 环境变量名称 | 默认值 | 说明 |
| :--- | :--- | :--- | :--- |
| `Backup:Directory` | `Backup__Directory` | `data` | 数据文件与数据库保存目录 |
| `Auth:AllowRegistration` | `Auth__AllowRegistration` | `true` | 是否开放新用户注册 |
| `Auth:JwtSecret` | `Auth__JwtSecret` | *自动生成* | JWT 签名密钥（留空则自动生成并持久化在 `jwt.secret`） |

---

## 客户端配置

在 JxTerminal 桌面端：
1. 打开 **设置 (Settings)** -> **云同步 (Sync)**；
2. 服务器地址填写：`http://your-server-ip:5000`；
3. 注册或登录账号；
4. 点击 **立即备份** 或 **从云端恢复** 即可完成同步。

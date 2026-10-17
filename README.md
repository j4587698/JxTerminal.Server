# JxTerminal.Server

轻量级跨平台同步与备份服务端，用于支持 [JxTerminal](https://github.com/j4587698/JxTerminal) 跨设备同步连接信息、设置、代码片段、SSH 密钥与命令历史。

## 特性

- **零知识端到端加密（E2EE）**：敏感机密（服务器密码、私钥、AI API Key）在客户端通过用户密码派生的 `SyncKey`（PBKDF2 + AES-256-GCM）加密后再传输，服务端无法窥探任何明文机密。
- **内置 Web 管理控制台**：浏览器直接访问 `http://<ip>:<port>/` 即可进入现代响应式管理后台，支持深浅色主题，无需任何复杂前端配置。
- **首次运行初始化引导**：首个注册用户自动成为系统超级管理员（Admin），后续可一键在控制台管理所有用户。
- **灵活的准入安全控制**：
  - **动态注册开关**：可在管理后台一键开启或关闭公开注册（关闭后仅允许管理员在控制台添加账号）；
  - **注册邀请码 / 接入密钥**：支持配置注册 Secret，客户端注册时必须提供相符密钥，防止公网陌生人随意扫描并滥用服务。
- **用户与备份生命周期管理**：在 Web 控制台可查看所有用户、备份快照数、存储占用；支持重置密码、禁用/启用账号、一键级联删除用户及其所有云端备份数据。
- **单文件嵌入式存储**：基于 [TinyDb](https://www.nuget.org/packages/TinyDb/)，所有数据持久化在单文件 `data/server.db`，无任何外部数据库依赖。
- **容器化一键部署**：内置 Docker 与 Docker Compose 快速运行。

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

3. 打开浏览器访问：
   ```text
   http://localhost:5000/
   ```
   首次运行将展示“初始化超级管理员”页面，创建完成后即可登录并使用管理控制台！

---

### 方式二：.NET 命令行直接运行

要求：已安装 [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

```bash
dotnet run -c Release --urls "http://0.0.0.0:5000"
```

---

## Web 管理后台功能

在浏览器中打开 `http://<服务器地址>:5000/`：

1. **系统概览**：实时统计当前注册用户总数、云端备份快照总数、总存储大小、服务运行时长与开放注册状态。
2. **用户管理**：
   - 查看所有注册用户、权限等级（超级管理员 / 普通用户）、账号状态、注册时间与备份空间占用；
   - ➕ 创建新用户（可指定是否为管理员）；
   - 🔑 一键重置用户密码；
   - ⛔ 禁用 / 启用用户登录权限；
   - 🗑️ 删除用户（自动级联清理该用户所有备份快照数据）。
3. **准入控制与安全配置**：
   - **允许客户端公开注册**：随时一键切换开关；
   - **注册邀请码 / 接入密钥**：设置后，客户端必须输入正确的邀请码才能注册。

---

## 环境变量与配置

可在 `appsettings.json` 或通过 Docker 环境变量覆盖配置：

| 配置项 | 环境变量名称 | 默认值 | 说明 |
| :--- | :--- | :--- | :--- |
| `Backup:Directory` | `Backup__Directory` | `data` | 数据文件与数据库保存目录 |
| `Auth:AllowRegistration` | `Auth__AllowRegistration` | `true` | 初始是否开放新用户注册（亦可在 Web 控制台动态修改） |
| `Auth:RegistrationSecret` | `Auth__RegistrationSecret` | `""` | 初始注册邀请码 / 接入密钥（留空不限制，亦可在 Web 控制台动态修改） |
| `Auth:JwtSecret` | `Auth__JwtSecret` | *自动生成* | JWT 签名密钥（留空则自动生成并持久化在 `jwt.secret`） |

---

## 命令行运维与密码重置（CLI）

当管理员忘记密码、忘记用户名或无法访问 Web 控制台时，可在服务器本地（或 Docker 容器内）直接通过命令行工具进行运维管理：

```bash
# 查看所有注册用户（找回用户名、角色与状态）
dotnet JxTerminal.Server.dll --list-users

# 重置指定用户的密码（支持重置管理员自身或普通用户）
dotnet JxTerminal.Server.dll --reset-password <username> <new-password>

# 直接创建新的管理员账号
dotnet JxTerminal.Server.dll --create-admin <username> <password>

# 调整用户管理员权限（提升/降权）
dotnet JxTerminal.Server.dll --set-admin <username> true

# 删除指定用户及其云端数据
dotnet JxTerminal.Server.dll --delete-user <username>
```

> **提示**：
> - 若使用 Docker 容器部署，只需执行 `docker exec -it <container_id_or_name> dotnet JxTerminal.Server.dll --list-users` 或 `--reset-password <username> <new-password>` 即可。
> - 若数据库目录不在默认路径，可附加 `--data-dir <path>` 参数指定数据目录。
> - **关于端到端加密（E2EE）同步**：密码重置后，请在任意一台保留有正常本地配置的客户端上使用新密码重新登录并同步一次，客户端会自动使用新密码生成的密钥重新加密并更新云端数据。

---

## 客户端接入使用


在 JxTerminal 桌面端：
1. 打开 **设置 (Settings)** -> **云同步与远程备份 (Sync)**；
2. 服务器地址填写：`http://your-server-ip:5000`；
3. 输入用户名与密码（若服务端设置了准入密钥，需在“注册邀请码 / 密钥”中填写）；
4. 点击 **注册** 或 **登录**；
5. 登录成功后即可一键备份、查看远程备份历史或从任意设备一键还原配置。

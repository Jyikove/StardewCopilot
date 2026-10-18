# Stardew Copilot

适用于 macOS 的《星露谷物语》SMAPI 助手：在游戏内与 DeepSeek 对话，结合当前存档状态、Wiki 资料和游戏截图获取建议。

当前版本 **0.17.1**，适配 **Stardew Valley 1.6.15 / SMAPI 4.5.2**。本仓库提供实现源码、构建脚本和合成数据测试，不包含游戏程序集、API Key、个人配置、存档、对话或截图。

## 功能

- 游戏内中文聊天界面，流式回复，支持 macOS 中文输入法、光标编辑和指令补全。
- 进入存档后按 **Enter** 或点击能量条上方按钮打开。单人模式可叠加在兼容的原生菜单上，保持旧菜单并暂停游戏时间；Esc 关闭后后台回答继续。
- 每个存档分别保存对话和待办，长对话自动压缩上下文。
- 按需只读查询背包、任务、NPC、作物、机器、箱子和冰箱物品等游戏状态。
- Wiki 优先的检索增强回答：本地 SQLite 缓存、过期刷新、必要时全网补查。网页来源显示为句内浅蓝色编号，悬停查看网址。
- `/screen` 自适应视觉分析：游戏画面预览、按需从原图裁剪、最多一次重截，结合对话记忆和即时状态回答。
- API Key 在游戏内配置，加密保存在 Mod 自己的目录，不需要额外的 Codex CLI 或系统钥匙串配置。

当前只支持 macOS；截图功能不支持分屏。已占用 Enter 的输入框、确认操作、读档/结算和场景切换优先保留原操作。联机不强制暂停世界。

## 构建与安装

准备已安装 SMAPI 的游戏，以及 .NET 8 SDK。构建目标是游戏所用的 .NET 6；游戏目录里的程序集只作为本地引用，不随源码分发。

```bash
git clone https://github.com/Jyikove/StardewCopilot.git
cd StardewCopilot
./build.sh
```

默认查找 Steam 的标准 macOS 游戏目录。如果游戏安装在其他位置：

```bash
export GAME_PATH="/path/to/Stardew Valley/Contents/MacOS"
./build.sh
```

`GAME_PATH` 指向包含 `StardewModdingAPI.dll`、`Stardew Valley.dll` 的目录，也可作为 `./build.sh` 的位置参数。`DOTNET_BIN` 可指定使用的 .NET SDK 可执行文件。

构建结果位于 `dist/StardewCopilot/`。正常保存并退出游戏后安装：

```bash
./install.sh
```

也可将构建目录整个复制到游戏的 `Mods/` 下。更新时保留原有 `config.json`、`secrets.json`、`.credential-key` 和 `data/`，然后重启游戏。

默认构建只更新本地输出文件，**不生成 ZIP**。仅在明确需要分享包时执行：

```bash
./build.sh --package
```

手动打包需要 Python 3，使用固定文件白名单，不包含个人配置或运行记录。

## 首次使用

进入存档，打开助手，在弹窗中粘贴自己的 DeepSeek API Key 并确认。以后直接进入聊天；用 `/resetkey` 可重新配置。

输入问题后按 Enter 发送，Shift+Enter 换行。支持 Cmd+A、Cmd+V、鼠标定位和方向键编辑。空输入框输入 `/` 显示指令，↑/↓ 选择，Enter 只补全，继续输入问题后再次发送。

| 指令 | 用途 |
| --- | --- |
| `/help 你的问题` | 询问本 Mod 的功能、限制和使用方法；不带问题显示指令说明 |
| `/screen 你的问题` | 分析当前游戏画面，结合本存档对话、即时状态和知识资料 |
| `/search 你的问题` | 独立深度检索，使用即时状态和联网正文，不读取或写入网页缓存 |
| `/clearchat` | 清除当前存档的对话、摘要及对话备份 |
| `/cleartodo` | 清除当前存档全部待办 |
| `/resetkey` | 打开 API Key 配置窗口 |

截图示例：先打开背包，再按 Enter 叠加助手，输入 `/screen 结合刚才的目标，看看哪些物品值得出售`。截图排除助手窗口，保留底层游戏菜单；图片仅保留在本次请求内存，并发送给 DeepSeek。图片和联网服务按提供方实际用量计费。

默认模型为 `deepseek-flash`。截图模式需要支持图片的该模型。普通对话和深度检索的具体配置见 `src/ModConfig.cs` 与 `src/Knowledge/KnowledgeCacheManager.cs` 中的选项定义；修改配置后重启游戏。

## 数据位置与边界

| 数据 | 位置 |
| --- | --- |
| 常规设置 | `Mods/StardewCopilot/config.json` |
| 加密 Key / 本机加密密钥 | `Mods/StardewCopilot/secrets.json`、同目录 `.credential-key` |
| 按存档区分的对话与待办 | `Mods/StardewCopilot/data/` |
| 可复用网页知识库 | `~/Library/Application Support/StardewCopilot/knowledge.sqlite` |
| 原图、预览及局部图 | 仅本次请求内存，不写入磁盘或对话文件 |

分享源码或安装包时使用干净构建输出，不复制正在使用的整个 Mod 目录。密钥加密不隔离同一账户下的其他程序；两个凭据文件都属于个人配置。

游戏状态查询均为只读。助手可以在用户明确要求时添加本地待办，其他游戏操作由玩家完成。截图模式仅开放读取。已移除地图标记功能。

## 实现与测试

- [实现结构](docs/ARCHITECTURE.md)：请求流程、实时状态、检索、视觉分析与菜单叠加。
- [测试方法](TESTING.md)：不需要真实 Key 的核心测试，以及使用本地游戏依赖的界面/GPU 测试。

核心测试使用 .NET 8 和合成数据：

```bash
dotnet run --project tests/SmokeTests.csproj
dotnet run --project tests/screen/ScreenTests.csproj
```

## 参考接口

[SMAPI](https://smapi.io/) · [Stardew Valley Wiki](https://stardewvalleywiki.com/) · [DeepSeek API](https://api-docs.deepseek.com/) · [DeepSeek 图片输入](https://api-docs.deepseek.com/guides/vision/)

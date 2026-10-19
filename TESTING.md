# 测试方法

测试源码中的消息、Key 和状态是合成样例，不使用个人对话或真实 API Key；游戏术语测试只读安装游戏的公共本地化资源。测试结果文件、图片和临时数据不提交到 Git。

## 核心协议与会话

使用 .NET 8 SDK，无需安装游戏，也不会调用真实 DeepSeek：

```bash
dotnet run --project tests/SmokeTests.csproj
dotnet run --project tests/screen/ScreenTests.csproj
dotnet run --project tests/terminology/TerminologyTests.csproj
dotnet run --project tests/mods/ModTests.csproj
```

覆盖消息和摘要、流式接收、取消/超时、按存档保存、凭据加密、指令、待办权限，以及截图工具协议、上下文、即时状态、裁剪/重截预算和来源标记。

待办意图由模型根据本次消息语义判断，不设本地关键词门槛。模拟测试覆盖两条口语请求及不含原关键词的表达，验证工具可用、实际成功结果和按存档保存；建议、闲聊和规划的模拟回复不调用添加工具时不会写入。请求会携带共享语义规则，包括不擅自保存、尊重否定、历史消息不能授权。模拟测试不代表真实模型一定遵守这些规则。

查询预算回归包含：恰好 8 次、单批超过额度、多轮批次超过剩余额度，在流式与非流式下都应禁用工具并产生最终回答；验证跳过的待办/检索不会执行、全部工具 ID 均有结果、原有来源规则不变，以及最终问答能够显示并按存档保存。另验证超出独立响应解析边界的畸形批次仍被拒绝。

人格回归还检查各入口只注入一次共享规则、提示词估算增幅不超过第一阶段基线的 15%、闲聊/疲惫/单纯出门不强制检索但保留即时状态、事实问题原有核实流程、规划保留完整任务和作物数据、摘要与人格分离。模拟回复只证明协议和文本传递正确，不能证明模型实际语气或优先级判断；真实游戏中的十二类验收见 [人格验收](docs/PERSONA-ACCEPTANCE.md)。

`tests/mods` 使用临时目录和合成 Mod 清单验证 Mod 库存注入数据、按 ID 查找、字段白名单、嵌套敏感字段过滤、任意路径拒绝、配置值的运行时状态标记，以及 Mod 文档检索的来源相关性过滤。测试不会读取真实 Mods 目录、真实配置或 API Key，也不调用外部网络。

## 使用游戏依赖的本地检查

以下项目面向 .NET 6，使用本地游戏目录中的 MonoGame/SkiaSharp。先设置 `GAME_PATH`；非标准安装也可以向构建命令传 `-p:GamePath="游戏目录"`。

```bash
dotnet build tests/menu/MenuTests.csproj
dotnet build tests/multiplayer/MultiplayerTests.csproj
dotnet build tests/vision/VisionTests.csproj
dotnet build tests/ui/UiTests.csproj
dotnet build tests/knowledge/KnowledgeTests.csproj
dotnet build tests/terminology-game/TermsGameTests.csproj
```

运行时选择与游戏程序集相同架构的 .NET 6 host，设置原生库搜索目录。例如将自己的 host 路径写入 `DOTNET6_BIN`：

```bash
export DOTNET6_BIN="/path/to/dotnet-6/dotnet"
export DYLD_LIBRARY_PATH="$GAME_PATH${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}"
"$DOTNET6_BIN" tests/menu/bin/Debug/net6.0/MenuTests.dll
"$DOTNET6_BIN" tests/vision/bin/Debug/net6.0/VisionTests.dll
"$DOTNET6_BIN" tests/ui/bin/Debug/net6.0/UiTests.dll
"$DOTNET6_BIN" tests/knowledge/bin/Debug/net6.0/KnowledgeTests.dll
"$DOTNET6_BIN" tests/terminology-game/bin/Debug/net6.0/TermsGameTests.dll
```

不要混用 ARM host 与 x64 游戏程序集。`vision` 检查使用隐藏的合成窗口测试实际 GPU 读取和原图像素，不截取桌面或已运行的游戏。`ui` 在自身的忽略输出目录生成预览图，并使用临时合成待办及凭据。`knowledge` 使用临时 SQLite 和模拟 HTTP 响应。

`terminology` 使用合成术语和 HTTP 响应，检查跨分段名称、重复括号名称合并、属性括号与 URL/标识符保护、歧义回退、术语上下文上限、普通/搜索/截图/帮助入口及中文记录保存。`terminology-game` 直接读取本机游戏的英文与简体中文资源，验证武器、附魔、增益和属性名称，不加载存档或调用模型；真实模型及模组自定义译名仍需按 [中文术语验收](docs/TERMINOLOGY.md) 复测。

## 游戏内复测

1. 正常保存并退出游戏，构建、安装后通过 SMAPI 重启，确认加载的版本和日志。
2. 进入存档，首次打开应显示 Key 配置；已有 Key 时应直接显示聊天。
3. 验证中文组合输入、鼠标光标、Enter 发送、Shift+Enter 换行和 `/` 指令补全。
4. 单人模式在兼容的背包、箱子等菜单上按 Enter，确认旧菜单保留、时间暂停，Esc 返回原菜单。
5. 发送普通问题并检查流式回复和来源悬停；关闭后重新打开能查看后台回答。
6. 新建并勾选待办；退出重进确认当前存档恢复，其他存档独立。
7. 先说明一个目标，再使用 `/screen` 分析当前菜单，确认结合历史目标及即时状态。修改物品后再问，应使用新数据。
8. 调整 UI/世界缩放检查截图的背景、方向和裁剪位置。画面本身缺信息时应查状态或请求补充界面，不能无限重截。
9. 检查 `/search` 的预算与取消，切换存档时旧结果不能进入新会话。

手动联网测试会按 DeepSeek 用量计费；本仓库不提供真实凭据或真实游戏测试记录。

## 联机实机验收

本次自动测试使用合成状态与模拟 API，不连接实际游戏服务器；不能替代两台电脑之间的联机验证。`tests/menu` 覆盖房主/农场工兼容菜单叠加、G 与聊天键冲突、输入/结算拦截、不强制暂停及异步 Key 检查；`tests/multiplayer` 调用本机真实 Stardew Valley 程序集，测试农场工未同步地点排除、当前地点可读、切换本机角色的背包读取和断线状态。核心测试模拟两名玩家同时请求、独立保存、重连、清除隔离和断线取消。测试不读个人存档、Key，也不调用付费服务。

.NET 6 测试编译后以 x64 .NET 6 运行对应 DLL，方式与上面的游戏依赖检查相同：

```bash
dotnet tests/menu/bin/Debug/net6.0/MenuTests.dll
dotnet tests/multiplayer/bin/Debug/net6.0/MultiplayerTests.dll
```

用测试农场做以下双人验收，避免改动常用存档：

1. 一人房主、一人农场工，先只在农场工的 Mac 安装助手；配置自己的 Key。G 和 HUD 能打开助手，Enter 正常打开/发送游戏聊天；聊天输入中 G 不抢焦点。
2. 双方分别拿不同物品、持有不同个人任务和 NPC 好感；询问助手，确认只把本机角色的数据称为“你”。分账时资金分别正确，团队订单说明属于团队。
3. 背包、地图、箱子及商店中用 G 打开，Esc 关闭回到原对象；另一玩家继续移动并观察时间，确认没有被助手暂停。钓鱼拉条、命名、准备/睡觉等待和结算时不抢按键。
4. 农场工询问未同步区域，不能误报“没有作物/物品/NPC”；前往该地后再次查询，使用新同步状态。让房主收走作物或箱子物品，在本机同步后重新提问，确认更新。
5. 双方都安装时各发一个问题、添加不同待办并勾选；互不串记录。农场工退出重进同一角色，应恢复自己的记录。其 /clearchat、/cleartodo 不清除房主记录。
6. 请求途中农场工掉线，随后加入另一测试农场，迟到的回答/待办不能写入新角色。房主模式也重复上述检查；/screen 只分析本机游戏画面。
7. 检查双方 SMAPI 日志是否新增 Stardew Copilot 异常；分享日志前去掉个人信息，不分享配置和凭据文件。

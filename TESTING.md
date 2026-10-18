# 测试方法

测试源码中的消息、Key、物品和状态是合成样例，不使用个人对话或真实 API Key。测试结果文件、图片和临时数据不提交到 Git。

## 核心协议与会话

使用 .NET 8 SDK，无需安装游戏，也不会调用真实 DeepSeek：

```bash
dotnet run --project tests/SmokeTests.csproj
dotnet run --project tests/screen/ScreenTests.csproj
```

覆盖消息和摘要、流式接收、取消/超时、按存档保存、凭据加密、指令、待办权限，以及截图工具协议、上下文、即时状态、裁剪/重截预算和来源标记。

## 使用游戏依赖的本地检查

以下项目面向 .NET 6，使用本地游戏目录中的 MonoGame/SkiaSharp。先设置 `GAME_PATH`；非标准安装也可以向构建命令传 `-p:GamePath="游戏目录"`。

```bash
dotnet build tests/menu/MenuTests.csproj
dotnet build tests/vision/VisionTests.csproj
dotnet build tests/ui/UiTests.csproj
dotnet build tests/knowledge/KnowledgeTests.csproj
```

运行时选择与游戏程序集相同架构的 .NET 6 host，设置原生库搜索目录。例如将自己的 host 路径写入 `DOTNET6_BIN`：

```bash
export DOTNET6_BIN="/path/to/dotnet-6/dotnet"
export DYLD_LIBRARY_PATH="$GAME_PATH${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}"
"$DOTNET6_BIN" tests/menu/bin/Debug/net6.0/MenuTests.dll
"$DOTNET6_BIN" tests/vision/bin/Debug/net6.0/VisionTests.dll
"$DOTNET6_BIN" tests/ui/bin/Debug/net6.0/UiTests.dll
"$DOTNET6_BIN" tests/knowledge/bin/Debug/net6.0/KnowledgeTests.dll
```

不要混用 ARM host 与 x64 游戏程序集。`vision` 检查使用隐藏的合成窗口测试实际 GPU 读取和原图像素，不截取桌面或已运行的游戏。`ui` 在自身的忽略输出目录生成预览图，并使用临时合成待办及凭据。`knowledge` 使用临时 SQLite 和模拟 HTTP 响应。

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

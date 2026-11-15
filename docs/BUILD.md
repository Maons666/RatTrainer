# 构建与测试

构建环境为 Windows x64，使用 Windows 自带的 .NET Framework C# 编译器。需要本机安装目标版本 Rat Game；游戏程序集不包含在仓库中。游戏安装路径可以通过 `-GamePath` 指定。

## 完整构建三个应用

在 PowerShell 中进入项目根目录：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-desktop.ps1 -GamePath 'A:\Steam\steamapps\common\rat game'
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-assist-installer.ps1 -ReusePlugins
```

`bootstrap.ps1` 从官方 Release 下载 BepInEx，并校验下载文件的 SHA-256。完整桌面构建会编译游戏组件、辅助插件和两个桌面程序；独立安装器复用同一套辅助插件。构建不启动游戏，也不会安装到游戏目录。

输出：

- `dist/RatTrainer-All-v1.3.3.exe`
- `dist/RatTrainer-Host-v1.3.3.exe`
- `dist/RatAssist-Installer-v1.0.exe`

已经完整构建过时，可以用 `build-desktop.ps1 -DesktopOnly` 仅重建桌面程序。清理过 `build/` 后需要重新完整构建。

## 离线验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\desktop-regression.ps1 -GamePath 'A:\Steam\steamapps\common\rat game'
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-assist-installer.ps1 -ReusePlugins -Test -GamePath 'A:\Steam\steamapps\common\rat game'
```

布局与窗口检查在离线预览中执行，不连接游戏。安装和卸载检查只写入临时模拟目录；它们需要读取目标版本游戏程序集以准备版本校验样本。检查结果生成在 `test-output/`，不提交到仓库。

`tests/run.ps1` 提供核心逻辑检查，另需本机 .NET 8 运行时。`tests/prepare-qa.ps1`、`tests/run-qa.ps1` 和 `tests/QaPlugin.cs` 用于单独的游戏运行测试，不在常规构建或离线回归中自动执行。

## 清理范围

`.tools/` 是可重新下载的构建依赖；`build/`、`dist/RatTrainer/` 和 `test-output/` 是可重新生成的中间产物。清理前保留需要交付的 EXE。旧游戏测试目录可能包含指向真实游戏目录的目录联接，清理时必须只移除联接本身，不遍历它们的目标。

`tools/catalog-art.py` 是可选的本地素材提取工具，需额外安装 UnityPy；已有美术素材足以完成常规构建，不要求运行该工具。

# Prism

UE `.pak` 资产管理工具：**Android + Windows 桌面**双端，基于 [CUE4Parse](https://github.com/FabianFG/CUE4Parse)。

在 [kardswalker/Prism](https://github.com/kardswalker/Prism)（Android WebView 版）的基础上扩展而来。

## 功能

- **Pak 浏览 / 搜索 / 导出**（raw、PNG、typed：纹理/模型/音频）
- **预览**：纹理（PNG）、音频（WAV 播放）、模型（几何信息）、本地化
- **纹理替换**：ASTC/BC7/DXT
  - 桌面：进程外 `UAssetCLI` + `astcenc`/`texconv`
  - Android：进程内 `prism_codecs.so`（native）
- **本地化（locres）编辑**：过滤/搜索条目，修改写回，构建补丁 Pak
  - 桌面：ListBox 虚拟化渲染
  - Android：分页渲染（每页 100 条）
- **Pak 合并**：冲突检测 + 询问（桌面弹窗 / Android 原生 AlertDialog），Oodle 可选
- **补丁 Pak 构建**（`repak_bind` 打包）
- **缩略图**（设置中开启）、**日志查看/导出**、**设置持久化**、**深浅色主题**
- **响应式布局**：桌面横屏左列右详情 / 竖屏底部抽屉；Android 触屏手势
- AES 密钥、`.usmap` 映射支持

## 平台

| 平台 | 入口 | 技术 |
|---|---|---|
| Windows | `Prism.Desktop.Desktop` | Avalonia 12 / .NET 10（单文件发布） |
| Android | `Prism.Desktop.Android` | Avalonia 12 / .NET 10（APK，arm64-v8a） |

## 项目结构

```
Prism.Desktop          共享 Avalonia UI / 逻辑（库）
Prism.Desktop.Desktop  Windows 壳（入口、发布）
Prism.Desktop.Android  Android 壳（MainActivity、原生对话框、native 库打包）
PakTool.Core           Pak 会话、预览、导出、合并、LocresResourceCodec
UAssetTexture.Core     纹理替换引擎（inspect/replace）
UAssetCLI              纹理替换 CLI（inspect-texture / replace-texture）
Prism                  Android WebView 版（上游原样保留）
tools/                 astcenc / texconv 编码器（运行依赖）
```

## 构建

前置：.NET 10 SDK；Android 需要 JDK 与 Android SDK。

```sh
# 初始化 submodule
git submodule update --init

# 纹理替换 CLI（Windows 桌面运行时依赖）
dotnet build UAssetCLI/UAssetCLI.csproj -c Release

# Windows 单文件发布
dotnet publish Prism.Desktop.Desktop/Prism.Desktop.Desktop.csproj \
  -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# Android Release APK
dotnet build Prism.Desktop.Android/Prism.Desktop.Android.csproj \
  -c Release -p:JavaSdkDirectory=<JDK路径>
```

说明：

- `external/CUE4Parse` 是 git submodule，锁定到 `ecc48789`。
- `tools/` 下的 `astcenc`/`texconv` 不在仓库中，需要自行放入（纹理替换运行时依赖）。
- Oodle 二进制按 EULA 不提交：Android 打包需要时自行构建 `liboodle-data-shared.so` 放入 `third_party/lib/arm64-v8a/`。

## 许可

GPL-3.0（继承上游 kardswalker/Prism）。详见 `LICENSE` 与 `NOTICE`。
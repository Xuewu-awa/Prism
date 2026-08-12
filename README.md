# Prism 工作区

UE `.pak` 资产管理工具生态：**Android + Windows 桌面**双端，基于 [CUE4Parse](https://github.com/FabianFG/CUE4Parse)，在 [kardswalker/Prism](https://github.com/kardswalker/Prism) 基础上扩展。

## 目录

```
FModel-dev/                      Prism 主工程（独立解决方案）
  ├── Prism/                     Android WebView 版（上游原样）
  ├── Prism.PC/                  本地 Web UI 版
  ├── Prism.Desktop/             Avalonia 共享 UI/逻辑（库，net10.0）
  ├── Prism.Desktop.Desktop/     Windows 壳（单文件发布）
  ├── Prism.Desktop.Android/     Android 壳（APK，arm64-v8a）
  ├── PakTool.Core/              Pak 会话/预览/导出/合并（LocresResourceCodec）
  ├── UAssetTexture.Core/        纹理替换引擎
  ├── UAssetCLI/                 纹理替换 CLI
  └── third_party/               Android native 库（prism_codecs/repak_bind）
UAssetTextureWeb/                纹理替换 Web 应用（tools/ 编码器目录）
UAssetCLI/                       纹理替换 CLI（独立副本，供桌面版运行）
UAssetAPI-master/                UAssetAPI（vendored，含本地修改）
UE-Pak-Manager/                  WPF Pak 管理器
rel/                             发行版输出（win/、android/）
```

## 快速开始

```sh
# Windows 桌面（Debug）
dotnet build FModel-dev/Prism.Desktop.Desktop/Prism.Desktop.Desktop.csproj

# Windows 单文件发布
dotnet publish FModel-dev/Prism.Desktop.Desktop/Prism.Desktop.Desktop.csproj \
  -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# Android Release APK（需 JDK + Android SDK）
dotnet build FModel-dev/Prism.Desktop.Android/Prism.Desktop.Android.csproj \
  -c Release -p:JavaSdkDirectory=<JDK路径>
```

说明：`FModel-dev/external/CUE4Parse` 是 git submodule；桌面纹理替换需要 `UAssetCLI`（先 `dotnet build UAssetCLI -c Release`）与 `UAssetTextureWeb/tools/` 下的 astcenc/texconv。

## 许可

GPL-3.0（继承上游 kardswalker/Prism）。详见 `LICENSE` 与 `NOTICE`。

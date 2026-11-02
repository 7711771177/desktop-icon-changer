# 桌面图标一键修改 / DesktopIconChanger

给 Windows 桌面图标换图的单文件小工具：选一张图 → 拖个裁剪框 → 挑一种形状 → 一键应用。

C# WinForms 单文件实现，**零第三方依赖**（只用 .NET Framework 自带的 GDI+），编译产物约 45 KB，免安装、免管理员（改「公共桌面」上的图标才需要提权）。

![界面预览](docs/preview.png)

---

## 功能

- **读取桌面全部图标**：快捷方式与文件夹一视同仁，启动时自动列出并显示当前图标缩略图
- **自由框选裁剪**：在图上拖 1:1 正方形裁剪框，框内拖动移动、边角拖动缩放、滚轮整体缩放；右侧 64px 预览实时跟随
- **四种形状**
  - 方形：整张图铺满
  - 圆形：圆形蒙版，圆外全透明
  - 圆角：iOS 风格圆角矩形（圆角半径约为边长的 20%），角外透明
  - 智能抠图：自动识别主体、背景全透明（离线启发式，见下文）
- **批量应用**：列表里多选，一次给多个图标换成同一张图
- **恢复默认**：一键还原选中图标为系统默认图标
- **撤销**：`Ctrl+Z` 逐步回退，保留最近 20 步
- **自动备份**：第一次改动某个图标前，把原始 IconLocation / desktop.ini / 目录属性记录到 `%APPDATA%\DesktopIconChanger\backup.dat`
- **即时生效**：改完调用 `SHChangeNotify` 刷新图标缓存，不用重启资源管理器（个别情况按 `F5` 即可）

## 下载 / 构建

**直接用**：下载仓库里的 `dist/DesktopIconChanger.exe` 双击运行。需要 .NET Framework 4.x（Windows 8 及以上自带）。

**自己编译**：双击 `build.bat`，产物在 `dist\`。脚本调用的是系统自带的 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，不需要装 Visual Studio。

```bat
build.bat
```

等效命令行：

```bat
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe ^
  /platform:anycpu /optimize+ /out:dist\DesktopIconChanger.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\DesktopIconChanger.cs
```

> 源码文件是 **UTF-8 with BOM** 保存的。用别的编码保存会编译（或运行）后中文乱码。

## 使用

1. 点右上角「选择图片」，直接把 JPG / PNG / BMP / GIF 拖进窗口也可以
2. 在左侧预览图上拖裁剪框，框住想当图标的那一块
3. 选形状：方形 / 圆形 / 圆角 / 智能抠图
4. 在下方列表里点选一个或多个桌面图标
5. 点「应用修改」，桌面立刻刷新
6. 改坏了？选回去点「恢复默认」，或者按 `Ctrl+Z`

## 四种形状是怎么做的

图标统一存到 `%APPDATA%\DesktopIconChanger\icons\`（不放在桌面等工作目录，避免某些目录下资源管理器读不到 .ico 而显示成白纸）。

**方形 / 圆形 / 圆角**：先用 `GraphicsPath` 生成形状路径，再走同一个 `ApplyMask` —— 把形状填成 alpha 蒙版，与原图逐像素相乘。边缘走 `AntiAlias`，所以圆形和圆角有自然的抗锯齿过渡，而不是硬边。

**智能抠图**（离线启发式，**不是**神经网络，不联网、不下载模型）：

1. 边框像素做 16 级量化直方图，最大档的均值当背景色
2. 从四条边泛洪，把「与背景色接近」的像素判为背景（种子容差 48，邻域容差 42/62）
3. 3×3 十字开运算，断开 1~2 像素的细桥
4. 连通块标号，只保留面积 ≥ 最大块 10% 的块（丢掉噪点和碎屑）
5. alpha 做两次 `[1,2,1]` 可分离模糊，再重映射 `v' = clamp((v-0.34)/0.66)`，让混了背景色的边缘落到轮廓外
6. 多源 BFS 把最近的不透明颜色铺到半透明像素（颜色去污，防白边）
7. 按主体外框缩放到画布 94% 并居中

单张 114px 图实测约 9 ms。适合「主体和背景颜色差得明显」的图（插画、贴纸、纯色底产品图）；照片背景复杂时效果一般。

## 技术实现

| 目的 | 做法 |
| --- | --- |
| 改快捷方式图标 | `WScript.Shell` 的 `IShellLink`（反射调用，不引 COM 引用）写 `.lnk` 的 `IconLocation` |
| 改文件夹图标 | 生成 `desktop.ini`（`[.ShellClassInfo]` + `IconResource`），并给目录加 `ReadOnly` 属性让系统认这个 ini |
| 生成多尺寸 ICO | 自己拼 ICO 结构：4 帧 **全 DIB**（256/48/32/16，32bpp `BITMAPINFOHEADER` + 全 0 AND mask）。不用 PNG 压缩帧——兼容性更好 |
| 放大不糊 | `HighQualityBicubic` + `ImageAttributes.SetWrapMode(WrapMode.TileFlipXY)`（不加这句，双三次会采样到画布外，四角出现半透明边环） |
| 刷新桌面 | `SHChangeNotify(SHCNE_ASSOCCHANGED)` |
| 取系统默认图标 | `SHGetFileInfo` / `ExtractIconEx` + `DestroyIcon` |

界面是纯自绘主题：`UiTheme` 配色令牌 + `SoftPanel`（圆角卡片带投影）+ `RoundButton` + 胶囊单选（`Appearance.Button`）+ OwnerDraw 列表 + `OnPaint` 里两枚 `PathGradientBrush` 柔光斑。

## 已知限制

- 「智能抠图」是启发式算法，不是模型。原图里**贴着主体的文字或水印**会被当成主体保留下来，用裁剪框框掉即可
- 修改「公共桌面」(`C:\Users\Public\Desktop`) 上的图标需要以管理员身份运行，否则会静默失败
- 只为 Windows 而生（`.lnk`、`desktop.ini`、Win32 API），不跨平台

## 目录结构

```text
├─ src/DesktopIconChanger.cs   全部源码（单文件，约 2250 行）
├─ dist/DesktopIconChanger.exe 预编译版本
├─ docs/preview.png            界面截图
├─ build.bat                   一键编译
└─ LICENSE                     MIT
```

## 许可证

[MIT](LICENSE) © 2026 desktop-icon-changer contributors

---

## English

A tiny Windows utility that changes desktop icons in one go: pick an image, drag a square crop box, choose a shape (square / circle / rounded corner / auto cutout), apply — with multi-select, batch apply, restore-default, 20-step undo and automatic backup.

Single-file C# WinForms app, **zero third-party dependencies** (GDI+ only), ~45 KB, no installation, no admin rights unless you touch the Public Desktop.

Build with `build.bat` (uses the `csc.exe` bundled with .NET Framework 4.x). Licensed under MIT.

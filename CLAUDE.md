# StylesVN — 《斯泰尔斯庄园奇案》视觉小说（Unity）

> AI 协作入口文档。系统怎么运转见 `docs/系统架构与操作逻辑.md`；玩法与人工测试要点见 `docs/玩法介绍_测试者版.md`；
> 上传与发布见 `PUBLISHING.md`；两个项目的周边对照见 `docs/仓库周边对照_FallenAngel_vs_StylesVN.md`。

## 速查

- **项目**：阿加莎·克里斯蒂《斯泰尔斯庄园奇案》的**非官方同人**视觉小说（中文自行改写，不搬运任何已出版译本）。
  Unity 2022.3.62f3c1（`D:\unity\2022.3.62f3c1`），内置渲染管线，目标平台 Windows / Android。
- **体量**：9 章 · 495 条演出指令 · 437 句对白 · 正文约 2.26 万字（约 100 分钟）；8 推理 / 5 现场调查 / 3 询问 / 6 终局指认 / 36 条证据。
- **内容管线（最重要）**：剧本唯一来源是 `outputs/styles/js/story.js`（DSL：`N()` `S()` `CH()` `BG()` `CHC()` `DED()` `INV()` `ASKQ()` `NOTE()` `FX()`）。
  改完必须重跑转换，**不要手改 `Assets/StreamingAssets/content/*.json`**（下次转换会覆盖）：

  ```bat
  node tools/convert_from_js.js ..\..\styles\js Assets\StreamingAssets\content
  ```

- **构建**：Unity 必须带 **`-noUpm`**（本机 UPM 启动即崩溃，工程把 uGUI / TextMeshPro / Newtonsoft.Json 内置在
  `Assets/ThirdParty`、`Assets/TextMesh Pro`、`Assets/Plugins`、`Packages/`）：

  ```bat
  Unity.exe -batchmode -nographics -quit -noUpm -projectPath <本目录> ^
            -executeMethod Styles.EditorTools.ProjectBootstrap.RunAllAndBuildWindows
  ```

- **两套自检**：① 编辑器侧「自动通关自检」`Styles.EditorTools.SelfTest.RunBatch`；
  ② 成品侧 `Builds\Windows\Styles.exe -styles-selfcheck`（32 项，含「真通关」＝用真实界面从标题玩到结算屏）。
- **一键检查**：`check.cmd`（Windows）或 `./check.sh`（Git Bash）。

## 铁律（AI 协作约束）

1. **可改范围**：`Assets/Scripts/**`、`tools/**`、`outputs/styles/js/story.js`、`docs/**` 与根目录周边文件。
   **不要动**：`Assets/StreamingAssets/content/**`（转换器产物）、`Assets/ThirdParty/**`、`Assets/TextMesh Pro/**`、`Packages/**`（第三方内置源码）、`Library/`、`Builds/`。
2. 场景由代码生成：改 `Assets/Scripts/Editor/ProjectBootstrap.cs` 后跑 `Styles → 2. 生成启动场景` 重建 `Assets/Scenes/Boot.unity`。
   删除脚本时同步删除对应 `.meta`，避免 Missing Script。
3. 不新增 Package；不改 `ProjectSettings`（除用户明确要求）。
4. **内容改动必须过闸门**：转换 → 内容校验（缺失 0 / 问题 0 / 提醒 0）→ `SelfTest.RunBatch` 必须「结果：通过」→
   成品自检 32/32（含真通关）。任何一项红了先修，不要带着红继续改。
5. 改 UI 布局时记住三条既有约定：**对话框不拦射线**（`raycastTarget = false`，否则「点击继续」点不到）；
   **HUD 永远画在最上层**（因此「演出中途返回标题」必须走 `GameUI.EndCurrentFlow()` 清场，否则残躯会留在标题上）；
   **调查热点间距按当前画布尺寸算**（用 1920×1080 参考尺寸会在 4:3 下让两个方框叠住）。
6. 文案与版权：正文自行改写，**不要从任何已出版中文译本粘贴原文**；新增素材必须能在 `THIRD_PARTY_NOTICES.md`
   里找到出处与许可（字体 OFL / Unity 官方包 / 模型许可）。
7. 小步快跑：一次一个可编译、可验证的改动，完成后说明「如何验证」。
8. 关键状态用 `Debug.Log("[Styles] ...")` 输出（成品日志：`%USERPROFILE%\AppData\LocalLow\LorXer Studio\Styles\Player.log`）。

## 收尾习惯

功能经用户拍板后：① 在 `README.md` 的「本次修订」里补一段；② 若改了推理/调查/询问/指认的数量或章节数，
同步更新 `Assets/Scripts/Core/SystemCheck.cs` 里「真通关」的期望值；③ 已验证的改动及时 git 提交
（提交信息前缀 `feat:` / `fix:` / `content:` / `docs:`）。

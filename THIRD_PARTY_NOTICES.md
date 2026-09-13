# 第三方组件与许可（发布前请保留本文件）

本工程包含或依赖以下第三方内容。**发布到公开仓库时请随仓库保留本文件，并在发行包里一并附上对应许可全文。**
（下表按「仓库内位置 / 许可 / 需要注意什么」列出。）

## 1. 引擎与官方包

| 组件 | 位置 | 许可 | 注意 |
|---|---|---|---|
| Unity 2022.3 LTS 运行时 | 不在仓库内（`Library/`、`Builds/` 已忽略） | Unity 官方条款 | 仓库只提交工程源；成品里已含 Unity 运行时，分发受 Unity 条款约束 |
| TextMesh Pro（源码取自 `com.unity.textmeshpro`） | `Assets/TextMesh Pro/`、`Assets/ThirdParty/TextMeshPro/`、`Packages/com.unity.textmeshpro/` | Unity Companion License（**不是**开源许可） | 允许随 Unity 工程一起使用/分发；**不要**单独把这些源码当独立库再发布，也不要改成自己的版权声明 |
| uGUI（源码取自 `com.unity.ugui`） | `Assets/ThirdParty/uGUI/` | Unity Companion License（同上） | 同上。官方仓库 `Unity-Technologies/uGUI` 也是这份许可 |

> 这两份是「源码可见」但不是 OSI 开源。公开仓库里保留它们没问题（Unity 自己就公开在 GitHub），
> 但要在 README / 本文件里说明来源与许可，别在 `LICENSE` 里声称是你自己的代码。

## 2. 第三方库

| 组件 | 位置 | 许可 | 注意 |
|---|---|---|---|
| Newtonsoft.Json（Json.NET）——经 Unity 打包的版本 | `Assets/Plugins/Newtonsoft.Json.dll`、`Packages/com.unity.nuget.newtonsoft-json/` | **包整体：Unity Companion License**（`LICENSES/Unity-Companion-License-NewtonsoftJson.md`）；**包内组件：MIT** | 这是「两层」许可，两个文件都要留：① Unity 的 UPM 包本身按 Unity Companion License 分发；② 包内 `Third Party Notices.md` 声明其打包的组件（`Newtonsoft.Json`、`Json.Net.Unity3D`、`Newtonsoft.Json-for-Unity`、`com.newtonsoft.json`）**均为 MIT**，我们已经把该文件放进 `LICENSES/MIT-Newtonsoft.Json-third-party-notices.md`（含 MIT 全文与 `Copyright (c) 2007 James Newton-King`）。 |
| Noto Serif SC（思源宋体简体） | `Assets/Fonts/NotoSerifSC-Regular.otf`、`NotoSerifSC-Bold.otf` | SIL Open Font License 1.1（全文：`LICENSES/OFL-1.1-NotoSerifSC.txt`） | **必须**随字体附 OFL 全文；若改了字体名要遵守 OFL 的保留名称条款；游戏内嵌字体属于分发，OFL 允许 |
| LiberationSans（TextMesh Pro 官方包自带） | `Assets/TextMesh Pro/Fonts/LiberationSans.ttf` | SIL Open Font License 1.1（就近：`Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt`） | 来自 Unity 的 TMP 包，随包已附 OFL；本工程用它作为 TMP 默认字体的后备 |
| EmojiOne 表情图（TextMesh Pro 官方包自带） | `Assets/TextMesh Pro/Sprites/` | 见同目录 `EmojiOne Attribution.txt`（该素材要求署名，条款以其官网为准） | ⚠️ 若发行版**用不到**表情 sprite，最干净的做法是删掉 `Assets/TextMesh Pro/Sprites/`（同时检查 TMP Settings 的默认 sprite 指向）；要保留就随发行包附上该署名文件 |
| TextMesh Pro 示例包（未展开进 Assets） | `Packages/com.unity.textmeshpro/Package Resources/TMP Examples & Extras.unitypackage` | 见上：包内自带 Anton / Bangers / Oswald / Roboto / LiberationSans 等字体与 EmojiOne 表情图，各自许可见包内 OFL 与 Attribution 文件 | 本工程**没有**把该示例包展开到 `Assets/`，因此这些字体不作为工程资产分发；将来若展开，需要按上表逐项登记（并注意 **Roboto 是 Apache-2.0**，不是 OFL） |

> **关于 Unity Companion License**：Unity 官方包只附**链接**、不附条款全文（`LICENSES/Unity-Companion-License-*.md` 就是官方原文）。
> 我们沿用 Unity 自己的分发方式；需要完整条款时请访问
> <https://unity.com/legal/licenses/unity-companion-license>。

## 3. 仅在开发/离线流程中使用（不进成品）

| 组件 | 位置 | 许可 | 注意 |
|---|---|---|---|
| ComfyUI（美术生成流水线） | `tools/comfy/pipeline.py`（仅通过 HTTP 调用本地服务） | GPL-3.0 | 本仓库只含调用脚本，不含 ComfyUI 本体，也不与成品链接 —— 不构成传染。别人要复现生成流程需自行安装并遵守 GPL |
| 生成用的底模 / LoRA / ControlNet 模型 | 不在仓库内 | 各自模型许可（如 SDXL 的 CreativeML Open RAIL++-M） | 生成的图片可商用但有使用限制；**不要把模型权重提交到仓库**（几百 MB～几 GB，且许可各异） |

## 4. 故事与美术内容

| 内容 | 说明 |
|---|---|
| 剧本（`Assets/StreamingAssets/content/*.json`、`outputs/styles/js/*.js`） | 基于阿加莎·克里斯蒂《斯泰尔斯庄园奇案》（1920）**自行改写的原创中文文本**，没有搬运任何已出版中文译本。原著在**美国已进入公有领域**（1929 年前出版），但在**英国/欧盟为作者身后 70 年（2046 年前）**、**中国为身后 50 年（2026 年底前）**仍在保护期内 —— 公开分发改编作品在这些地区存在法律风险，见 `PUBLISHING.md` |
| 美术（`Assets/Resources/Art/`） | 由本地 ComfyUI 流水线生成的原创图片；人物肖像与场景均为生成结果，不含第三方素材的拼贴 |
| 字体 | 见上表（OFL） |

## 5. 发布前请一并确认

1. 仓库根目录有 `LICENSE`（本工程已按 **代码 MIT + 剧情/美术保留条款** 拆成 `LICENSE-CODE` / `LICENSE-ASSETS`，`LICENSE` 是索引）。
2. 发行包（Releases 里的 zip / apk）里附上：`LICENSE`、`LICENSE-CODE`、`LICENSE-ASSETS`、`THIRD_PARTY_NOTICES.md` 与 `LICENSES/` 目录。
3. 若使用了模型生成的图片作商业发行，核对所用底模的许可条款。

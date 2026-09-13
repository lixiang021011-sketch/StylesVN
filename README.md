# StylesVN · 《斯泰尔斯庄园奇案》视觉小说工程

> **本作是阿加莎·克里斯蒂《斯泰尔斯庄园奇案》（1920）的非官方同人改编**，剧情中文文本为自行改写，
> 未使用任何已出版中文译本；与权利人无任何关联，未获授权或背书，**权利人要求即下架**。
> 本仓库**只包含源码与素材**，可执行成品（Windows / Android）请到 Releases 下载，不要提交进仓库。
> 授权：代码见 [LICENSE-CODE](LICENSE-CODE)（MIT），剧情与美术见 [LICENSE-ASSETS](LICENSE-ASSETS)（保留所有权利 + 同人条款），
> 第三方组件（字体 / Unity 官方包）见 [LICENSES/](LICENSES) 与 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

Unity 2022.3 LTS（2022.3.62f3c1）· 内置渲染管线 · 目标平台 Windows / Android（工程同时兼容 macOS / iOS）。

**已完成并可运行**：全剧九场（495 条演出指令、437 句对白、正文约 2.26 万字、约 100 分钟）、13 张场景图 + 标题图、13 张角色立绘、
完整玩法系统（对话 / 选项 / 推理 / 调查 / 询问 / 笔记本 / 人物关系图 / 存档 / 设置）、
Windows 可执行程序与 Android APK。

## 本次修订（2026-09-13）

### 美术素材整体重做

上一版的 13 张立绘其实全是**房间照**——原因是美术管线的示例命令把「场景图」当成了立绘的
IPAdapter 参考图，同一个书架场景被烧进了每一张立绘；加上抠图用的是「与四角平均色的全局色差」，
人物身上的米色、暖色被一并抠掉，画面碎裂。这次：

1. 立绘管线去掉背景参考，改用「人形剪影引导图 + 纯色幕布 + 色度抠图」；
2. 抠图改为**按色度判定 + 连通性泛洪 + 收边 + 去碎点 + 去溢色**（详见 `docs/美术管线_ComfyUI.md`）；
3. 出图加了自动闸门：幕布色相不对、抠完只剩不到 12% 不透明、脚下被画了地板、背后拖了投影——
   任意一条命中就换种子重画；
4. 27 张素材（13 立绘 + 13 场景 + 1 标题图）全部重出，并补上了标题画面一直缺的底图。

旧素材完整备份在 `ToolsOut/art_v1_backup/`，总览图见 `ToolsOut/contact_characters.png` 与
`ToolsOut/contact_backgrounds.png`。

### 代码修复

| 问题 | 影响 | 修复 |
|---|---|---|
| `fx` 指令解释器里没有分支 | 剧本里 8 处「闪白 / 震动 / 黑场」演出**一次都没播过** | 解释器补上 `fx` 分支，`say` 指令上挂的特效也会执行 |
| 7 条 `fx` 指令没有参数 | 指令形同虚设 | 按上下文补上 `shake` / `fade` / `flash` |
| 对话框本体挡住射线 | 点「→ 点击继续」的位置**没有反应**，只有点对话框以外才推进 | 对话框与姓名牌不再拦截点击 |
| 弹层与标题画面的层级 | 从标题点「人物与关系图 / 设置」时，面板被标题盖住，等于点不开 | 打开弹层时把整层提到最前 |
| 从标题画面读档 | 此时 `GameDirector` 还不存在，点中存档会**空引用报错** | 新增读档入口，自动创建解释器并收起标题 |
| 弹层换页不清空 | 菜单的按钮会残留在存档页、设置页上，还能点到 | 每次打开弹层先清空内容区与按钮区 |
| 推理题答错 | 失误数永远是 0，也不扣分 | 答错计失误并扣 3 分 |
| 列表滚动容器 | 内容区被四向拉伸，每个列表底部多出一大块空白 | 改成「顶对齐 + 竖向按需撑高」 |
| 窗口构建早于源码与素材 | `Builds/Windows` 里的 exe 不含最新剧本、美术与修复 | 重新打包 |
| 药房调查场景从未被触发 | `styles_scene_chemist` 有完整文案与 4 个热点，但剧本里没有对应指令 —— 里面的「不在场证明」永远拿不到 | 在第六章「证据是会自己走出来的」之后接上该场景 |
| 4 条证据定义了却发不出来 | `time` / `alfred_out` / `dying` / `raikes` 没有任何发放通道，笔记本永远集不满 | 解释器支持在 `say` 上挂 `give`，并把 4 条挂到描述它的那句台词上 |
| 内容校验误报 27 条「证据未被授予」 | 校验只扫了指令与选项，漏掉「现场调查热点」和「询问话题」两条发放通道 | 补齐两条通道，并新增「调查场景从未被触发」检查 |
| 调查缺漏不扣分 | 剧本里的调查指令没写 `need`，扣分分支永远不触发 | 未写时回退到调查场景自身的 `need` |

### 第二轮修复（框架体检）

第一轮之后加了「成品自带体检」：`Builds/Windows/Styles.exe -styles-selfcheck` 会把所有界面逐个打开、
点遍每个按钮、逐屏截图，结果写在 `Builds/Windows/ToolsOut/system_check.{txt,json}`。
按 29 项检查 × 三种画幅（16:9 / 20:9 / 4:3）跑通后又修掉这几处：

| 问题 | 影响 | 修复 |
|---|---|---|
| 「自动」按钮按了没反应 | `AutoMode` 只被赋值，没有任何代码读它 —— 自动播放是空的，设置里的「自动播放间隔」同样白给 | 补上自动翻页：念完一句等「自动播放间隔」秒翻页；「快进」则是立刻翻页；开弹层 / 出选项 / 解谜题时自动停手，玩家一点就接回手动 |
| 现场调查的热点会互相压住 | 热点的间距是按 1920×1080 参考尺寸放宽的，热点却按百分比锚定 —— 4:3 画布下横向被压到 86%，两个 96px 的方框叠在一起，压住的那一角点不到 | 改成按「当前画布真实尺寸」松弛，判据换成能保证方框不重叠的切比雪夫距离 |
| 体检本身的两处误报 | ①热点间距门槛用「屏幕宽度的 5.5%」，20:9 下比实际布局要求还严（误报重叠）；②某一项中途失败会把面板留在场上，下一项抓到残骸跟着误报 | ①门槛换算回画布单位，并直接检查方框是否相交；②失败项结束时清场（`GameUI.ResetForCheck`） |
| 体检截图不可信 | 章节卡是淡入的，点完就截图只能拍到全黑；结算屏喂的是构造出来的假状态，截图上印着「证据 0/36」 | 等淡入结束再截；结算屏改用真实游玩状态，指认记录也从真数据取 |

体检结果：**29 项全过**（1600×900 / 2400×1080 / 1280×960 各一遍，0 报错），
其中 8 条运行期提醒全部来自体检自己喂的异常输入。

### 第三轮修复（真通关 + 边界冲突）

「逐项体检全绿」不等于「游戏能玩通」——每一项都是在干净界面上单独测一个系统，
看不出「上一步留下的状态把后面搞坏」。所以又加了**真通关**：用真实界面从标题一路玩到结算屏
（点对话、选选项、答推理、找热点、问口供、指认凶手），再把界面里真实发生的次数和
自动通关自检的期望值对照。这一跑就抓出两处真冲突：

| 问题 | 影响 | 修复 |
|---|---|---|
| 演出中途返回标题会留残骸 | HUD 永远画在最上层，玩家能在**调查 / 推理 / 询问 / 选项 / 章节卡中途**点「菜单 → 返回标题」；此前调查面板、选项框、章节卡会整个留在标题画面上 | 新增 `EndCurrentFlow()`：停解释器、撤解谜面板、关选项层与章节卡、停自动翻页；`LoadFromSlot()` / `Restart()` 也走同一条清理 |
| 返回标题后剧情自己接着演 | 章节卡的演出协程、`wait` 指令的协程都还挂着，返回标题后它们照样把剧情往下推，标题背后会冒出对话框 | 章节卡加作废标志（`Hide()` 后不再 `next()`）；`GameDirector.Stop()` 停掉挂在自己身上的协程；`Next()` 在 `Running == false` 时直接返回 |

体检同时扩到 **31 项**，新增：

* **中途撤退**（调查 / 章节卡里返回标题）——就是上面两条 bug 的守门测试；
* **真通关**——真实界面走完整局，断言 9 张章节卡 / 8 道推理 / 5 场调查 / 3 场询问 /
  6 题指认全对 / 走到结算，并核对证据与评级。

真通关实测：**9 章 · 233 句对白 · 8 推理 · 5 调查 · 3 询问 · 6 题指认全对 · 走到结算 ·
证据 31/36 · 得分 90 · 评级 S**。31 项 × 三种画幅全过，0 运行期错误；
整部剧本的自动通关自检同样通过。

> 「证据 31/36」不是丢档：3 场询问共 35 个话题、其中 16 个发线索，而系统只要求问
> 3+3+4 = 10 个。真通关只问最低要求 → 31 条；把话题问全 → 35/36（自检的假界面会全部解锁，
> 所以它报 35）。差的 1 条出自第三章的选项分支，与另一条互斥，属设计。

配套文档：[docs/系统架构与操作逻辑.md](docs/系统架构与操作逻辑.md)（系统怎么运转、每个动作触发什么、
边界逻辑清单、参考的开源项目）· [docs/玩法介绍_测试者版.md](docs/玩法介绍_测试者版.md)（给测试者的
上手说明、重点测试项、常见「不是 bug 的现象」、Bug 报告模板）。

### 第四轮修复（内容与关系图排版）

人工试玩截图里发现两处「看一眼就知道不对」的问题，都已修：

| 问题 | 根因 | 修复 |
|---|---|---|
| 知识卡是一片空白（只有标题 + 图标 + 「明白了」） | `tools/convert_from_js.js` 里 `case 'note': break;` —— 原稿 `story.js` 的 `NOTE(icon,title,html)` 正文（html 字段）在转换时被整段丢掉，4 张知识卡全是空壳 | 转换器补上 html → body，并把网页标签转成 TMP 能认的写法（`<p>`/`<br>` → 换行、保留 `<b>`）；4 张卡的正文已合并进内容库 |
| 人物关系图糊成一团 | 节点间距只有约 100px，名字标签却是固定 210px 宽居中排——必然互相压住；节点框也小、还和头像错位 | 关系图重排：语义种子 + 碰撞松弛（按「名字宽 / 方框高」推开，保证互不重叠），112px 金框头像、框下名字带深色底衬、红线（同谋）配图例 |

> ⚠️ **内容管线的坑（已记录）**：现在的 `Assets/StreamingAssets/content/*.json` 里有 4 处 `give`、
> 7 处 `fx` 和第六章新增的 2 条指令是**直接改在 JSON 上**的，`story.js` 里没有。
> 也就是说**重跑转换会覆盖掉这些修订**。这一轮的合并脚本只搬了知识卡正文、没动其它字段，
> 但下次要改内容时请先把这些修订回填到 `story.js`，或者把 JSON 当唯一来源。

体检同时扩到 **32 项**，新增：**关系图排版**（逐个量像素：头像必须在自己的方框内、
名字底衬不能互相叠、也不能压到别人的头像）与**知识卡正文非空**（内容级断言，防止再出现空卡片）。

### 第五轮：按原著扩写（从「浓缩提纲」到长篇）

第一版把整部小说压成了骨架：正文 8995 字、约 45 分钟，只相当于原著的**百分之七**左右
（原著英文约 6.2 万词，中文满译通常十几万字）。这一轮按原著把缺的描写与情节补回来：

| 章节 | 补了什么（举例） |
|---|---|
| 序章 斯泰尔斯 | 抵达与宅子、全家晚宴的座次与气氛、玛丽 / 劳伦斯 / 辛西娅 / 多卡斯的肖像、这桩婚事与遗嘱的来龙去脉、阿尔弗雷德的行事、辛西娅的药房 |
| 第一章 悲剧之夜 | 凌晨的铃声与三道门闩、撞门、房内陈设诸证、临终的话、威尔金斯医生的时间推断、丈夫那句“那太好了” |
| 第二章 波洛登场 | 波洛请人的理由、他「先看缺席」的方法论、勘查的七件事逐条写清、壁炉里的遗嘱残片与夏令炉火 |
| 第三章 药与杯 | 五只咖啡杯与糖、可可锅、补药与溴化物、士的宁被溴化物沉淀的机关、仆人的口供与那把失而复得的钥匙 |
| 第四章 死因调查庭 | 调查程序、多卡斯与安妮的证词、威尔金斯 vs 鲍尔斯坦、药房登记簿与笔迹专家、被改成「7月17日」的信、验尸官的裁决 |
| 第五章 为什么他拒绝开口 | 波洛为什么**不**让阿尔弗雷德被捕（一事不再理陷阱）、贾普的压力、玛丽那种「已经知道结局」的平静 |
| 第六章 铁证 | 搜查出的药瓶与假胡子、匿名信、雷克斯太太与不在场证明、药房伙计看到的「黑胡子先生」、伪造笔迹、约翰被捕 |
| 第七章 最后的一环 | 改为夜探：断电、烛下翻抽屉、信封刀上的新缺口、威尔斯律师与遗嘱、玛丽的绿臂章、劳伦斯招认踩碎杯子 |
| 终章 波洛的解释 | 结案前的全场与那块证据板 |

**第二批（同一天）**又补了五块原著内容：保释期间的家庭戏与互相监视、玛丽与鲍尔斯坦的夜间支线、
约翰在看守所的两场探视、辛西娅与劳伦斯那条笨拙的感情线、雷克斯太太农舍的正面场景（流言的真相），
以及终章里波洛逐节拆解机关的十段长篇解释（动机 → 工具 → 假线索 → 时间 → 屋里的另两个人 → 被烧的遗嘱
→ 无罪的陷阱 → 那盎司士的宁 → 藏在纸捻瓶里的信 → 他如何找到它）。

结果：**正文 8995 → 22624 字（2.5 倍），对白 233 → 437 句，演出指令 291 → 495 条，时长约 45 → 100 分钟。**
内容校验仍是 0 缺失 / 0 问题 / 0 提醒；自动通关自检通过；框架体检 32 项 × 三种画幅全过。

> 顺带把内容管线修干净了：`tools/convert_from_js.js` 以前漏映射 `give`（台词发证据）与 `fx`（演出特效），
> 所以当初只能直接改 JSON —— 现在这两项都在转换器里补上，并通过「回填 → 重新转换 → 逐条比对」
> 把 JSON 与 `story.js` 重新对齐（141 段新增、0 处意外改动）。**现在 story.js 是唯一来源，重跑转换不会丢东西。**

### 验收结果

编辑器的自动通关自检（`Styles/7. 自动通关自检`）跑完整部剧本：

```
章节 9 · 演出指令 291        调查 5 场 · 推理 8 题 · 询问 3 场
用过的背景 13 种 · 立绘 10 种   收集证据 35 / 36
指认 6 次，全对               结局触发 是       结果：通过 ✔
```

（差的那 1 条证据出自第三章的选项分支，与另一条互斥，属设计。）

内容与素材体检（`Styles/5. 校验内容与素材`）：**缺失素材 0 · 校验问题 0 · 提醒 0**。

## 快速开始

1. 用 Unity Hub 打开本目录（编辑器版本 **2022.3.62f3c1**）。
2. 菜单 **Styles → 1. 配置工程（PC + 移动端）**。
3. 菜单 **Styles → 2. 生成启动场景**（会生成 `Assets/Scenes/Boot.unity`）。
4. 直接打开 `Builds/Windows/Styles.exe` 试玩，或在编辑器里打开 Boot 场景按 Play。
5. 命令行一键流程（本机实测通过）：

```bat
Unity.exe -batchmode -nographics -quit -noUpm -projectPath <本目录> ^
          -executeMethod Styles.EditorTools.ProjectBootstrap.RunAllAndBuildWindows
```

6. 框架自检（成品自带，不用装编辑器）：直接启动成品并带参数，跑完自己退出，结果在
   `Builds/Windows/ToolsOut/system_check.txt`，逐屏截图在同目录 `shots/` 下：

```bat
Builds\Windows\Styles.exe -styles-selfcheck -screen-fullscreen 0
```

## ⚠️ 关于本机的 Unity Package Manager

本机两个 Unity 编辑器（2022.3.49f1c1 / 2022.3.62f3c1）的 **UPM 进程启动即崩溃**
（`project:update-dependencies` 返回 500，参数为空），任何工程都无法解析网络包。

因此本工程做了两件事，**完全不依赖 UPM，离线可编译**：

1. `Packages/manifest.json` 只保留内置模块；
2. uGUI、TextMeshPro、Newtonsoft.Json 的源码/程序集已内置到
   `Assets/ThirdParty/` 与 `Assets/Plugins/`（由 `tools/vendor_packages.js` 一键重建）。

> 命令行务必带 `-noUpm`。若将来修复了 Unity 安装（Hub 里重新安装该版本），
> 可以删掉 `Assets/ThirdParty`、`Assets/TextMesh Pro`、`Assets/Plugins/Newtonsoft.Json.dll`，
> 并改回 `Packages/manifest.full.json` 里的官方包写法。

## 目录

| 路径 | 内容 |
|---|---|
| `Assets/Scripts/Core` | 数据模型、剧本解释器（GameDirector）、存档、设置、音频、资源服务 |
| `Assets/Scripts/UI` | 纯代码构建的界面：对话、选项、推理、调查、询问、笔记本、关系图、回顾、菜单、结算 |
| `Assets/Scripts/Editor` | 工程配置、场景生成、中文字体资产生成、素材导入规则、内容校验、打包 |
| `Assets/StreamingAssets/content` | 剧本与数据库（JSON，UTF-8）：章节 / 证据 / 调查 / 询问 / 指认 / 原著对照 |
| `Assets/Resources/Art` | ComfyUI 生成的背景与立绘（运行时代码按命名约定加载） |
| `Assets/Fonts` + `Assets/Resources/Fonts` | Noto Serif SC（OFL）与生成的中文 TMP 字体资产 |
| `tools/comfy/pipeline.py` | ComfyUI + ControlNet + IPAdapter 生成流水线 |
| `tools/convert_from_js.js` | 把网页原型剧本编译成 Unity 内容库 |
| `tools/vendor_packages.js` | 内置 uGUI / TMP / Newtonsoft（绕过坏掉的 UPM） |
| `ToolsOut/` | 内容报告、素材清单、质检报告、总览图 |
| `docs/` | 美术管线说明、20 小时制作计划 |
| `Builds/` | Windows 与 Android 成品 |

## 内容管线

```
剧本文档(YAML/CSV)  →  tools/build_content.py  →  Assets/StreamingAssets/content/*.json
                                       ↓
                        tools/asset_manifest.py  →  待生成素材清单
                                       ↓
        blockout 线稿/深度图  →  ComfyUI(SDXL+ControlNet+IPAdapter)  →  Assets/Art/**
                                       ↓
                          tools/qa_check.py  →  质检报告（分辨率/空白/人脸/风格一致性）
```

细节见 `docs/美术管线_ComfyUI.md` 与 `docs/制作计划_20小时.md`。

## 授权与署名 / Licensing

| 范围 | 文件 | 条款 |
|---|---|---|
| 源代码（`Assets/Scripts/`、`tools/` 等） | [LICENSE-CODE](LICENSE-CODE) | MIT |
| 剧情文本与美术素材 | [LICENSE-ASSETS](LICENSE-ASSETS) | 保留所有权利；允许非商业游玩 / 解说 / 评测（需署名），商业使用与再发布需事先许可 |
| 第三方（Noto Serif SC 字体、uGUI、TextMesh Pro、Newtonsoft.Json 的 UPM 版本） | [LICENSES/](LICENSES)、[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) | SIL OFL 1.1 / Unity Companion License |

**同人声明**：本作与阿加莎·克里斯蒂的继承人或任何出版商无关联，未获其授权或背书。
原著保护期：美国已进入公有领域；英国 / 欧盟至 2046 年底；中国大陆至 2026 年底。
如权利人提出要求，将立即下架相关内容。

---

## English

**StylesVN** — an unofficial, fan-made visual-novel adaptation of Agatha Christie's
*The Mysterious Affair at Styles* (1920). All Chinese text is an original rewrite; no published
translation was used. Not affiliated with, or endorsed by, the rights holders; content will be
removed on request. (The novel is public domain in the US; still protected in the UK/EU until
the end of 2046 and in mainland China until the end of 2026.)

* **Engine / target**: Unity 2022.3 LTS (2022.3.62f3c1, built-in render pipeline), Windows + Android.
* **Scope**: 9 chapters · 495 script beats · 437 spoken lines · ~22.6k Chinese characters (~100 min playing time),
  with 8 deduction puzzles, 5 investigation scenes, 3 interrogations, 6 final-accusation questions and 36 evidence items.
* **Engine-independent core**: the story lives in JSON (`Assets/StreamingAssets/content/`), driven by a small
  interpreter (`GameDirector`) through an `IVnView` interface — so the same script can be played by the real UI
  or by an automated mock view.
* **Built-in checks**: the shipped player can run its own health check (32 items, including a full
  play-through of the real UI from title to ending):

  ```bat
  Builds\Windows\Styles.exe -styles-selfcheck -screen-fullscreen 0
  ```

  Results land in `Builds\Windows\ToolsOut/system_check.txt` (+ screenshots).
  Editor-side gates: `check.cmd` (compile + content validation + full-story self test).
* **Build note**: Unity must be run with **`-noUpm`**. Unity's Package Manager crashes on the machine
  this project was authored on, so uGUI / TextMesh Pro / Newtonsoft.Json are vendored under
  `Assets/ThirdParty`, `Assets/TextMesh Pro` and `Assets/Plugins`. On a healthy Unity install you may
  instead restore `Packages/manifest.full.json` and delete those vendored copies.
* **Tooling**: the script is authored in a small JS DSL and converted to JSON
  (`tools/convert_from_js.js`); art was generated with a local ComfyUI pipeline (`tools/comfy/pipeline.py`).
* **Licensing**: source code under MIT (`LICENSE-CODE`); story text and art assets are
  all-rights-reserved with fan-work terms (`LICENSE-ASSETS`); third-party notices in
  `THIRD_PARTY_NOTICES.md` and `LICENSES/`. Builds are published through GitHub Releases, not committed.
* **Docs**: `docs/系统架构与操作逻辑.md` (architecture and control flow),
  `docs/玩法介绍_测试者版.md` (player & tester guide), `PUBLISHING.md` (release checklist),
  `docs/仓库周边对照_FallenAngel_vs_StylesVN.md` (repo-scaffolding comparison with the author's other project).

## 上传 / 发布注意

* 仓库**只放源码**：`Library/`（Unity 缓存，约 2.5 GB）与 `Builds/`（成品，约 848 MB）已在 `.gitignore` 中排除；
  成品请走 **Releases** 上传（`Styles-win64.zip` + `Styles.apk`）。
* 入库规模实测：**921 个文件 / 约 69 MB**，最大单文件 11.5 MB（字体），远低于 GitHub 限制。
* 更完整的发布前清单（体积、许可、隐私、CI 建议）见 [PUBLISHING.md](PUBLISHING.md)。

# 仓库周边对照：FallenAngel × StylesVN

> 「周边」= 与玩法无关、但决定别人能不能看懂、能不能跑起来、能不能合法使用的那些文件：
> README / AGENTS.md / .gitignore / .gitattributes / LICENSE / 第三方声明 / 一键检查脚本 / docs 组织 / 发布清单。
> 本文对照两个工程，记录**本次互相补齐的内容**与**不要互相照搬的地方**。
> 对照日期：2026-09-13。

## 一、两个工程的定位差异（决定了周边该怎么写）

| | FallenAngel | StylesVN |
|---|---|---|
| 类型 | 原创竖屏音游 Demo（局内 modifier 的 roguelike 方向） | 《斯泰尔斯庄园奇案》**同人**视觉小说（中文原创改写） |
| 引擎 | Unity 2022.3.62f3c1 | Unity 2022.3.62f3c1 |
| 远端 | `github.com/lixiang021011-sketch/FallenAngel`（已公开） | 待上传（本机已 `git init`） |
| 入库规模 | 766 文件 / 30.7 MB | 932+ 文件 / 约 69 MB |
| 第三方素材策略 | **他人的音频不入库**（`.gitignore` 明确排除 `Assets/Resources/Audio/`），`_refs/` 与 AI 出图目录也不入库 | 无音频；字体为 OFL 可分发；美术自制 |
| 版权风险 | 原创内容，风险低 | **改编作品**：美国公有领域，英/欧至 2046、中国至 2026 年底仍受保护 → 必须写明同人声明 |

## 二、逐项对照与本次动作

图例：✅ 已有且规范 / ⚠️ 有但不够 / ❌ 缺失 → 箭头表示本次补齐方向。

| 周边项 | FallenAngel | StylesVN（本次修订前） | 本次动作 |
|---|---|---|---|
| README | ✅ 十大节 + **末尾 English** 段，双语 | ⚠️ 内容很全但**只有中文** | StylesVN → FallenAngel：补 `## English` 段（含构建注意、许可、检查入口） |
| AGENTS.md（AI 协作入口） | ✅ 2.2 KB：速查 + 铁律 + 收尾习惯 | ❌ 无 | StylesVN ← FallenAngel：新建 `AGENTS.md`（速查/铁律/收尾习惯，写入本工程的硬约束：转换器单一来源、`-noUpm`、两套自检、三条 UI 既有约定、版权红线） |
| CLAUDE.md | ✅ 与 AGENTS.md **内容完全相同** | ❌ 无 | StylesVN ← FallenAngel：同样放一份（内容拷贝，保持一致） |
| .gitignore | ✅ 分区注释 + 明确策略（第三方音频/参考资料/AI 输出不入库） | ⚠️ 规则够用但少策略说明 | StylesVN ← FallenAngel：补 `.DS_Store` `Thumbs.db` `desktop.ini` `__pycache__/` `*.stackdump`，并把「第三方音频不入库」写成**注释策略**（工程目前无音频） |
| .gitattributes | ❌ 无 | ✅ 有（LF 统一 + 二进制标记 + 可选 LFS 规则） | FallenAngel ← StylesVN：新增 `.gitattributes` |
| LICENSE 家族 | ❌ 无（公开仓库却没有许可 = 默认「保留所有权利」，别人无法合法复用） | ✅ `LICENSE` 索引 + `LICENSE-CODE`(MIT) + `LICENSE-ASSETS` | FallenAngel ← StylesVN：补 `LICENSE` / `LICENSE-CODE` / `LICENSE-ASSETS`（内容条款按原创处理，不含原著改编问题） |
| 第三方许可全文 | ⚠️ 字体 OFL 就近放在 `Assets/Fonts/SourceHanSans-OFL.txt` ✅，但无统一声明（TMP 的 Unity Companion License 未记录） | ✅ `LICENSES/` 4 份 + `THIRD_PARTY_NOTICES.md` | 双向：StylesVN 学 FallenAngel 把 **OFL 就近放在字体旁**（`Assets/Fonts/NotoSerifSC-OFL.txt`）；FallenAngel 建 `THIRD_PARTY_NOTICES.md` + `LICENSES/`（补 TMP 的 Unity Companion License） |
| 一键检查脚本 | ✅ `compile_check.sh`：Unity 常量、**编辑器占用检查**、**日志里 grep `error CS`**（因为 batchmode 编译失败也可能返回 0） | ⚠️ 只有文档里的长命令 | StylesVN ← FallenAngel：新增 `check.cmd`（Windows）+ `check.sh`（Git Bash），沿用「占用检查 + 退出码 + 日志兜底」三件套，并串上内容校验与通关自检 |
| docs 组织 | ✅ `docs/` 7 篇 + `docs/README.md` 索引，文件名英文 | ✅ `docs/` 4 篇（架构 / 玩法 / 美术管线 / 制作计划），文件名中文 | 各自保留；StylesVN 把本篇对照文档也放进 `docs/` |
| 验证证据入库 | ✅ `balance/`（模拟脚本 + 报告）、`portfolio/`（评审清单 + 验证摘要） | ✅ `ToolsOut/`（内容报告 / 素材清单 / 质检 JSON / 总览图） | 各自保留（同类做法：把可复核的证据留在仓库里） |
| 发布清单 | ⚠️ `portfolio/release_review_checklist.md` 是**玩法评审**，不是仓库发布清单 | ✅ `PUBLISHING.md`（体积表、许可、隐私、Releases、自检勾选项） | FallenAngel ← StylesVN：新增 `PUBLISHING.md` |
| 自动化检查套件 | ✅ 按域拆成 6 个 `*Checks.cs`（Icon / NotePart / PlayModifier / PlayVisual / Portfolio / PortfolioPlay） | ⚠️ 单一 `Core/SystemCheck.cs`（71 KB，32 项，内含真通关） | 建议项：StylesVN 后续可按域拆成 `Checks/` 子目录（见第五节） |
| 提交信息规范 | ✅ `docs:` / `feat:` 前缀 + 中文说明 | ⚠️ 尚未提交过 | StylesVN：在 `AGENTS.md` 里写明前缀约定 |

## 三、本次给 StylesVN 新增/修改的周边

1. `AGENTS.md` + `CLAUDE.md`（内容一致）——AI 协作入口：速查、铁律、收尾习惯。
2. `check.cmd`（Windows 批处理，纯 ASCII 以免 cmd 代码页把中文注释拆成命令）与 `check.sh`（Git Bash）——
   一键跑「编译 + 内容校验」→「自动通关自检」，退出码 0/1/2/3 语义明确。实测：23 秒内跑完并全绿。
3. `Assets/Fonts/NotoSerifSC-OFL.txt`——把 OFL 许可放到字体旁边（学 FallenAngel 的做法）。
4. `.gitignore` 补系统文件、Python 缓存、崩溃转储，并写入「第三方音频不入库」的策略注释。
5. `README.md` 末尾新增 `## English` 段（双语仓库，方便外语读者）。
6. 本文档（同时放进 FallenAngel 的 `docs/`）。

## 四、本次给 FallenAngel 新增的周边

1. `LICENSE` + `LICENSE-CODE`（MIT）+ `LICENSE-ASSETS`（原创内容条款）。
2. `.gitattributes`（LF 统一 + 二进制标记）。
3. `THIRD_PARTY_NOTICES.md` + `LICENSES/Unity-Companion-License-TextMeshPro.md`
   （字体 OFL 已在 `Assets/Fonts/` 旁，本文档指出其位置）。
4. `PUBLISHING.md`（仓库发布清单：体积、排除项、许可、Releases、Topics 建议、自检项）。
5. 本文档放进 `docs/`。

> 这些文件都只是**新增**，没有改动 FallenAngel 的任何代码、场景或既有文档；是否提交由你决定
> （`git status` 里会看到它们处于未跟踪状态）。

## 五、不要互相照搬的地方

| 事项 | 说明 |
|---|---|
| `_refs/` 目录 | FallenAngel 用它放**外部开源参考谱面/素材**并整目录排除。StylesVN 不要照搬这个做法去放原著文本——即使目录被 `.gitignore` 排除，本地留存的原文也不该成为工作流的一部分；本工程的扩展写作依据是**自制的剧情要点笔记**，不入库任何原著段落或译本。 |
| 第三方音频 | FallenAngel 明确「本地保留、公开版不含他人音乐」。StylesVN 目前**没有任何音频**；将来补音频时若使用第三方素材，必须沿用同一策略，且把来源写进 `THIRD_PARTY_NOTICES.md`。 |
| 检查套件拆分 | FallenAngel 按域拆 6 个 `*Checks.cs` 更适合它的模块数量；StylesVN 的 32 项是一个**连贯的界面流程体检**，硬拆会牺牲「真通关」的连续性。若要拆，建议按此边界：`Checks/ContentChecks.cs`、`Checks/UiFlowChecks.cs`、`Checks/SaveSettingsChecks.cs`、`Checks/PlaythroughCheck.cs`。 |
| 文件名语言 | FallenAngel 的 docs 用英文文件名；StylesVN 面向中文协作，保留中文文件名。两边都不必强行统一。 |

## 六、下一步（按优先级）

1. **FallenAngel 立刻加 `LICENSE`**（公开仓库没有许可，别人无法合法复用；本次已生成，等你提交）。
2. StylesVN 上传 GitHub（本机已 `git init`，见 `PUBLISHING.md` 的命令与 Topics）。
3. StylesVN 按域拆分 `SystemCheck.cs`（可选，收益是维护性）。
4. 两个工程都可以加一个最小 CI：跑 `check.cmd` 的等价命令（Unity 授权是门槛，个人仓库可只跑内容校验部分）。

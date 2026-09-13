# 上传 GitHub 前的检查清单（StylesVN）

> 这份清单是针对本工程实测的结果（2026-09-13 量过一次体积、扫过一次路径与密钥、核过一次第三方许可）。
> 按顺序做完再 `git init` / `push`，能避开几乎所有常见坑。

---

## 一、先说三个「会出事」的点

### 1. 版权（最要紧的一条）

本作是《斯泰尔斯庄园奇案》（1920）的改编。原著的保护期在各地区不一致：

| 地区 | 状态 | 含义 |
|---|---|---|
| 美国 | **公有领域**（1929 年前出版的作品） | 在美国公开分发改编没问题 |
| 英国 / 欧盟 | 作者身后 70 年 → **2046 年底前仍受保护**（阿加莎·克里斯蒂 1976 年去世） | 在这些地区公开分发改编作品，**需要授权** |
| 中国 | 作者身后 50 年 → **2026 年底前仍受保护**（到 2026-12-31） | 同上 |

**可以放心做的**：你的剧本是自行改写的中文原创文本，没有搬运任何已出版的译本 —— 这比「复制译本」安全得多。
**建议做的**：README 里写清「非官方同人 / 非商业改编，版权归权利人所有，如权利人要求会立即下架」，
仓库和发行包里不要放原著原文或译本摘录；如果要商业化，先取得授权或等保护期结束。

### 2. 别把「不该进仓库的东西」提交进去

本工程实测体积：

| 目录 | 体积 | 是否入库 | 说明 |
|---|---|---|---|
| `Library/` | **2.5 GB** | ❌ | Unity 导入缓存，`.gitignore` 已忽略 |
| `Builds/` | **848 MB** | ❌ | 成品走 **GitHub Releases**，不要提交 |
| `Assets/` | 60.6 MB | ✅ | 美术 30 MB + 字体 22.6 MB + 第三方源码 ~7 MB |
| `ToolsOut/` | 58 MB | ⚠️ 只留报告 | `raw/`(9.7) `vendor/`(3.5) `art_v1_backup/`(41.4) `shots/` 全部忽略 |
| `Packages/` | 13.9 MB | ✅ | 内置包（本机 UPM 坏了，工程离线编译靠它） |

修剪后仓库约 **65 MB**，远低于 GitHub 的限制（单文件硬上限 100 MB，超过 50 MB 会告警）。
本工程**没有任何单文件超过 50 MB**（最大是 11.5 MB 的 `NotoSerifSC-Bold.otf`）—— 不必上 Git LFS，
想上也可以（`.gitattributes` 里留了注释好的 LFS 行）。

### 3. 本机路径 / 隐私

扫了一遍，有 1 处需要改：`tools/vendor_packages.js` 里的 `UGUI_SRC` 原来硬编码了开发机上的
绝对路径（指向另一个工程的 `Library/PackageCache`）。这行会把用户名和一个无关工程的路径暴露在公开仓库里，
别人也跑不通。**已修**：现在优先读环境变量 `UGUI_SRC`，否则从本工程自己的
`Library/PackageCache/com.unity.ugui@1.0.0` 取；找不到会给出明确提示并退出。

其余提到 `LorXer` 的地方是 `ProjectSettings` 里的公司名 `LorXer Studio` 与 Bundle ID `com.lorxer.styles`
—— 那是你的发行信息，属于有意保留，不算泄露。仓库里**没有**发现任何 API key / token / 密码。

---

## 二、许可怎么摆

本工程混合了三类内容，**不要用一个 MIT 覆盖全部**：

1. **你的代码**（`Assets/Scripts/`、`tools/`）：建议 MIT（或 Apache-2.0）。
2. **剧情与美术**（`Assets/StreamingAssets/content/`、`Assets/Resources/Art/`）：建议写「保留所有权利，
   非商业使用请注明来源」之类的条款；改编作品的权属还牵扯原著（见上）。
3. **第三方**：uGUI / TextMesh Pro 是 **Unity Companion License**（源码可见但非 OSI 开源，Unity 自己公开在
   GitHub 上，随工程保留 + 注明来源即可）；Newtonsoft.Json 是 MIT；Noto Serif SC 是 **SIL OFL 1.1**
   （必须随字体附 OFL 全文）。详细逐项列表见仓库里的 `THIRD_PARTY_NOTICES.md`（已为你生成）。

---

## 三、动手步骤

```bat
cd <本工程目录>

:: 0) 先确认 .gitignore / .gitattributes / THIRD_PARTY_NOTICES.md 都在（本次已生成）
:: 1) 初始化
git init
git add -A
git status --short            :: 看一眼入库清单：不该有 Library/、Builds/、ToolsOut/raw 这些
git commit -m "StylesVN: 斯泰尔斯庄园奇案 Unity 视觉小说（源码 + 剧本 + 美术）"

:: 2) 建远端（把 <你的账号>/<仓库名> 换掉）
git branch -M main
git remote add origin https://github.com/<你的账号>/<仓库名>.git
git push -u origin main
```

发布成品（Windows 压缩包 / APK）走 **Releases**：

```bat
:: 打 tag 并推上去，然后在 GitHub 上 New release 里上传 Styles-win64.zip 与 Styles.apk
git tag -a v0.1.0 -m "第一版：九章完整流程"
git push origin v0.1.0
```

---

## 四、发布前逐条自检

- [ ] `git status` 里**没有** `Library/`、`Temp/`、`Builds/`、`Logs/`、`ToolsOut/raw|vendor|art_v1_backup|shots`
- [ ] 没有任何单文件 > 50 MB（本工程现状：最大 11.5 MB ✓）
- [ ] `tools/vendor_packages.js` 的本机路径默认值已改掉
- [ ] `LICENSE` 已加，且**没有**把 uGUI / TMP / 字体 / 剧情美术一起划进 MIT
- [ ] `THIRD_PARTY_NOTICES.md` 已加；OFL 与 MIT 的全文已随仓库或发行包附上
- [ ] README 里写清了：Unity 版本 `2022.3.62f3c1`、**必须带 `-noUpm`**（本机 UPM 崩溃，故内置了 uGUI/TMP/Newtonsoft）
- [ ] README 里说明了「同人改编 / 非商业」与权利人下架声明
- [ ] 发行包里附了：`LICENSE`、`THIRD_PARTY_NOTICES.md`、字体 OFL、Newtonsoft MIT
- [ ] 没有把 `Assets/Fonts/*.otf` 换名后当成自研字体（OFL 保留名称条款）
- [ ] `Assets/Art/`（空目录）与 `tools/comfy/pipeline_v1_backup.py` 要不要留：前者建议删，后者建议留（它记录了旧管线的错误做法）

---

## 五、对贡献者的友好度（可选但推荐）

1. **README 的「快速开始」要能让别人在别的机器上跑起来**：现在写的是本机路径与 `-noUpm`，
   建议补一句「若你的 Unity 能正常联网获取包，可改用 `Packages/manifest.full.json` 并删掉 `Assets/ThirdParty`」。
2. **加一个最小 CI**（可选）：本工程自带命令行入口，接 GitHub Actions 只需一句
   `Unity.exe -batchmode -nographics -quit -noUpm -projectPath . -executeMethod Styles.EditorTools.ProjectBootstrap.RunAllAndBuildWindows`
   —— 它会顺带跑内容校验（0 缺失 / 0 问题 / 0 提醒）。注意 CI 需要 Unity 授权，个人仓库建议只跑内容校验脚本。
3. **附上自检结果**：把 `ToolsOut/content_report.txt` 与 `ToolsOut/qa_report.json` 一并入库，
   别人一眼就能看到「9 章 / 495 条指令 / 437 句对白 / 证据 36 条 / 校验 0 问题」。
4. **截图**：`Builds/Windows/ToolsOut/shots/` 的逐屏截图很适合放进 README（挑 4~6 张，别把 5 MB 的总览图直接内嵌）。

---

## 六、如果你想直接「一步到位」

需要我做的话，我可以：

1. 改掉 `tools/vendor_packages.js` 的本机路径；
2. 生成 `LICENSE`（代码 MIT + 内容保留条款）与 `LICENSE-CODE` / `LICENSE-ASSETS` 拆分；
3. 从自检截图里挑 6 张做成 `docs/screenshots/` 并写进 README；
4. 在本机跑一遍 `git init + add + status`，把**即将入库的文件清单和总体积**列给你确认（不会 push）。

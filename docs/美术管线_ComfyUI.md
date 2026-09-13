# 美术管线：ComfyUI + ControlNet + IPAdapter

本工程的美术**不使用手绘**，全部由本地 ComfyUI 生成；构图与一致性由 ControlNet 与 IPAdapter 控制。

## 1. 本机环境

| 项目 | 实测值 |
|---|---|
| GPU | NVIDIA RTX 4070 Laptop，**8GB 显存** |
| 内存 / CPU | 32GB / Ryzen 7 8845H（8 核 16 线程） |
| ComfyUI | `D:\AI\ComfyUI_windows_portable`，内置 Python 3.13 + torch 2.13.0+cu130 |
| 生成速度 | SDXL + Lightning 8 步，1536×864 场景图 **约 15~25 秒/张** |

### 已安装的模型

| 用途 | 文件 |
|---|---|
| 基础模型（场景） | `checkpoints/sd_xl_base_1.0.safetensors` |
| 基础模型（动漫/galgame 风格，可选） | `checkpoints/animagine-xl-4.0.safetensors` |
| 构图控制（关键） | `controlnet/controlnet-union-sdxl-1.0.safetensors`（Union：depth / canny / lineart / scribble / pose 一个模型全包） |
| 加速 | `loras/sdxl_lightning_4step_lora.safetensors`（4~8 步出图，速度提升 4 倍） |
| 角色一致性 | `ipadapter/ip-adapter-plus_sdxl_vit-h.safetensors` + `clip_vision/CLIP-ViT-H-14...safetensors` |
| 放大 | `upscale_models/RealESRGAN_x4.pth` |
| 自定义节点 | `custom_nodes/ComfyUI_IPAdapter_plus` |

> 启动 ComfyUI：双击 `D:\AI\ComfyUI_windows_portable\run_nvidia_gpu.bat`，服务地址 `http://127.0.0.1:8188`。

## 2. 生产流程

```
① ToolsOut/asset_manifest.json     ← Unity 编辑器菜单「Styles/5. 校验内容与素材」自动产出（缺哪些图一清二楚）
② tools/comfy/pipeline.py          ← 读清单 → 画 blockout 引导图 → 调 ComfyUI → 存图 → 自动质检
③ Assets/Resources/Art/{Backgrounds,Characters}/*.png
④ Unity 的 ArtImportPostprocessor  ← PNG 落盘即自动设为 Sprite 2D + ASTC/BC7 压缩 + 角色底部锚点
```

### 命令

```bash
# 场景：生成指定几张 / 全部
python tools/comfy/pipeline.py --kind bg --only styles_bedroom styles_library
python tools/comfy/pipeline.py --kind bg --all

# 立绘：全部（每个角色自动选键色、自动重画到幕布色相正确为止）
python tools/comfy/pipeline.py --kind char --all

# 标题图
python tools/comfy/pipeline.py --kind title

# 换模型（例如换成动漫 checkpoint）
set CKPT_BG=animagine-xl-4.0.safetensors
set CKPT_CHAR=animagine-xl-4.0.safetensors
```

> ⚠️ **不要给立绘传 `--ref` 背景图。**
> v1 的示例命令是 `--kind char --ref .../Backgrounds/styles_library.png`，
> 于是 IPAdapter 把同一张书架场景烧进了 13 张立绘里，全部立绘都成了"房间里站着一个小人"。
> 立绘的参考图只能是**已经确认合格的立绘**（`Assets/Resources/Art/Characters/*_neutral.png`）；
> 背景图一律会被 `pipeline.py` 拒绝。

## 3. ControlNet 怎么用（关键）

**场景图**：`pipeline.py` 里的 `BG_SPECS` 用一组矩形描述画面结构（墙、地面、窗户、家具的位置与"深度值"），
Pillow 把这些矩形渲染成灰度 **blockout 图**，再作为 `ControlNetApplyAdvanced` 的 `depth` 引导输入。

```
blockout（灰度方块构图） ──► ControlNet Union(depth) ──► SDXL 去噪 ──► 成品场景图
```

好处：**构图是可控、可复现、可批量的**。要改画面布局，改 `BG_SPECS` 里的几行坐标即可，
不需要重画整张图；同一个布局还能生成白天/夜晚/不同天气的变体（换 prompt、复用 blockout）。

**立绘**：以"已通过的角色图"或"风格参考图"作为 IPAdapter 输入，固定脸型/服装/配色，
再换表情或姿势的 prompt 批量产出同一角色的多个表情（`<id>_<emotion>.png`）。

立绘现在还多两道保险：

1. **人形剪影引导图**：Pillow 画一个"头顶到脚底、居中站立"的灰度剪影，
   以很低的强度（0.35、end_percent 0.45）喂给 ControlNet，只用来锁构图，
   放开细节让模型自己画。
2. **幕布色相校验**：模型经常把"纯品红 / 绿幕"画成陶土红或米黄，
   一旦底边实测色相偏离要求超过阈值就自动换种子重画（`--tries`，默认 4 次）。

### 抠图（透明底）的正确做法

出图是"带纯色幕布的正片"，透明底由 `cutout_alpha()` 后处理得到，判据是**色度**而不是整体色差：

| 步骤 | 做法 | 为什么 |
|---|---|---|
| 底色采样 | 取最外一圈像素的中位色 | 只要人物不贴边，一定拿到幕布色 |
| 背景判定 | 色相接近幕布 + 饱和度不低于幕布的一定比例 + 亮度差可控 | 灰/黑/米色的西装与皮肤不会被误判 |
| 泛洪 | 从画布四边做四连通泛洪，且限制**相邻像素跳变** | 幕布自身的渐变能吃干净，人物硬边挡得住 |
| 收边 | 先把背景收掉 1px，再补回"颜色明显不是幕布"的封闭区域 | 修掉抗锯齿羽化带钻进去咬出的针孔 |
| 去碎点 | 只保留最大的前景连通块（其余 <10% 的碎点丢弃） | 去掉幕布纹理被误判成前景的脏点 |
| 去溢色 | 压低边缘的幕布色通道 | 去掉绿边/红边 |

> v1 的 `cutout()` 用的是"与四角平均色的**全局**色差"，在人物身上的米色、暖色上一律误伤，
> 于是整张立绘被咬得支离破碎。现已替换为上面的做法。

## 4. 一致性策略（20 小时体量的关键）

1. **风格锚点**：先产出 5~8 张"风格标杆图"，人工确认后作为 IPAdapter 的固定参考。
2. **角色锚点**：每个角色先生成一张 `_neutral` 正面图；后续所有表情都以它为 IPAdapter 参考。
3. **固定种子家族**：同角色用同一 seed 基值 + 递增偏移，减少随机漂移。
4. **必要时训练 LoRA**：当角色超过 8 个表情、或风格漂移明显时，用已通过的 30~50 张图训练一个
   SDXL 风格 LoRA（本地 4070 约 1.5~2 小时），之后所有出图挂载该 LoRA，一致性会显著提升。
5. **自动质检**（`ToolsOut/qa_report.json`）：分辨率、平均亮度、对比度、是否空白、透明通道覆盖率；
   不合格的自动重新生成。

## 5. 命名规范（Unity 端按此约定自动加载）

```
Assets/Resources/Art/Backgrounds/styles_bedroom.png          场景（16:9，1536×864 起）
Assets/Resources/Art/Characters/poirot_neutral.png           立绘（透明底，832×1216 起）
Assets/Resources/Art/Characters/poirot_angry.png             同一角色不同表情
Assets/Resources/Art/CG/styles_cg_denouement.png             剧情 CG（整幅插画）
Assets/Resources/Audio/BGM/*.ogg                   音乐
Assets/Resources/Audio/SFX/*.wav                   音效
Assets/Resources/Audio/Voice/<角色>/<台词编号>.ogg  语音（可选）
```

## 6. 版权与合规

- 生成模型与 LoRA 需遵守各自许可（SDXL 为 CreativeML Open RAIL++；Animagine XL 4.0 为 Fair AI Public License 1.0-SD）。
- 中文字体使用 **Noto Serif SC（SIL OFL 1.1）**，可商用；已放入 `Assets/Fonts`。
- 原作《斯泰尔斯庄园奇案》出版于 1920 年，在美国已进入公有领域；本作为粉丝向改编，
  商业发行前请自行确认当地版权与商标规定。


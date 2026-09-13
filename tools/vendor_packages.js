/*
 * 把 uGUI / TextMeshPro / Newtonsoft.Json 直接内置进 Assets，使工程完全不依赖 Unity Package Manager。
 * 用法: node tools/vendor_packages.js
 *
 * 背景：本机 Unity 的 UPM 进程启动即崩溃（project:update-dependencies 500），
 * 因此改为「包源码内置到 Assets」的方案：任何一台机器、离线都能打开并编译。
 * 若将来 UPM 恢复正常，可删除 Assets/ThirdParty 与 Assets/TextMesh Pro，
 * 并把 Packages/manifest.full.json 还原为 manifest.json 使用官方包。
 */
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const ROOT = path.resolve(__dirname, '..');
const PKG = path.join(ROOT, 'Packages');
const ASSETS = path.join(ROOT, 'Assets');
const WORK = path.join(ROOT, 'ToolsOut', 'vendor');
// uGUI 源码的位置：优先用环境变量，否则从本工程的 PackageCache 里找。
// （以前这里硬编码了开发机上的绝对路径，换台机器就跑不通，也不该出现在公开仓库里。）
const UGUI_SRC = process.env.UGUI_SRC ||
  path.join(ROOT, 'Library', 'PackageCache', 'com.unity.ugui@1.0.0');
if (!fs.existsSync(path.join(UGUI_SRC, 'Runtime'))) {
  console.error('找不到 uGUI 源码：' + UGUI_SRC);
  console.error('请设置环境变量 UGUI_SRC，指向 Unity 的 com.unity.ugui@1.0.0 目录，例如：');
  console.error('  正常联网的工程： <项目>/Library/PackageCache/com.unity.ugui@1.0.0');
  console.error('  或 Unity 安装目录： <Unity>/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.ugui');
  process.exit(1);
}

const copyTree = (src, dst, filter) => {
  if (!fs.existsSync(src)) { console.warn('缺少源目录: ' + src); return 0; }
  let n = 0;
  for (const e of fs.readdirSync(src, { withFileTypes: true })) {
    const s = path.join(src, e.name), d = path.join(dst, e.name);
    if (filter && !filter(s, e)) continue;
    if (e.isDirectory()) { fs.mkdirSync(d, { recursive: true }); n += copyTree(s, d, filter); }
    else { fs.mkdirSync(dst, { recursive: true }); fs.copyFileSync(s, d); n++; }
  }
  return n;
};
// 保留 .asmdef：TextMeshPro 必须以 Unity.TextMeshPro 程序集名编译，
// 才能获得 UnityEngine.TextCore 的内部访问权限（friend assembly）。
// 保留 .meta：GUID 必须与原包一致，否则 TMP Settings / 字体 / 着色器之间的引用会断。
const keepAll = () => true;
const rm = (p) => { if (fs.existsSync(p)) fs.rmSync(p, { recursive: true, force: true }); };

// ---------- 1. uGUI ----------
rm(path.join(ASSETS, 'ThirdParty/uGUI'));
let n = copyTree(path.join(UGUI_SRC, 'Runtime'), path.join(ASSETS, 'ThirdParty/uGUI'), keepAll);
console.log('uGUI 源码文件: ' + n);

// ---------- 2. TextMeshPro 运行时脚本 ----------
rm(path.join(ASSETS, 'ThirdParty/TextMeshPro'));
n = copyTree(path.join(PKG, 'com.unity.textmeshpro/Scripts/Runtime'),
             path.join(ASSETS, 'ThirdParty/TextMeshPro/Scripts'), keepAll);
console.log('TMP 运行时脚本: ' + n);

// ---------- 3. TMP Essential Resources（TMP Settings / 着色器 / 默认字体）----------
const unitypackage = path.join(PKG, 'com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage');
if (fs.existsSync(unitypackage)) {
  rm(WORK);
  fs.mkdirSync(WORK, { recursive: true });
  execFileSync('tar', ['-xzf', unitypackage, '-C', WORK], { stdio: 'inherit' });
  let count = 0;
  for (const guid of fs.readdirSync(WORK)) {
    const dir = path.join(WORK, guid);
    const pn = path.join(dir, 'pathname');
    const asset = path.join(dir, 'asset');
    if (!fs.existsSync(pn) || !fs.existsSync(asset)) continue;
    const rel = fs.readFileSync(pn, 'utf8').trim();
    if (!rel.startsWith('Assets/')) continue;
    const dst = path.join(ROOT, rel);
    fs.mkdirSync(path.dirname(dst), { recursive: true });
    fs.copyFileSync(asset, dst);
    // 同步写回 .meta，保证 GUID 与包内一致（TMP Settings 依赖这些引用）
    const meta = path.join(dir, 'asset.meta');
    if (fs.existsSync(meta)) fs.copyFileSync(meta, dst + '.meta');
    count++;
  }
  console.log('TMP Essential Resources 解包文件: ' + count);
} else {
  console.warn('未找到 TMP Essential Resources.unitypackage');
}

// ---------- 4. Newtonsoft.Json ----------
const dll = path.join(PKG, 'com.unity.nuget.newtonsoft-json/Runtime/Newtonsoft.Json.dll');
fs.mkdirSync(path.join(ASSETS, 'Plugins'), { recursive: true });
if (fs.existsSync(dll)) { fs.copyFileSync(dll, path.join(ASSETS, 'Plugins/Newtonsoft.Json.dll')); console.log('Newtonsoft.Json.dll 已放入 Assets/Plugins'); }
const aot = path.join(PKG, 'com.unity.nuget.newtonsoft-json/Runtime/AOT');
if (fs.existsSync(aot)) {
  for (const f of fs.readdirSync(aot)) {
    if (f.endsWith('.xml')) { fs.copyFileSync(path.join(aot, f), path.join(ASSETS, 'Plugins', f)); console.log('IL2CPP 保护文件: ' + f); }
  }
}

// ---------- 5. 关闭包依赖 ----------
const manifest = {
  dependencies: {
    'com.unity.modules.androidjni': '1.0.0',
    'com.unity.modules.audio': '1.0.0',
    'com.unity.modules.imageconversion': '1.0.0',
    'com.unity.modules.imgui': '1.0.0',
    'com.unity.modules.jsonserialize': '1.0.0',
    'com.unity.modules.screencapture': '1.0.0',
    'com.unity.modules.ui': '1.0.0',
    'com.unity.modules.uielements': '1.0.0',
    'com.unity.modules.unitywebrequest': '1.0.0',
    'com.unity.modules.unitywebrequesttexture': '1.0.0',
    'com.unity.modules.video': '1.0.0'
  }
};
fs.writeFileSync(path.join(PKG, 'manifest.json'), JSON.stringify(manifest, null, 2));
rm(path.join(PKG, 'packages-lock.json'));
console.log('manifest.json 已改为仅内置模块（不依赖 UPM 网络包）');
console.log('完成。');

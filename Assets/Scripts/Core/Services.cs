using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;

namespace Styles.Core
{
    /// <summary>玩家设置：文字速度、自动播放间隔、音量、全屏、语言。持久化到 persistentDataPath。</summary>
    [Serializable]
    public class GameSettings
    {
        public float textSpeed = 0.018f;     // 每字秒数
        public float autoDelay = 1.5f;
        public float bgmVolume = 0.7f;
        public float sfxVolume = 0.8f;
        public float voiceVolume = 1.0f;
        public bool fullscreen = true;
        public bool skipUnread = false;
        public bool reduceMotion = false;
        public string language = "zh-CN";
        public int quality = 2;              // 0 低 / 1 中 / 2 高 / 3 极高
    }

    public static class SettingsService
    {
        static GameSettings _current;
        static string PathFile { get { return Path.Combine(Application.persistentDataPath, "settings.json"); } }

        public static GameSettings Current
        {
            get
            {
                if (_current != null) return _current;
                try
                {
                    if (File.Exists(PathFile)) _current = JsonConvert.DeserializeObject<GameSettings>(File.ReadAllText(PathFile));
                }
                catch (Exception e) { Debug.LogWarning("[Styles] 设置读取失败：" + e.Message); }
                if (_current == null) _current = new GameSettings();
                return _current;
            }
        }

        public static void Save()
        {
            try { File.WriteAllText(PathFile, JsonConvert.SerializeObject(Current, Formatting.Indented)); }
            catch (Exception e) { Debug.LogWarning("[Styles] 设置写入失败：" + e.Message); }
            Apply();
        }

        public static void Apply()
        {
            var s = Current;
            QualitySettings.SetQualityLevel(Mathf.Clamp(s.quality, 0, QualitySettings.names.Length - 1), true);
            Screen.fullScreen = s.fullscreen;
            Application.targetFrameRate = s.quality >= 2 ? 60 : 30;
            var ad = AudioDirector.Instance;
            if (ad != null) ad.ApplyVolumes();
        }
    }

    /// <summary>音频：BGM 交叉淡入淡出 + 一次性音效。素材放在 Resources/Audio。</summary>
    public class AudioDirector : MonoBehaviour
    {
        public static AudioDirector Instance { get; private set; }
        AudioSource _bgmA, _bgmB, _sfx, _voice;
        bool _useA = true;
        float _fade = -1f;
        AudioClip _pending;

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _bgmA = gameObject.AddComponent<AudioSource>(); _bgmA.loop = true;
            _bgmB = gameObject.AddComponent<AudioSource>(); _bgmB.loop = true;
            _sfx = gameObject.AddComponent<AudioSource>();
            _voice = gameObject.AddComponent<AudioSource>();
            ApplyVolumes();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void ApplyVolumes()
        {
            var s = SettingsService.Current;
            _bgmA.volume = s.bgmVolume; _bgmB.volume = 0f;
            _sfx.volume = s.sfxVolume; _voice.volume = s.voiceVolume;
        }

        public void PlayBgm(string id, float fade = 1.2f, bool loop = true)
        {
            if (string.IsNullOrEmpty(id)) { StopBgm(fade); return; }
            var clip = Resources.Load<AudioClip>("Audio/BGM/" + id);
            if (clip == null) { Debug.LogWarning("[Styles] 缺少 BGM: " + id); return; }
            var from = _useA ? _bgmA : _bgmB;
            var to = _useA ? _bgmB : _bgmA;
            if (from.clip == clip && from.isPlaying) return;
            to.clip = clip; to.loop = loop; to.volume = 0f; to.Play();
            _pending = clip; _fade = 0f;
            _useA = !_useA;
        }

        public void StopBgm(float fade = 1.2f)
        {
            _pending = null; _fade = 0f;
        }

        public void PlaySfx(string id)
        {
            var clip = Resources.Load<AudioClip>("Audio/SFX/" + id);
            if (clip != null) _sfx.PlayOneShot(clip, SettingsService.Current.sfxVolume);
        }

        public void PlayVoice(string id)
        {
            var clip = Resources.Load<AudioClip>("Audio/Voice/" + id);
            if (clip == null) return;
            _voice.Stop(); _voice.clip = clip; _voice.Play();
        }

        void Update()
        {
            if (_fade < 0f) return;
            _fade += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_fade / 1.2f);
            var s = SettingsService.Current;
            var a = _useA ? _bgmA : _bgmB;   // 当前（新）
            var b = _useA ? _bgmB : _bgmA;   // 旧
            if (_pending != null) { a.volume = s.bgmVolume * t; b.volume = s.bgmVolume * (1f - t); if (t >= 1f) { b.Stop(); _fade = -1f; } }
            else { b.volume = s.bgmVolume * (1f - t); a.volume = s.bgmVolume * (1f - t); if (t >= 1f) { a.Stop(); b.Stop(); _fade = -1f; } }
        }
    }

    /// <summary>图片资源：命名约定 + 缺图占位 + 缓存。</summary>
    public static class AssetService
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        static Sprite _missing;

        public static Sprite Background(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Load("Art/Backgrounds/" + id);
        }

        public static Sprite Cg(string id) { return Load("Art/CG/" + id); }

        public static Sprite Portrait(string characterId, string emotion)
        {
            if (string.IsNullOrEmpty(characterId)) return null;
            var e = string.IsNullOrEmpty(emotion) ? "neutral" : emotion;
            var s = Load("Art/Characters/" + characterId + "_" + e);
            if (s != null && s != Missing()) return s;
            s = Load("Art/Characters/" + characterId + "_neutral");
            if (s != null && s != Missing()) return s;
            s = Load("Art/Characters/" + characterId);
            return s;
        }

        public static Sprite Ui(string id) { return Load("Art/UI/" + id); }

        public static Sprite Load(string resourcesPath)
        {
            Sprite cached;
            if (Cache.TryGetValue(resourcesPath, out cached)) return cached;
            var s = Resources.Load<Sprite>(resourcesPath);
            if (s == null) s = Missing();
            Cache[resourcesPath] = s;
            return s;
        }

        public static void ClearCache() { Cache.Clear(); Resources.UnloadUnusedAssets(); }

        public static Sprite Missing()
        {
            if (_missing != null) return _missing;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(60, 20, 60, 255);
            tex.SetPixels32(px); tex.Apply();
            _missing = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 100f);
            _missing.name = "__missing__";
            return _missing;
        }
    }

    /// <summary>中文字体：编辑器阶段生成 TMP 字体资源，运行时按需加载。</summary>
    public static class FontService
    {
        static TMP_FontAsset _main;
        public static TMP_FontAsset Main
        {
            get
            {
                if (_main != null) return _main;
                _main = Resources.Load<TMP_FontAsset>("Fonts/StylesSerif SDF");
                if (_main == null) _main = TMP_Settings.defaultFontAsset;
                if (_main == null)
                {
                    var osFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimSun", "Noto Sans CJK SC", "Arial" }, 32);
                    if (osFont != null) _main = TMP_FontAsset.CreateFontAsset(osFont);
                }
                return _main;
            }
        }
    }
}

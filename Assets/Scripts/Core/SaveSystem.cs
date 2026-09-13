using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Styles.Core
{
    [Serializable]
    public class SlotMeta
    {
        public int slot;
        public bool used;
        public string chapter = "";
        public string time = "";
        public int clues;
        public int score;
        public int beats;
    }

    /// <summary>存档：3 个手动槽 + 1 个自动槽；写入采用临时文件 + 替换，避免断电损坏。</summary>
    public static class SaveSystem
    {
        public const int AutoSlot = 0;
        public const int SlotCount = 4;

        static string Dir { get { return Path.Combine(Application.persistentDataPath, "saves"); } }
        static string PathOf(int slot) { return Path.Combine(Dir, "slot" + slot + ".json"); }

        public static void Save(int slot, GameState state)
        {
            if (state == null) return;
            Directory.CreateDirectory(Dir);
            state.savedAtUtc = DateTime.UtcNow.ToString("o");
            var json = JsonConvert.SerializeObject(state, Formatting.Indented);
            var tmp = PathOf(slot) + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(PathOf(slot))) File.Delete(PathOf(slot));
            File.Move(tmp, PathOf(slot));
        }

        public static GameState Load(int slot)
        {
            try
            {
                var p = PathOf(slot);
                if (!File.Exists(p)) return null;
                var st = JsonConvert.DeserializeObject<GameState>(File.ReadAllText(p));
                if (st == null) return null;
                if (st.version < 1) st.version = 1;
                if (st.clues == null) st.clues = new System.Collections.Generic.List<string>();
                if (st.flags == null) st.flags = new System.Collections.Generic.Dictionary<string, int>();
                if (st.topics == null) st.topics = new System.Collections.Generic.Dictionary<string, bool>();
                if (st.spots == null) st.spots = new System.Collections.Generic.Dictionary<string, bool>();
                if (st.log == null) st.log = new System.Collections.Generic.List<LogLine>();
                if (st.accuse == null) st.accuse = new System.Collections.Generic.List<AccuseRecord>();
                return st;
            }
            catch (Exception e) { Debug.LogError("[Styles] 存档读取失败: " + e.Message); return null; }
        }

        public static bool Exists(int slot) { return File.Exists(PathOf(slot)); }

        public static SlotMeta Meta(int slot)
        {
            var st = Load(slot);
            if (st == null) return new SlotMeta { slot = slot, used = false };
            var t = DateTime.TryParse(st.savedAtUtc, out var dt) ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "";
            return new SlotMeta { slot = slot, used = true, chapter = st.chapterId, time = t, clues = st.clues.Count, score = st.score, beats = st.beatIndex };
        }

        public static void Delete(int slot)
        {
            var p = PathOf(slot);
            if (File.Exists(p)) File.Delete(p);
        }
    }
}

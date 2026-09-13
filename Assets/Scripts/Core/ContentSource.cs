using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Styles.Core
{
    /// <summary>内容读取抽象：桌面端直接读文件，Android / iOS / WebGL 走 UnityWebRequest。</summary>
    public interface IContentSource
    {
        string ReadText(string relativePath);
        T ReadJson<T>(string relativePath) where T : class;
        bool Exists(string relativePath);
    }

    public class StreamingAssetsContentSource : IContentSource
    {
        readonly string _root;
        readonly bool _streaming;

        public StreamingAssetsContentSource(string subFolder = "content")
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _streaming = true;
            _root = subFolder;
#else
            _streaming = false;
            _root = Path.Combine(Application.streamingAssetsPath, subFolder);
#endif
        }

        public bool Exists(string relativePath)
        {
            if (_streaming) return true;
            return File.Exists(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        public string ReadText(string relativePath)
        {
            if (_streaming)
            {
                var url = Application.streamingAssetsPath + "/" + _root + "/" + relativePath;
                using (var req = UnityWebRequest.Get(url))
                {
                    var op = req.SendWebRequest();
                    while (!op.isDone) { }
                    if (req.result != UnityWebRequest.Result.Success)
                        throw new Exception("[Styles] 读取失败 " + relativePath + " : " + req.error);
                    return req.downloadHandler.text;
                }
            }
            return File.ReadAllText(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        public T ReadJson<T>(string relativePath) where T : class
        {
            if (!Exists(relativePath)) return null;
            var text = ReadText(relativePath);
            return string.IsNullOrWhiteSpace(text) ? null : JsonConvert.DeserializeObject<T>(text);
        }
    }
}

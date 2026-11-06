using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

public static class ElderboomAssetGrab
{
    const string InfoUrl = "https://packages-v2.unity.com/-/api/legacy-package-download-info/336744";
    const string PackagePath = "Temp/FreeZombieMaleAAB.unitypackage";

    public static void Install()
    {
        var before = new HashSet<string>(AssetDatabase.GetAllAssetPaths());
        if (!AlreadyPresent(before))
            ImportPackage(DownloadPackage());
        var added = AssetDatabase.GetAllAssetPaths().Where(p => !before.Contains(p)).ToList();
        Directory.CreateDirectory("Logs");
        File.WriteAllLines("Logs/aab_imported.txt", added);
        ElderboomZombieRig.Rebuild();
        ElderboomZombieRig.AdoptStoreZombie(added);
        HDRPSliceStandaloneBuild.BuildPort();
        Debug.Log("ELDERBOOM_AAB_INSTALLED " + added.Count);
    }

    static bool AlreadyPresent(HashSet<string> paths)
    {
        return paths.Any(p => p.IndexOf("Zombie Male AAB", StringComparison.OrdinalIgnoreCase) >= 0
            || p.IndexOf("FREE Zombie Male", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    static string DownloadPackage()
    {
        string token = CloudProjectSettings.accessToken;
        if (string.IsNullOrEmpty(token))
            token = SessionToken();
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("ELDERBOOM_ASSET_NO_TOKEN");
        Debug.Log("ELDERBOOM_TOKEN_LENGTH " + token.Length);

        string info = Get(InfoUrl, token);
        if (info.IndexOf("url", StringComparison.OrdinalIgnoreCase) < 0)
        {
            Post("https://packages-v2.unity.com/-/api/purchases", token, "{\"id\":\"336744\"}");
            info = Get(InfoUrl, token);
        }
        string url = FirstUrl(info);
        if (string.IsNullOrEmpty(url))
            throw new InvalidOperationException("ELDERBOOM_ASSET_NO_URL " + Redact(info));
        Directory.CreateDirectory("Temp");
        var request = UnityWebRequest.Get(url);
        request.downloadHandler = new DownloadHandlerFile(PackagePath);
        Send(request);
        if (request.result != UnityWebRequest.Result.Success)
            throw new InvalidOperationException("ELDERBOOM_ASSET_DOWNLOAD_FAILED " + request.responseCode);
        Debug.Log("ELDERBOOM_ASSET_DOWNLOADED " + new FileInfo(PackagePath).Length);
        return PackagePath;
    }

    static void ImportPackage(string path)
    {
        AssetDatabase.ImportPackage(path, false);
        AssetDatabase.Refresh();
    }

    static string SessionToken()
    {
        var type = Type.GetType("UnityEditor.Connect.UnityConnect, UnityEditor");
        var instance = type?.GetProperty("instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
        var method = type?.GetMethod("GetAccessToken", Type.EmptyTypes);
        return method?.Invoke(instance, null) as string;
    }

    static string Get(string url, string token)
    {
        var request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Authorization", "Bearer " + token);
        Send(request);
        Debug.Log("ELDERBOOM_ASSET_HTTP " + request.responseCode);
        if (request.result != UnityWebRequest.Result.Success && request.responseCode == 0)
            throw new InvalidOperationException("ELDERBOOM_ASSET_HTTP_FAILED");
        return request.downloadHandler.text ?? "";
    }

    static void Post(string url, string token, string body)
    {
        var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Authorization", "Bearer " + token);
        request.SetRequestHeader("Content-Type", "application/json");
        Send(request);
        Debug.Log("ELDERBOOM_ASSET_PURCHASE " + request.responseCode);
    }

    static void Send(UnityWebRequest request)
    {
        request.SendWebRequest();
        while (!request.isDone) {}
    }

    static string FirstUrl(string json)
    {
        int search = 0;
        while (search < json.Length)
        {
            int i = json.IndexOf("https://", search, StringComparison.Ordinal);
            if (i < 0) return null;
            int end = i;
            while (end < json.Length && json[end] != '"' && json[end] != ' ' && json[end] != '\\') end++;
            string url = json.Substring(i, end - i).Replace("\\u0026", "&").Replace("\\/", "/");
            if (url.Contains("unitypackage") || url.Contains("download") || url.Contains("amazonaws") || url.Contains("cloudfront"))
                return url;
            search = end + 1;
        }
        return null;
    }

    static string Redact(string json)
    {
        if (string.IsNullOrEmpty(json)) return "empty";
        var keys = new List<string>();
        int i = 0;
        while (i < json.Length && keys.Count < 12)
        {
            int q = json.IndexOf('"', i);
            if (q < 0 || q + 1 >= json.Length) break;
            int q2 = json.IndexOf('"', q + 1);
            if (q2 < 0) break;
            string word = json.Substring(q + 1, q2 - q - 1);
            if (word.Length > 0 && word.Length < 40 && json.IndexOf(':', q2) >= 0 && json.IndexOf(':', q2) < q2 + 3)
                keys.Add(word);
            i = q2 + 1;
        }
        return string.Join(",", keys);
    }
}

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Text.RegularExpressions;

class WeddingServer {
    static readonly string ROOT = AppDomain.CurrentDomain.BaseDirectory;
    const string UA_ANDROID = "Mozilla/5.0 (Linux; Android 11; M2007J3SC Build/RKQ1.200826.002; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/77.0.3865.120 MQQBrowser/6.2 TBS/045714 Mobile Safari/537.36 NeteaseMusic/8.7.01";
    const string EAPI_KEY = "e82ckenh8dichen8";
    static readonly Random Rnd = new Random();
    static readonly Dictionary<string, string> QrKeyCookies = new Dictionary<string, string>();

    /* ---------- EAPI 加密（metowolf/Meting 方案） ---------- */
    static string EapiEncrypt(string urlPath, string text) {
        string msg = "nobody" + urlPath + "use" + text + "md5forencrypt";
        byte[] digest = MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(msg));
        string digestHex = BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
        string data = urlPath + "-36cd479b6b5-" + text + "-36cd479b6b5-" + digestHex;
        byte[] key = Encoding.UTF8.GetBytes(EAPI_KEY);
        byte[] plain = Encoding.UTF8.GetBytes(data);
        // 手动 PKCS7 填充（部分系统 Aes.PKCS7 填充结果异常，手动填充保证与标准一致）
        int padLen = 16 - (plain.Length % 16);
        byte[] padded = new byte[plain.Length + padLen];
        Array.Copy(plain, padded, plain.Length);
        for (int i = plain.Length; i < padded.Length; i++) padded[i] = (byte)padLen;
        Aes aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using (ICryptoTransform enc = aes.CreateEncryptor()) {
            byte[] encBytes = enc.TransformFinalBlock(padded, 0, padded.Length);
            return BitConverter.ToString(encBytes).Replace("-", "").ToUpperInvariant();
        }
    }

    static string RandomHex(int bytes) {
        byte[] b = new byte[bytes];
        Rnd.NextBytes(b);
        StringBuilder sb = new StringBuilder();
        foreach (byte x in b) sb.Append(x.ToString("X2"));
        return sb.ToString();
    }

    static byte[] EapiCall(string apiPath, string jsonBody, string extraCookie) {
        string deviceId = RandomHex(16);
        string ts = DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString();
        string requestId = ts + "_" + Rnd.Next(0, 1000).ToString("D4");
        string cookie = "osver=android; appver=8.7.01; os=android; deviceId=" + deviceId +
                        "; channel=netease; requestId=" + requestId + "; __remember_me=true";
        if (!string.IsNullOrEmpty(extraCookie)) cookie += "; " + extraCookie;
        string form = "params=" + EapiEncrypt(apiPath, jsonBody);
        string eapiUrl = "http://music.163.com/eapi/" + apiPath.Substring("/api/".Length);
        return FetchPost(eapiUrl, form, "music.163.com", cookie, UA_ANDROID);
    }

    /* ---------- HTTP 工具 ---------- */
    static byte[] FetchGet(string url, string referer, string cookie) {
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
        req.UserAgent = UA_ANDROID;
        req.Timeout = 20000;
        if (!string.IsNullOrEmpty(referer)) req.Referer = referer;
        if (!string.IsNullOrEmpty(cookie)) req.Headers["Cookie"] = cookie;
        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
        using (Stream s = resp.GetResponseStream())
        using (MemoryStream ms = new MemoryStream()) {
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }

    static byte[] FetchPost(string url, string formBody, string referer, string cookie, string ua) {
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = "POST";
        req.UserAgent = ua;
        req.ContentType = "application/x-www-form-urlencoded";
        req.Accept = "*/*";
        req.ServicePoint.Expect100Continue = false;
        req.Timeout = 20000;
        if (!string.IsNullOrEmpty(referer)) req.Referer = referer;
        if (!string.IsNullOrEmpty(cookie)) req.Headers["Cookie"] = cookie;
        byte[] data = Encoding.UTF8.GetBytes(formBody);
        req.ContentLength = data.Length;
        using (Stream ws = req.GetRequestStream()) ws.Write(data, 0, data.Length);
        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
        using (Stream s = resp.GetResponseStream())
        using (MemoryStream ms = new MemoryStream()) {
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }

    static string FetchPostFull(string url, string formBody, string referer, string cookie, string ua) {
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = "POST";
        req.UserAgent = ua;
        req.ContentType = "application/x-www-form-urlencoded";
        req.Accept = "*/*";
        req.ServicePoint.Expect100Continue = false;
        req.Timeout = 20000;
        if (!string.IsNullOrEmpty(referer)) req.Referer = referer;
        if (!string.IsNullOrEmpty(cookie)) req.Headers["Cookie"] = cookie;
        byte[] data = Encoding.UTF8.GetBytes(formBody);
        req.ContentLength = data.Length;
        using (Stream ws = req.GetRequestStream()) ws.Write(data, 0, data.Length);
        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) {
            string sc = resp.Headers["Set-Cookie"];
            using (Stream s = resp.GetResponseStream())
            using (MemoryStream ms = new MemoryStream()) {
                s.CopyTo(ms);
                return Encoding.UTF8.GetString(ms.ToArray()) + "\n__SETCOOKIE__" + (sc ?? "");
            }
        }
    }
    static string ExtractCookie(string all, string name) {
        if (string.IsNullOrEmpty(all)) return "";
        Match m = Regex.Match(all, name + "=([^;,\\s]+)");
        return m.Success ? m.Value : "";
    }
    static byte[] HandleQrKey() {
        string deviceId = RandomHex(16);
        string ts = DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString();
        string requestId = ts + "_" + Rnd.Next(0, 1000).ToString("D4");
        string cookie = "osver=android; appver=8.7.01; os=android; deviceId=" + deviceId +
                        "; channel=netease; requestId=" + requestId + "; __remember_me=true";
        string form = "params=" + EapiEncrypt("/api/login/qrcode/unikey", "{\"type\":1}");
        string resp = FetchPostFull("http://music.163.com/eapi/login/qrcode/unikey", form, "music.163.com", cookie, UA_ANDROID);
        string setCookie = resp.Substring(resp.IndexOf("\n__SETCOOKIE__") + 14);
        string body = resp.Substring(0, resp.IndexOf("\n__SETCOOKIE__"));
        Match m = Regex.Match(body, "\"unikey\":\"([^\"]+)\"");
        string unikey = m.Success ? m.Groups[1].Value : "";
        string nmtid = ExtractCookie(setCookie, "NMTID");
        if (!string.IsNullOrEmpty(unikey)) { lock (QrKeyCookies) QrKeyCookies[unikey] = nmtid; }
        return Encoding.UTF8.GetBytes("{\"code\":200,\"unikey\":\"" + unikey + "\",\"qrUrl\":\"https://music.163.com/login?codekey=" + unikey + "\"}");
    }
    static string TxSign(string payload) {
        byte[] hb = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(payload));
        string hash = BitConverter.ToString(hb).Replace("-", "").ToUpperInvariant();
        int[] part1Idx = { 23, 14, 6, 36, 16, 7, 19 };
        int[] part2Idx = { 16, 1, 32, 12, 19, 27, 8, 5 };
        int[] scramble = { 89, 39, 179, 150, 218, 82, 58, 252, 177, 52, 186, 123, 120, 64, 242, 133, 143, 161, 121, 179 };
        StringBuilder part1 = new StringBuilder();
        foreach (int i in part1Idx) part1.Append(hash[i]);
        StringBuilder part2 = new StringBuilder();
        foreach (int i in part2Idx) part2.Append(hash[i]);
        byte[] part3 = new byte[20];
        for (int i = 0; i < 20; i++) part3[i] = (byte)(scramble[i] ^ Convert.ToInt32(hash.Substring(i * 2, 2), 16));
        string b64 = Convert.ToBase64String(part3).Replace("/", "").Replace("+", "").Replace("=", "");
        return ("zzc" + part1.ToString() + b64 + part2.ToString()).ToLowerInvariant();
    }
    static byte[] FetchPostJson(string url, string jsonBody) {
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = "POST";
        req.UserAgent = UA_ANDROID;
        req.ContentType = "application/json";
        req.Accept = "*/*";
        req.ServicePoint.Expect100Continue = false;
        req.Timeout = 20000;
        byte[] data = Encoding.UTF8.GetBytes(jsonBody);
        req.ContentLength = data.Length;
        using (Stream ws = req.GetRequestStream()) ws.Write(data, 0, data.Length);
        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
        using (Stream s = resp.GetResponseStream())
        using (MemoryStream ms = new MemoryStream()) {
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }
    static string QqMediaMid(string songmid, string uin, string token) {
        // 歌曲详情接口用游客身份即可，避免 auth 触发异常响应
        string authJson = ",\"qq\":\"0\"";
        string commJson = "{\"v\":14090008,\"ct\":11,\"cv\":14090008,\"chid\":\"2005000982\",\"QIMEI\":\"\",\"QIMEI36\":\"\",\"tmeAppID\":\"qqmusic\",\"format\":\"json\",\"inCharset\":\"utf-8\",\"outCharset\":\"utf-8\",\"OpenUDID\":\"ffffffffbff94f7d000000000033c587\",\"udid\":\"ffffffffbff94f7d000000000033c587\",\"os_ver\":\"10\",\"aid\":\"d2550265db4ce5c4\",\"phonetype\":\"MI 6\",\"devicelevel\":29,\"newdevicelevel\":29,\"nettype\":\"1030\",\"rom\":\"xiaomi/iarim/sagit:10/eomam.200122.001/1:user/release-keys\",\"OpenUDID2\":\"ffffffffbff94f7d000001999ff7d5bf\"" + authJson + "}";
        string reqJson = "{\"comm\":" + commJson + ",\"req\":{\"module\":\"music.pf_song_detail_svr\",\"method\":\"get_song_detail_yqq\",\"param\":{\"song_type\":0,\"song_mid\":\"" + songmid + "\"}}}";
        string sign = TxSign(reqJson);
        byte[] body = FetchPostJson("https://u.y.qq.com/cgi-bin/musics.fcg?sign=" + sign, reqJson);
        string json = Encoding.UTF8.GetString(body);
        Match m = Regex.Match(json, "\"media_mid\":\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : "";
    }
    static byte[] HandleQqLyric(string songmid) {
        try {
            string guestAuth = ",\"qq\":\"0\"";
            string commJson = "{\"v\":14090008,\"ct\":11,\"cv\":14090008,\"chid\":\"2005000982\",\"QIMEI\":\"\",\"QIMEI36\":\"\",\"tmeAppID\":\"qqmusic\",\"format\":\"json\",\"inCharset\":\"utf-8\",\"outCharset\":\"utf-8\",\"OpenUDID\":\"ffffffffbff94f7d000000000033c587\",\"udid\":\"ffffffffbff94f7d000000000033c587\",\"os_ver\":\"10\",\"aid\":\"d2550265db4ce5c4\",\"phonetype\":\"MI 6\",\"devicelevel\":29,\"newdevicelevel\":29,\"nettype\":\"1030\",\"rom\":\"xiaomi/iarim/sagit:10/eomam.200122.001/1:user/release-keys\",\"OpenUDID2\":\"ffffffffbff94f7d000001999ff7d5bf\"" + guestAuth + "}";
            // 1) songmid → 数字 id
            string detailJson = "{\"comm\":" + commJson + ",\"req\":{\"module\":\"music.pf_song_detail_svr\",\"method\":\"get_song_detail_yqq\",\"param\":{\"song_type\":0,\"song_mid\":\"" + songmid + "\"}}}";
            byte[] dBody = FetchPostJson("https://u.y.qq.com/cgi-bin/musics.fcg?sign=" + TxSign(detailJson), detailJson);
            Match idM = Regex.Match(Encoding.UTF8.GetString(dBody), "\"track_info\":\\{\"id\":(\\d+)");
            string id = idM.Success ? idM.Groups[1].Value : "";
            if (id.Length == 0) return Encoding.UTF8.GetBytes("{\"lyric\":\"\",\"debug\":\"id-fail\"}");
            // 2) 获取歌词（crypt:0 返回 base64 明文）
            string lyricJson = "{\"comm\":" + commJson + ",\"req\":{\"method\":\"GetPlayLyricInfo\",\"module\":\"music.musichallSong.PlayLyricInfo\",\"param\":{\"format\":\"json\",\"crypt\":0,\"ct\":19,\"cv\":1873,\"interval\":0,\"lrc_t\":0,\"qrc\":0,\"qrc_t\":0,\"roma\":0,\"roma_t\":0,\"songID\":" + id + ",\"trans\":1,\"trans_t\":1,\"type\":-1}}}";
            byte[] lBody = FetchPostJson("https://u.y.qq.com/cgi-bin/musics.fcg?sign=" + TxSign(lyricJson), lyricJson);
            string lJson = Encoding.UTF8.GetString(lBody);
            string lrc = "";
            Match lm = Regex.Match(lJson, "\"lyric\":\"([^\"]*)\"");
            if (lm.Success && lm.Groups[1].Value.Length > 0) {
                try { byte[] decoded = Convert.FromBase64String(lm.Groups[1].Value); lrc = Encoding.UTF8.GetString(decoded); } catch { }
            }
            string trans = "";
            Match tm = Regex.Match(lJson, "\"trans\":\"([^\"]*)\"");
            if (tm.Success && tm.Groups[1].Value.Length > 0) {
                try { byte[] decoded = Convert.FromBase64String(tm.Groups[1].Value); trans = Encoding.UTF8.GetString(decoded); } catch { }
            }
            string esc = lrc.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
            string escT = trans.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
            return Encoding.UTF8.GetBytes("{\"lyric\":\"" + esc + "\",\"trans\":\"" + escT + "\"}");
        } catch (Exception e) {
            return Encoding.UTF8.GetBytes("{\"lyric\":\"\",\"error\":\"" + Json(e.Message) + "\"}");
        }
    }
    static string QqPurl(string songmid, string mediaMid, string uin, string token) {
        string authJson;
        if (uin != "0" && token.Length > 0) {
            authJson = ",\"qq\":\"" + uin + "\",\"authst\":\"" + token + "\",\"tmeLoginType\":" + (token.StartsWith("W_X_") ? 1 : 2);
        } else {
            authJson = ",\"qq\":\"0\"";
        }
        string commJson = "{\"v\":14090008,\"ct\":11,\"cv\":14090008,\"chid\":\"2005000982\",\"QIMEI\":\"\",\"QIMEI36\":\"\",\"tmeAppID\":\"qqmusic\",\"format\":\"json\",\"inCharset\":\"utf-8\",\"outCharset\":\"utf-8\",\"OpenUDID\":\"ffffffffbff94f7d000000000033c587\",\"udid\":\"ffffffffbff94f7d000000000033c587\",\"os_ver\":\"10\",\"aid\":\"d2550265db4ce5c4\",\"phonetype\":\"MI 6\",\"devicelevel\":29,\"newdevicelevel\":29,\"nettype\":\"1030\",\"rom\":\"xiaomi/iarim/sagit:10/eomam.200122.001/1:user/release-keys\",\"OpenUDID2\":\"ffffffffbff94f7d000001999ff7d5bf\"" + authJson + "}";
        string requestJson = "{\"comm\":" + commJson + ",\"request\":{\"module\":\"music.vkey.GetVkey\",\"method\":\"UrlGetVkey\",\"param\":{\"guid\":\"Feixiao\",\"uin\":\"" + uin + "\",\"downloadfrom\":1,\"ctx\":1,\"referer\":\"y.qq.com\",\"scene\":0,\"songtype\":[1],\"songmid\":[\"" + songmid + "\"],\"filename\":[\"M500" + mediaMid + ".mp3\"]}}}";
        string sign = TxSign(requestJson);
        byte[] body = FetchPostJson("https://u.y.qq.com/cgi-bin/musics.fcg?sign=" + sign, requestJson);
        string json = Encoding.UTF8.GetString(body);
        Match pm = Regex.Match(json, "\"purl\":\"([^\"]*)\"");
        return pm.Success ? pm.Groups[1].Value : "";
    }
    static byte[] HandleQqPlayUrl(string songmid, string mediaMid, string qcookie) {
        try {
            string uin = "0";
            Match um = Regex.Match(qcookie, "uin=o?(\\d+)");
            if (um.Success) uin = um.Groups[1].Value;
            string token = "";
            Match tm = Regex.Match(qcookie, "qqmusic_key=([^;]+)");
            if (!tm.Success) tm = Regex.Match(qcookie, "qm_keyst=([^;]+)");
            if (tm.Success) token = tm.Groups[1].Value;
            if (string.IsNullOrEmpty(mediaMid)) {
                mediaMid = QqMediaMid(songmid, uin, token);
                if (string.IsNullOrEmpty(mediaMid)) mediaMid = songmid;
            }
            string purl = QqPurl(songmid, mediaMid, uin, token);
            string reason = "";
            if (purl.Length == 0 && uin != "0") {
                // Cookie 过期/失效时自动回退游客模式
                purl = QqPurl(songmid, mediaMid, "0", "");
                if (purl.Length == 0) reason = "Cookie无效或歌曲需VIP";
            }
            if (purl.Length == 0 && uin == "0" && token.Length == 0 && reason.Length == 0) {
                reason = "Cookie未检测到uin或qqmusic_key";
            }
            if (purl.Length == 0 && reason.Length == 0) reason = "歌曲无版权或需要VIP";
            if (purl.Length > 0) {
                return Encoding.UTF8.GetBytes("{\"url\":\"https://isure.stream.qqmusic.qq.com/" + purl + "\"}");
            }
            return Encoding.UTF8.GetBytes("{\"url\":\"\",\"reason\":\"" + reason + "\"}");
        } catch (Exception e) {
            return Encoding.UTF8.GetBytes("{\"url\":\"\",\"error\":\"" + Json(e.Message) + "\"}");
        }
    }
    static byte[] HandleConfigGet() {
        try {
            string path = Path.Combine(ROOT, "config.json");
            if (File.Exists(path)) return File.ReadAllBytes(path);
            return Encoding.UTF8.GetBytes("{}");
        } catch { return Encoding.UTF8.GetBytes("{}"); }
    }
    static byte[] HandleConfigPost(byte[] body) {
        try {
            string path = Path.Combine(ROOT, "config.json");
            string json = Encoding.UTF8.GetString(body);
            // 格式化保存：无论前端传的是否单行，都重新格式化后写入（去掉 JavaScriptSerializer 依赖，兼容 .NET 8 编译）
            string pretty = FormatJson(JsonCompact(json));
            File.WriteAllText(path, pretty, new UTF8Encoding(false));
            return Encoding.UTF8.GetBytes("{\"ok\":true}");
        } catch (Exception e) {
            return Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"" + Json(e.Message) + "\"}");
        }
    }
    // 去掉 JSON 字符串外部的空白（缩进/换行），保留字符串内的原样
    static string JsonCompact(string s) {
        var sb = new StringBuilder(s.Length);
        bool inStr = false;
        for (int i = 0; i < s.Length; i++) {
            char c = s[i];
            if (inStr) {
                sb.Append(c);
                if (c == '\\' && i + 1 < s.Length) { sb.Append(s[i + 1]); i++; }
                else if (c == '"') { inStr = false; }
                continue;
            }
            if (c == '"') { inStr = true; sb.Append(c); continue; }
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }
    // 简易 JSON 格式化（缩进 2 空格）
    static string FormatJson(string json) {
        var sb = new StringBuilder();
        int indent = 0;
        bool inStr = false;
        for (int i = 0; i < json.Length; i++) {
            char c = json[i];
            if (inStr) {
                sb.Append(c);
                if (c == '\\' && i + 1 < json.Length) { sb.Append(json[i + 1]); i++; }
                else if (c == '"') { inStr = false; }
                continue;
            }
            if (c == '"') { inStr = true; sb.Append(c); continue; }
            if (c == '{' || c == '[') {
                sb.Append(c); sb.Append('\n'); indent += 2;
                sb.Append(new string(' ', indent));
            } else if (c == '}' || c == ']') {
                sb.Append('\n'); indent -= 2;
                sb.Append(new string(' ', indent)); sb.Append(c);
            } else if (c == ',') {
                sb.Append(c); sb.Append('\n');
                sb.Append(new string(' ', indent));
            } else if (c == ':') {
                sb.Append(c); sb.Append(' ');
            } else {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
    // 列出程序目录下的子文件夹（相对路径），供前端弹框选择模式素材目录
    static byte[] HandleDirs(string path) {
        try {
            if (!string.IsNullOrEmpty(path) && (path.Contains("..") || path.Contains(":") || path.Contains("\\"))) {
                return Encoding.UTF8.GetBytes("{\"dirs\":[],\"current\":\"" + Json(path) + "\"}");
            }
            string root = Path.Combine(ROOT, string.IsNullOrEmpty(path) ? "" : path);
            if (!Directory.Exists(root)) return Encoding.UTF8.GetBytes("{\"dirs\":[]}");
            var dirs = new List<string>();
            foreach (string d in Directory.GetDirectories(root)) {
                dirs.Add(Path.GetFileName(d));
            }
            dirs.Sort();
            var sb = new StringBuilder("{\"current\":\"" + Json(path) + "\",\"dirs\":[");
            for (int i = 0; i < dirs.Count; i++) {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Json(dirs[i])).Append('"');
            }
            sb.Append("]}");
            return Encoding.UTF8.GetBytes(sb.ToString());
        } catch (Exception e) {
            return Encoding.UTF8.GetBytes("{\"dirs\":[],\"error\":\"" + Json(e.Message) + "\"}");
        }
    }
    static byte[] HandleScan(string dir) {
        try {
            if (string.IsNullOrEmpty(dir) || dir.Contains("..") || dir.Contains(":") || dir.Contains("\\")) {
                return Encoding.UTF8.GetBytes("{\"files\":[]}");
            }
            string root = Path.Combine(ROOT, dir);
            if (!Directory.Exists(root)) return Encoding.UTF8.GetBytes("{\"files\":[]}");
            var files = new List<string>();
            WalkDir(root, dir, files);
            var sb = new StringBuilder("{\"files\":[");
            for (int i = 0; i < files.Count; i++) {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Json(files[i])).Append('"');
            }
            sb.Append("]}");
            return Encoding.UTF8.GetBytes(sb.ToString());
        } catch (Exception e) {
            return Encoding.UTF8.GetBytes("{\"files\":[],\"error\":\"" + Json(e.Message) + "\"}");
        }
    }
    static void WalkDir(string absDir, string relDir, List<string> files) {
        foreach (string f in Directory.GetFiles(absDir)) {
            files.Add(relDir + "/" + Path.GetFileName(f));
        }
        foreach (string d in Directory.GetDirectories(absDir)) {
            string n = Path.GetFileName(d);
            WalkDir(d, relDir + "/" + n, files);
        }
    }
    static byte[] HandleQqResolve(string shareUrl) {
        try {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(shareUrl);
            req.UserAgent = UA_ANDROID;
            req.AllowAutoRedirect = true;
            req.MaximumAutomaticRedirections = 6;
            req.Timeout = 15000;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) {
                string final = resp.ResponseUri != null ? resp.ResponseUri.ToString() : shareUrl;
                return Encoding.UTF8.GetBytes("{\"url\":\"" + final.Replace("\\\"", "'") + "\"}");
            }
        } catch (Exception e) {
            return Encoding.UTF8.GetBytes("{\"url\":\"\",\"error\":\"" + Json(e.Message) + "\"}");
        }
    }
    static byte[] HandleQrCheck(string key) {
        string ck = "";
        lock (QrKeyCookies) { if (QrKeyCookies.ContainsKey(key)) ck = QrKeyCookies[key]; }
        string deviceId = RandomHex(16);
        string ts = DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString();
        string requestId = ts + "_" + Rnd.Next(0, 1000).ToString("D4");
        string cookie = "osver=android; appver=8.7.01; os=android; deviceId=" + deviceId +
                        "; channel=netease; requestId=" + requestId + "; __remember_me=true" + (ck.Length > 0 ? "; " + ck : "");
        string form = "params=" + EapiEncrypt("/api/login/qrcode/client/login", "{\"type\":1,\"key\":\"" + key + "\"}");
        string resp = FetchPostFull("http://music.163.com/eapi/login/qrcode/client/login", form, "music.163.com", cookie, UA_ANDROID);
        string setCookie = resp.Substring(resp.IndexOf("\n__SETCOOKIE__") + 14);
        string body = resp.Substring(0, resp.IndexOf("\n__SETCOOKIE__"));
        Match cm = Regex.Match(body, "\"code\":(\\d+)");
        string code = cm.Success ? cm.Groups[1].Value : "0";
        Match mm = Regex.Match(body, "\"message\":\"([^\"]*)\"");
        string msg = mm.Success ? mm.Groups[1].Value : "";
        string musicU = ExtractCookie(setCookie, "MUSIC_U");
        if (code == "803") { lock (QrKeyCookies) QrKeyCookies.Remove(key); }
        return Encoding.UTF8.GetBytes("{\"code\":" + code + ",\"message\":\"" + msg.Replace("\"", "'") + "\",\"musicU\":\"" + musicU + "\"}");
    }
    static void Main() {
        int port = 8099;
        TcpListener listener = null;
        for (int p = port; p < port + 10; p++) {
            try {
                listener = new TcpListener(IPAddress.Loopback, p);
                listener.Start();
                port = p;
                break;
            } catch { }
        }
        if (listener == null) {
            Console.WriteLine("启动失败：端口被占用");
            Console.ReadKey();
            return;
        }
        Console.WriteLine("===========================================");
        Console.WriteLine("  婚礼影音播放器 本地服务已启动");
        Console.WriteLine("  http://127.0.0.1:" + port + "/wedding-player.html");
        Console.WriteLine("  浏览器将自动打开；关闭本窗口即停止服务");
        Console.WriteLine("===========================================");
        try {
            Process.Start(new ProcessStartInfo("http://127.0.0.1:" + port + "/wedding-player.html") { UseShellExecute = true });
        } catch { }
        while (true) {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); }
            catch { break; }
            Thread t = new Thread(new ParameterizedThreadStart(HandleClient));
            t.IsBackground = true;
            t.Start(client);
        }
    }

    static void HandleClient(object o) {
        TcpClient client = (TcpClient)o;
        try {
            using (client) {
                NetworkStream ns = client.GetStream();
                byte[] buf = new byte[8192];
                int read = 0;
                while (read < 65536) {
                    int n = ns.Read(buf, read, buf.Length - read);
                    if (n <= 0) return;
                    read += n;
                    if (Encoding.ASCII.GetString(buf, 0, read).Contains("\r\n\r\n")) break;
                }
                string request = Encoding.ASCII.GetString(buf, 0, read);
                string[] lines = request.Split(new[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length == 0) return;
                string[] parts = lines[0].Split(' ');
                string method = parts.Length > 0 ? parts[0] : "GET";
                string path = parts.Length > 1 ? parts[1] : "/";
                // 读取 POST body（config 保存）
                byte[] postBody = new byte[0];
                if (method == "POST") {
                    int headerEnd = request.IndexOf("\r\n\r\n");
                    if (headerEnd < 0) headerEnd = read;
                    int contentLen = 0;
                    Match clm = Regex.Match(request, "Content-Length:\\s*(\\d+)", RegexOptions.IgnoreCase);
                    if (clm.Success) contentLen = int.Parse(clm.Groups[1].Value);
                    if (contentLen > 0 && contentLen <= 4 * 1024 * 1024) {
                        int need = headerEnd + 4 + contentLen;
                        while (read < need && read < 5 * 1024 * 1024) {
                            byte[] nb = new byte[8192];
                            int n = ns.Read(nb, 0, nb.Length);
                            if (n <= 0) break;
                            Array.Resize(ref buf, read + n);
                            Array.Copy(nb, 0, buf, read, n);
                            read += n;
                        }
                        int bodyStart = headerEnd + 4;
                        if (read - bodyStart >= contentLen) {
                            postBody = new byte[contentLen];
                            Array.Copy(buf, bodyStart, postBody, 0, contentLen);
                        }
                    }
                }
                int qIdx = path.IndexOf('?');
                string qs = qIdx >= 0 ? path.Substring(qIdx + 1) : "";
                string pathOnly = qIdx >= 0 ? path.Substring(0, qIdx) : path;
                if (pathOnly.StartsWith("/api/")) {
                    if (method == "POST" && pathOnly == "/api/config") {
                        Send(ns, 200, "application/json; charset=utf-8", HandleConfigPost(postBody));
                    } else if (method == "GET" && pathOnly == "/api/config") {
                        Send(ns, 200, "application/json; charset=utf-8", HandleConfigGet());
                    } else if (method != "GET") {
                        Send(ns, 405, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Method Not Allowed"));
                    } else {
                        Send(ns, 200, "application/json; charset=utf-8", HandleApi(pathOnly, qs));
                    }
                } else {
                    if (method != "GET") { Send(ns, 405, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Method Not Allowed")); return; }
                    if (pathOnly == "/") pathOnly = "/wedding-player.html";
                    if (pathOnly == "/favicon.ico") { Send(ns, 204, "image/x-icon", new byte[0]); return; }
                    // 静态文件路径需做 URL 解码：否则中文/空格文件名（被浏览器编码为 %20/%E5%91%A8…）会 404
                    string decodedPath;
                    try { decodedPath = Uri.UnescapeDataString(pathOnly); }
                    catch { decodedPath = pathOnly; }
                    string file = Path.Combine(ROOT, decodedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(file)) {
                        Send(ns, 200, Mime(file), File.ReadAllBytes(file));
                    } else {
                        Send(ns, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not Found"));
                    }
                }
            }
        } catch { }
    }

    static void Send(NetworkStream ns, int code, string ctype, byte[] body) {
        string reason = code == 200 ? "OK" : (code == 404 ? "Not Found" : "Error");
        StringBuilder sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n");
        sb.Append("Content-Type: ").Append(ctype).Append("\r\n");
        sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        sb.Append("Cache-Control: no-store\r\n");
        sb.Append("Connection: close\r\n\r\n");
        byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
        ns.Write(head, 0, head.Length);
        ns.Write(body, 0, body.Length);
        ns.Flush();
    }

    static string Mime(string file) {
        if (file.EndsWith(".html")) return "text/html; charset=utf-8";
        if (file.EndsWith(".js")) return "application/javascript";
        if (file.EndsWith(".css")) return "text/css";
        if (file.EndsWith(".png")) return "image/png";
        if (file.EndsWith(".jpg") || file.EndsWith(".jpeg")) return "image/jpeg";
        if (file.EndsWith(".gif")) return "image/gif";
        if (file.EndsWith(".webp")) return "image/webp";
        if (file.EndsWith(".mp4")) return "video/mp4";
        if (file.EndsWith(".webm")) return "video/webm";
        if (file.EndsWith(".mov")) return "video/quicktime";
        if (file.EndsWith(".m4v")) return "video/x-m4v";
        if (file.EndsWith(".mp3")) return "audio/mpeg";
        if (file.EndsWith(".wav")) return "audio/wav";
        if (file.EndsWith(".ogg")) return "audio/ogg";
        if (file.EndsWith(".oga")) return "audio/ogg";
        if (file.EndsWith(".opus")) return "audio/ogg";
        if (file.EndsWith(".flac")) return "audio/flac";
        if (file.EndsWith(".m4a")) return "audio/mp4";
        if (file.EndsWith(".aac")) return "audio/aac";
        if (file.EndsWith(".lrc")) return "text/plain; charset=utf-8";
        if (file.EndsWith(".ico")) return "image/x-icon";
        return "application/octet-stream";
    }

    static string Q(string qs, string key) {
        if (string.IsNullOrEmpty(qs)) return "";
        string[] pairs = qs.Split('&');
        foreach (string p in pairs) {
            int i = p.IndexOf('=');
            if (i > 0) {
                string k = Uri.UnescapeDataString(p.Substring(0, i));
                string v = Uri.UnescapeDataString(p.Substring(i + 1));
                if (k == key) return v;
            }
        }
        return "";
    }

    static string Json(string s) {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    static byte[] HandleApi(string path, string qs) {
        try {
            string ucookie = Q(qs, "cookie");
            if (path == "/api/netease/playlist") {
                string id = Q(qs, "id");
                return EapiCall("/api/v6/playlist/detail", "{\"s\":\"0\",\"id\":\"" + id + "\",\"n\":\"1000\",\"t\":\"0\"}", ucookie);
            } else if (path == "/api/netease/search") {
                string s = Q(qs, "s");
                return EapiCall("/api/cloudsearch/pc", "{\"s\":\"" + Json(s) + "\",\"type\":1,\"limit\":8,\"total\":\"true\",\"offset\":0}", ucookie);
            } else if (path == "/api/netease/url") {
                string id = Q(qs, "id");
                return EapiCall("/api/song/enhance/player/url", "{\"ids\":[" + id + "],\"br\":128000}", ucookie);
            } else if (path == "/api/netease/lyric") {
                string id = Q(qs, "id");
                return EapiCall("/api/song/lyric", "{\"id\":\"" + id + "\",\"os\":\"linux\",\"lv\":-1,\"kv\":-1,\"tv\":-1}", ucookie);
            } else if (path == "/api/netease/qrkey") {
                return HandleQrKey();
            } else if (path == "/api/netease/qrcheck") {
                return HandleQrCheck(Q(qs, "key"));
            } else if (path == "/api/qq/playurl") {
                return HandleQqPlayUrl(Q(qs, "songmid"), Q(qs, "mediaMid"), Q(qs, "cookie"));
            } else if (path == "/api/qq/lyric") {
                return HandleQqLyric(Q(qs, "songmid"));
            } else if (path == "/api/qq/resolve") {
                return HandleQqResolve(Q(qs, "url"));
            } else if (path == "/api/scan") {
                return HandleScan(Q(qs, "dir"));
            } else if (path == "/api/dirs") {
                return HandleDirs(Q(qs, "path"));
            } else if (path == "/api/qq/playlist") {
                string u = "https://c.y.qq.com/qzone/fcg-bin/fcg_ucc_getcdinfo_byids_cp.fcg?type=1&json=1&utf8=1&onlysong=0&disstid=" + Q(qs, "id") +
                           "&format=json&g_tk=1913204067&loginUin=0&hostUin=0&inCharset=utf8&outCharset=utf-8&notice=0&platform=yqq.json&needNewCode=0";
                return FetchGet(u, "https://y.qq.com/", null);
            } else if (path == "/api/huawei/playlist") {
                return FetchGet(Q(qs, "url"), null, null);
            }
            return Encoding.UTF8.GetBytes("{\"error\":\"unknown endpoint\"}");
        } catch (Exception e) {
            return Encoding.UTF8.GetBytes("{\"error\":\"" + Json(e.Message) + "\"}");
        }
    }
}

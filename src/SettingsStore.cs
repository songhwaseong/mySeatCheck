using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class SettingsStore
{
    readonly string path;
    readonly JavaScriptSerializer json = new JavaScriptSerializer();

    internal SettingsStore(string path) { this.path = path; }

    internal Settings Load(out string notice)
    {
        notice = null;
        string backup = path + ".bak";
        if (!File.Exists(path) && !File.Exists(backup)) return new Settings();
        Settings value;
        bool legacy;
        bool recovered = false;
        try { value = Read(path, out legacy); }
        catch (Exception primaryError)
        {
            try { value = Read(backup, out legacy); }
            catch (Exception backupError)
            {
                throw new IOException("설정과 백업을 읽을 수 없습니다. 두 파일을 보존했습니다.\n" + path +
                    "\n" + primaryError.Message + "\n백업: " + backupError.Message, primaryError);
            }
            recovered = true;
            notice = "직전 정상 백업에서 설정을 복구했습니다.";
        }
        // 기존 평문 토큰은 백업에도 남기지 않는다. 손상된 원본은 정상 백업으로 교체한다.
        if (File.Exists(backup))
        {
            bool backupLegacy = false;
            Settings old = null;
            try { old = Read(backup, out backupLegacy); }
            catch (Exception) { /* 정상 원본이 다음 저장 때 백업이 된다. */ }
            if (old != null && backupLegacy) Write(backup, old, null);
        }
        if (legacy || recovered) Write(path, value, null);
        return value;
    }

    Settings Read(string file, out bool legacy)
    {
        var raw = File.ReadAllText(file, Encoding.UTF8);
        var value = json.Deserialize<Settings>(raw);
        if (value == null || value.Watches == null || value.Watches.Any(w => w == null || string.IsNullOrWhiteSpace(w.Url)))
            throw new InvalidDataException("감시 목록을 읽을 수 없습니다.");
        var fields = json.Deserialize<Dictionary<string, object>>(raw);
        if (!fields.ContainsKey("Watches")) throw new InvalidDataException("감시 목록이 없습니다.");
        legacy = !string.IsNullOrEmpty(value.TelegramToken);
        if (!string.IsNullOrEmpty(value.ProtectedTelegramToken))
            value.TelegramToken = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                Convert.FromBase64String(value.ProtectedTelegramToken), null, DataProtectionScope.CurrentUser));
        foreach (var watch in value.Watches)
        {
            if (watch.Seats == null) watch.Seats = new List<string>();
            if (watch.PendingNtfy == null) watch.PendingNtfy = new Dictionary<string, int>();
            if (watch.PendingTelegram == null) watch.PendingTelegram = new Dictionary<string, int>();
        }
        return value;
    }

    internal void Save(Settings value) { Write(path, value, path + ".bak"); }

    void Write(string dest, Settings value, string backup)
    {
        var disk = value.ForStorage();
        var temp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json.Serialize(disk));
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes, 0, bytes.Length);
                output.Flush(true);
            }
            if (File.Exists(dest)) File.Replace(temp, dest, backup);
            else File.Move(temp, dest);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

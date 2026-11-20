using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

// 노선별 알림 설정이 공통 알림 설정과 어떻게 합쳐지는지 확인한다.
class WatchAlertTest
{
    static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("Failed: " + name);
        Console.WriteLine("ok  " + name);
    }

    static void Main()
    {
        var common = new Settings { IntervalMin = 3, NtfyTopic = "seatwatch-me", TelegramToken = "123:abc", TelegramChat = "111" };

        // 예전 설정 파일의 노선은 모두 켜짐·공통 값으로 읽힌다.
        var old = new JavaScriptSerializer().Deserialize<Watch>("{\"Url\":\"https://flight.naver.com/flights/domestic/GMP:airport-CJU:airport-20991231\"}");
        Check(old.AlertPc && old.AlertNtfy && old.AlertTelegram && old.IntervalMin == 0, "old file defaults");
        var t = old.AlertTarget(common);
        Check(t.UsesNtfy() && t.NtfyTopic == "seatwatch-me" && t.UsesTelegram() && t.TelegramChat == "111", "defaults use common");
        Check(old.AlertSummary(common) == "알림 PC·ntfy·텔레그램 · 3분마다", "summary default");
        Check(!old.HasOwnAlerts && new Watch { AlertPc = false }.HasOwnAlerts && new Watch { IntervalMin = 5 }.HasOwnAlerts &&
            new Watch { TelegramChat = "9" }.HasOwnAlerts, "own alerts detection");

        // 다른 사람에게 보내기: 주제·채팅 ID 덮어쓰기. 봇 토큰은 공통.
        var family = new Watch { NtfyTopic = "seatwatch-family", TelegramChat = "222", AlertPc = false, IntervalMin = 10 };
        t = family.AlertTarget(common);
        Check(t.NtfyTopic == "seatwatch-family" && t.TelegramChat == "222" && t.TelegramToken == "123:abc", "per-route recipients");
        Check(common.NtfyTopic == "seatwatch-me" && common.TelegramChat == "111", "common settings untouched");
        Check(family.AlertSummary(common) == "알림 ntfy(따로)·텔레그램(따로) · 10분마다(따로)", "summary custom");

        // 노선에서 끈 채널은 보내지 않고, 공통에서 끈 채널은 노선에서 켜 두어도 보내지 않는다.
        var quiet = new Watch { AlertNtfy = false };
        Check(!quiet.AlertTarget(common).UsesNtfy() && quiet.AlertTarget(common).UsesTelegram(), "route channel off");
        var commonOff = common.Copy();
        commonOff.TelegramOn = false;
        Check(!family.AlertTarget(commonOff).UsesTelegram(), "common channel off wins");
        Check(new Watch { AlertPc = false, AlertNtfy = false, AlertTelegram = false }.AlertSummary(common) == "알림 없음 · 3분마다", "summary none");

        // 공통 주제가 비어 있어도 노선 주제가 있으면 그 노선만 ntfy 로 보낸다.
        var noTopic = new Settings { NtfyTopic = "" };
        Check(!new Watch().AlertTarget(noTopic).UsesNtfy() && family.AlertTarget(noTopic).UsesNtfy(), "route topic without common");

        // 노선 알림을 끄거나 받는 곳을 바꾸면 그 채널의 재전송 대기를 버린다.
        var w = new Watch();
        w.PendingNtfy["a"] = 0;
        w.PendingTelegram["a"] = 0;
        var before = w.AlertTarget(common);
        w.TelegramChat = "333";
        w.ResetPending(before, w.AlertTarget(common));
        Check(w.PendingNtfy.Count == 1 && w.PendingTelegram.Count == 0, "reset only changed channel");
        before = w.AlertTarget(common);
        w.AlertNtfy = false;
        w.ResetPending(before, w.AlertTarget(common));
        Check(w.PendingNtfy.Count == 0, "reset turned-off channel");

        // 새 좌석은 이 노선이 보내는 채널에만 재전송 대기로 들어간다.
        var card = new Card { Airline = "진에어", Dep = "18:05", Arr = "19:15", Cls = "할인석", Price = 50000 };
        var seen = new Watch { AlertTelegram = false, PrevKeys = new List<string>() };
        seen.Observe(new List<Card> { card }, true, seen.AlertTarget(common));
        Check(seen.PendingNtfy.Count == 1 && seen.PendingTelegram.Count == 0, "pending follows route channels");

        // 복제·조건 수정 때 이어받는다.
        var copy = new Watch();
        copy.CopyAlertsFrom(family);
        Check(copy.NtfyTopic == "seatwatch-family" && copy.TelegramChat == "222" && !copy.AlertPc && copy.IntervalMin == 10, "copy alerts");
        Console.WriteLine("all passed");
    }
}

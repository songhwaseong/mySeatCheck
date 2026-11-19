using System;
using System.Web.Script.Serialization;

class WatchDisplayTest
{
    static void Expect(Watch watch, bool running, string status, int fares)
    {
        if (watch.DisplayStatus(running) != status || watch.CurrentFares(running).Count != fares)
            throw new Exception("Unexpected seat display for " + status);
    }

    static void Main()
    {
        const string previous = "{\"Enabled\":false,\"Status\":\"좌석 1개\",\"LastCheck\":\"14:02:32\"," +
            "\"Seats\":[\"21:20→22:35 제주항공 일반석 136,800원\"],\"PrevKeys\":[\"21:20 제주항공 일반석\"]}";
        var json = new JavaScriptSerializer();
        var watch = json.Deserialize<Watch>(previous);
        Expect(watch, true, "노선 감시 꺼짐", 0);
        watch.Enabled = true;
        Expect(watch, true, "조회 대기", 0);
        watch.Unconfirmed = "조회 중…";
        Expect(watch, true, "조회 중…", 0);
        watch.Unconfirmed = null;
        Expect(watch, true, "좌석 있음 · 1개", 1);
        Expect(watch, false, "전체 감시 멈춤", 0);
        var restarted = json.Deserialize<Watch>(json.Serialize(watch));
        Expect(restarted, true, "조회 대기", 0);
        watch.Unconfirmed = "확인 못 함(로딩)";
        Expect(watch, true, "확인 못 함(로딩)", 0);
        if (watch.Seats.Count != 1 || watch.PrevKeys.Count != 1 || restarted.PrevKeys.Count != 1)
            throw new Exception("Cached result or notification baseline lost");
        watch.Seats.Clear();
        watch.Unconfirmed = null;
        Expect(watch, true, "좌석 없음", 0);

        // 최대 금액: 0이면 제한 없음, 기준과 같은 금액은 포함한다. 예전 설정 파일에는 값이 없어 0으로 읽힌다.
        if (!watch.Allows(5000000) || restarted.MaxPrice != 0)
            throw new Exception("Watch without a price limit must allow every fare");
        var limited = json.Deserialize<Watch>(json.Serialize(new Watch { Url = "x", MaxPrice = 100000 }));
        if (limited.MaxPrice != 100000 || !limited.Allows(100000) || limited.Allows(100001))
            throw new Exception("Price limit was not kept or compared correctly");
        if (Watch.PriceLabel(100000) != "10만 원 이하" || Watch.PriceLabel(85500) != "85,500원 이하" || Watch.PriceLabel(0) != "")
            throw new Exception("Unexpected price label");
        watch.OverPrice = 2;
        Expect(watch, true, "금액 초과 좌석만 2개", 0);

        // 텔레그램 칸이 없던 예전 설정 파일은 빈칸으로 읽고, 저장한 값은 다시 읽힌다.
        var oldSettings = json.Deserialize<Settings>("{\"IntervalMin\":3,\"NtfyTopic\":\"seatwatch-abc\",\"Running\":true,\"Watches\":[]}");
        var telegram = json.Deserialize<Settings>(json.Serialize(new Settings { TelegramToken = "1:a", TelegramChat = "42" }));
        if (oldSettings.TelegramToken != "" || oldSettings.TelegramChat != "" || oldSettings.NtfyTopic != "seatwatch-abc" ||
            telegram.TelegramToken != "1:a" || telegram.TelegramChat != "42")
            throw new Exception("Telegram settings were not read correctly");

        // 체크 칸이 없던 예전 설정은 켜짐으로 읽고, 체크를 풀면 값은 남아도 보내지 않는다.
        if (!oldSettings.NtfyOn || !oldSettings.TelegramOn || !oldSettings.UsesNtfy() || oldSettings.UsesTelegram() || !telegram.UsesTelegram())
            throw new Exception("Old settings should keep sending to filled channels");
        var muted = json.Deserialize<Settings>(json.Serialize(new Settings { NtfyTopic = "seatwatch-abc", TelegramToken = "1:a",
            TelegramChat = "42", NtfyOn = false, TelegramOn = false }));
        if (muted.NtfyOn || muted.TelegramOn || muted.UsesNtfy() || muted.UsesTelegram() ||
            muted.NtfyTopic != "seatwatch-abc" || muted.TelegramToken != "1:a" || muted.TelegramChat != "42")
            throw new Exception("Unchecked channels should keep their values but not send");
        Console.WriteLine("Seat and fare display regression checks passed.");
    }
}

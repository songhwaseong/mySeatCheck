// 네이버 항공권 화면 글자에서 편 카드를 뽑는다. 프로그램이 WebView2 페이지 안에서 실행하고,
// tests/parse.test.js 가 node 에서 같은 파일을 시험한다.
//
// 카드 모양(줄 단위):
//   아시아나항공 / 06:30CJU / 07:45GMP / 01시간 15분 / 할인석편도 102,300원~ / (카드 할인 줄) / 1% 적립
// 주의(2026-11-16 실측):
//   - 시각과 공항 코드가 붙어 나온다(06:30CJU) → \b 로 시각을 찾으면 안 맞는다.
//   - 사이트 시간대 거르기가 먹은 날 매진이면 "선택된 상세조건에 맞는 항공편이 없습니다".
//   - 정렬 문구 앞(광고)과 바닥글 뒤의 가격은 버린다.
var SeatScan = (function () {
  var CARD_RE = /([^\n]+)\n(\d{2}:\d{2})[A-Z]{3}\n(\d{2}:\d{2})[A-Z]{3}\n(?:[^\n]*\n){0,3}?([^\n]*?)편도\s*([\d,]+)원/g;
  var NONE_RE = /조건에 맞는 항공편이 없|검색 결과가 없|항공편이 없습니다/;

  function body(text) {
    var b = String(text || '');
    var start = b.search(/(출발시각 빠른 순|가격 낮은 순|최저가 순|추천순)/);
    if (start >= 0) b = b.slice(start);
    var end = b.indexOf('개인정보처리방침');
    if (end >= 0) b = b.slice(0, end);
    return b;
  }

  function parseText(text) {
    var b = body(text);
    var cards = [];
    var m;
    CARD_RE.lastIndex = 0;
    while ((m = CARD_RE.exec(b))) {
      cards.push({
        airline: m[1].trim(),
        dep: m[2],
        arr: m[3],
        cls: m[4].trim() || '좌석',
        price: Number(m[5].replace(/,/g, ''))
      });
    }
    return { cards: cards, none: NONE_RE.test(b), body: b };
  }

  // 국내선 페이지는 50개씩 이어 붙이며, 마지막 응답이 50개 미만이면 더 불러오지 않는다.
  // 정확한 배수는 화면만으로 마지막 빈 응답과 아직 안 읽은 다음 페이지를 구분할 수 없다.
  var DOMESTIC_PAGE_SIZE = 50;

  function scanPage(doc) {
    doc = doc || document;
    var t = doc.body ? doc.body.innerText : '';
    var list = doc.querySelector('[data-type="domestic"]');
    var r = parseText(list ? list.innerText : t);
    var loading = doc.querySelector('[aria-label="조건에 맞는 항공편 검색중"][aria-busy="true"]') ||
      (list && list.querySelector('[aria-busy="true"]'));
    var rows = list ? list.querySelectorAll('button[data-event-area-code="lsr.schedule1"]') : [];
    var cards = [];
    for (var i = 0; i < rows.length; i++) {
      var element = rows[i].parentElement;
      var row = parseText(element.innerText);
      if (row.cards.length !== 1) continue;
      var card = row.cards[0];
      var logo = element.querySelector('[data-area="logos"] img');
      var code = logo && (logo.getAttribute('src') || '').match(/\/vertical-static\/flight\/airlines\/([A-Z0-9]{2})\.png(?:[?#]|$)/i);
      if (code) card.airlineCode = code[1].toUpperCase();
      cards.push(card);
    }
    var allRowsRead = cards.length === rows.length && r.cards.length === rows.length;
    var readable = !loading && !r.none && rows.length > 0 && allRowsRead;
    var complete = readable && rows.length % DOMESTIC_PAGE_SIZE !== 0;
    return {
      cards: loading ? [] : (allRowsRead ? cards : r.cards),
      none: !loading && r.none && !rows.length && !r.cards.length,
      complete: complete, pageBoundary: readable && !complete,
      bodyHead: r.body.slice(0, 4000), ready: doc.readyState, url: doc.location.href
    };
  }

  // 목록 더 불러오기: 문서와 안쪽 스크롤 칸을 모두 맨 아래로
  function scrollDown() {
    var se = document.scrollingElement || document.documentElement;
    var before = se.scrollTop;
    se.scrollTop = se.scrollHeight;
    var moved = se.scrollTop !== before;
    var els = document.querySelectorAll('*');
    for (var i = 0; i < els.length; i++) {
      var el = els[i];
      if (el.scrollHeight > el.clientHeight + 50) {
        var oy = getComputedStyle(el).overflowY;
        if (oy === 'auto' || oy === 'scroll') {
          before = el.scrollTop;
          el.scrollTop = el.scrollHeight;
          if (el.scrollTop !== before) moved = true;
        }
      }
    }
    return moved;
  }

  return { parseText: parseText, scanPage: scanPage, scrollDown: scrollDown };
})();
if (typeof module !== 'undefined') module.exports = SeatScan;

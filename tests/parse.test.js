// node tests/parse.test.js
const assert = require('assert');
const fs = require('fs');
const path = require('path');
const SeatScan = require('../src/parse.js');

let n = 0;
function test(name, fn) { fn(); n++; console.log('ok', name); }

const head = '광고 9,900원 특가\n편도 선택\n출발시각 빠른 순\n11.21.토\n11.22.일\n';
const card = (air, dep, arr, cls, price, extra) =>
  `${air}\n${dep}CJU\n${arr}GMP\n01시간 15분\n${cls}편도 ${price}원~\n${extra ? extra + '\n' : ''}1% 적립\n`;

test('카드를 줄 단위로 읽는다(시각+공항 코드 붙은 꼴)', () => {
  const t = head + card('아시아나항공', '06:30', '07:45', '할인석', '102,300', 'KB국민 체크카드 결제 200원 캐시백 시102,100원~')
    + card('대한항공', '19:30', '20:45', '일반석', '125,300') + '개인정보처리방침\n대한항공\n20:00CJU\n21:10GMP\n일반석편도 1,000원~';
  const r = SeatScan.parseText(t);
  assert.strictEqual(r.cards.length, 2);
  assert.deepStrictEqual(r.cards[0], { airline: '아시아나항공', dep: '06:30', arr: '07:45', cls: '할인석', price: 102300 });
  assert.deepStrictEqual(r.cards[1], { airline: '대한항공', dep: '19:30', arr: '20:45', cls: '일반석', price: 125300 });
  assert.strictEqual(r.none, false);
});

test('정렬 문구 앞 광고 가격은 버린다', () => {
  const r = SeatScan.parseText(head + card('진에어', '18:05', '19:15', '할인석', '45,600'));
  assert.strictEqual(r.cards.length, 1);
  assert.strictEqual(r.cards[0].price, 45600);
});

test('사이트 거르기가 먹은 매진 화면을 알아본다', () => {
  const r = SeatScan.parseText(head + '선택된 상세조건에 맞는 항공편이 없습니다\n\n상세 조건을 다시 선택하세요');
  assert.strictEqual(r.cards.length, 0);
  assert.strictEqual(r.none, true);
});

test('실측 화면 글자(samples)도 깨지지 않는다', () => {
  const dir = path.join(__dirname, 'samples');
  for (const f of fs.readdirSync(dir).filter(name => name.endsWith('.txt'))) {
    const t = fs.readFileSync(path.join(dir, f), 'utf8');
    const r = SeatScan.parseText(t);
    const lines = (t.match(/^\d{2}:\d{2}CJU$/gm) || []).length;
    assert.strictEqual(r.cards.length, lines, f + ': 카드 수가 출발 줄 수와 같아야 함');
    if (!lines) assert.strictEqual(r.none, true, f + ': 카드가 없으면 매진 문구가 있어야 함');
  }
});

test('같은 함수로 여러 번 불러도 결과가 같다(정규식 lastIndex)', () => {
  const t = head + card('제주항공', '21:10', '22:20', '할인석', '88,000');
  assert.strictEqual(SeatScan.parseText(t).cards.length, 1);
  assert.strictEqual(SeatScan.parseText(t).cards.length, 1);
});

// 브라우저 없이 국내선 결과 영역과 항공편 선택 버튼의 텍스트만 제공한다.
function scanRows(texts, { loading = false, searching = false, extra = '', domestic = true, logos = [] } = {}) {
  const rows = texts.map((innerText, i) => ({ parentElement: {
    innerText,
    querySelector: selector => selector === '[data-area="logos"] img' && logos[i]
      ? { getAttribute: name => name === 'src' ? logos[i] : null } : null
  } }));
  const list = {
    innerText: head + texts.join('') + extra,
    querySelector: selector => selector === '[aria-busy="true"]' && loading ? {} : null,
    querySelectorAll: selector => selector === 'button[data-event-area-code="lsr.schedule1"]' ? rows : []
  };
  return SeatScan.scanPage({
    body: list, readyState: 'complete', location: { href: 'https://flight.naver.com/flights/domestic/test' },
    querySelector: selector => {
      if (selector === '[data-type="domestic"]') return domestic ? list : null;
      return searching && selector.includes('항공편 검색중') ? {} : null;
    }
  });
}

const example = card('진에어', '18:05', '19:15', '할인석', '45,600');
test('마지막 묶음의 모든 행을 읽어야 국내선 목록을 확정한다', () => {
  for (const count of [1, 49, 51]) {
    const r = scanRows(Array(count).fill(example));
    assert.strictEqual(r.complete, true);
    assert.strictEqual(r.cards.length, count);
  }
  for (const count of [0, 50, 100]) {
    const page = scanRows(Array(count).fill(example));
    assert.strictEqual(page.complete, false);
    assert.strictEqual(page.pageBoundary, count > 0);
  }
  assert.strictEqual(scanRows(Array(50).fill(example), { loading: true }).pageBoundary, false);
  assert.strictEqual(scanRows(Array(50).fill(example), { extra: '검색된 항공편이 없습니다' }).pageBoundary, false);
  assert.strictEqual(scanRows([...Array(49).fill(example), '운임 조회 중']).pageBoundary, false);
  assert.strictEqual(scanRows([example, '운임 조회 중']).complete, false);
  assert.strictEqual(scanRows([example], { domestic: false }).complete, false);
});

test('조회 중인 화면에서는 좌석과 매진을 모두 확정하지 않는다', () => {
  for (const state of [{ loading: true }, { searching: true }]) {
    const r = scanRows([example], state);
    assert.strictEqual(r.complete, false);
    assert.deepStrictEqual(r.cards, []);
    assert.strictEqual(scanRows([], { ...state, extra: '검색된 항공편이 없습니다' }).none, false);
  }
  assert.strictEqual(scanRows([], { extra: '검색된 항공편이 없습니다' }).none, true);
});

test('매진 문구와 항공편이 함께 보이면 어느 쪽도 확정하지 않는다', () => {
  const r = scanRows([example], { extra: '검색된 항공편이 없습니다' });
  assert.strictEqual(r.none, false);
  assert.strictEqual(r.complete, false);
});

test('항공사 로고의 코드를 읽고 공통 이미지는 제외한다', () => {
  const path = 'https://example.com/flight/vertical-static/flight/airlines/';
  const r = scanRows([example, example, example], { logos: [path + 'LJ.png', path + '7C.png?v=1', path + 'naver.png'] });
  assert.deepStrictEqual(r.cards.map(c => c.airlineCode), ['LJ', '7C', undefined]);
  const firstPage = scanRows(Array(50).fill(example), { logos: [path + 'LJ.png'] });
  assert.strictEqual(firstPage.complete, false);
  assert.strictEqual(firstPage.cards[0].airlineCode, 'LJ');
});

// ───────── 국제선 ─────────
const intlRow = [
  'CO2 60%', '대한항공, 일본항공', '이벤트혜택', '대한항공 공동운항', '18:50ICN', '09:30NRT', '+1일',
  '경유 1, 14시간 40분', '동일가 다른 일정2', '성인/KB국민카드', '473,200원~', '1만원 할인', '편도', '463,200', '원~',
  '최대 10,000원 적립', '로그인 후 특가확인'
].join('\n');

test('국제선 카드: 공동운항 줄을 건너뛰고 할인 전 가격과 +1일을 읽는다', () => {
  assert.deepStrictEqual(SeatScan.parseInternationalRow(intlRow),
    { airline: '대한항공, 일본항공', dep: '18:50', arr: '09:30+1', cls: '경유 1', price: 473200, intl: true,
      from: 'ICN', to: 'NRT', stops: 1, minutes: 880 });
  const plain = 'CO2 15%\n제주항공, 젯스타 재팬\n16:20ICN\n21:55NRT\n경유 1, 05시간 35분\n성인/모든 결제수단\n편도\n355,565\n원~';
  assert.deepStrictEqual(SeatScan.parseInternationalRow(plain),
    { airline: '제주항공, 젯스타 재팬', dep: '16:20', arr: '21:55', cls: '경유 1', price: 355565, intl: true,
      from: 'ICN', to: 'NRT', stops: 1, minutes: 335 });
  const direct = SeatScan.parseInternationalRow('최저가\nCO2 12%\n트리니티항공\n08:35ICN\n10:55HND\n직항, 02시간 20분\n편도\n139,000\n원~');
  assert.deepStrictEqual([direct.cls, direct.stops, direct.minutes, direct.to], ['직항', 0, 140, 'HND']);
  assert.strictEqual(SeatScan.parseInternationalRow('중국동방항공\n16:40ICN\n12:50NRT\n+1일\n경유 2, 34시간 20분\n편도\n664,988\n원~').minutes, 2060);
  assert.strictEqual(SeatScan.parseInternationalRow('운임 조회 중'), null);
  assert.strictEqual(SeatScan.parseInternationalRow('트리니티항공\n08:35ICN\n10:55NRT\n직항, 02시간 20분'), null);
});

// 브라우저 없이 국제선 결과 화면을 흉내 낸다. rows 는 실측 픽스처와 같은 { text, logos } 모양.
function scanIntl(rows, { done = true, loadingBadge = false, subopen = [], extra = '',
  href = 'https://flight.naver.com/flights/international/ICN-NRT-20261220?adult=1&fareType=Y' } = {}) {
  const buttons = rows.map(row => ({ parentElement: {
    innerText: row.text,
    querySelectorAll: selector => selector === '[data-area="logos"] img'
      ? (row.logos || []).map(src => ({ getAttribute: name => name === 'src' ? src : null })) : []
  } }));
  const marker = { className: 'international_search_condition_loading_area__p3h5v' + (done ? ' international_loading_done__3FFWt' : '') };
  return SeatScan.scanPage({
    body: { innerText: '가격 낮은 순\n' + rows.map(r => r.text).join('\n') + extra },
    readyState: 'complete',
    location: { href },
    querySelector: selector => {
      if (selector === '[aria-label="조건에 맞는 항공편 검색중"]') return marker;
      if (selector === '[aria-label="Loading..."][aria-busy="true"]') return loadingBadge ? {} : null;
      return null;
    },
    querySelectorAll: selector => {
      if (selector === 'button[data-event-area-code="isb.schedule1"]') return buttons;
      if (selector === 'button[data-event-area-code="isr.subopen"]') return subopen.map(innerText => ({ innerText }));
      return [];
    }
  });
}

test('국제선 실측 화면(samples/international)의 모든 행을 읽는다', () => {
  const dir = path.join(__dirname, 'samples', 'international');
  for (const f of fs.readdirSync(dir)) {
    const sample = JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8'));
    const r = scanIntl(sample.rows, { subopen: sample.subopen, href: sample.url });
    assert.strictEqual(r.complete, true, f);
    assert.strictEqual(r.cards.length, sample.rows.length, f + ': 모든 행을 카드로');
    assert.ok(r.cards.every(c => /^\d{2}:\d{2}$/.test(c.dep) && /^\d{2}:\d{2}(\+\d)?$/.test(c.arr) && c.price > 0 &&
      /^(직항|경유 \d)$/.test(c.cls) && c.airlineCode && c.stops >= 0 && c.minutes > 0), f + ': 카드 모양');
    assert.ok(r.hiddenSchedules > 0, f + ': 접힌 일정 수');
  }
});

test('국제선은 loading_done 클래스가 붙기 전에는 아무것도 확정하지 않는다', () => {
  const row = { text: intlRow, logos: ['https://x/flight/vertical-static/flight/airlines/KE.png', 'https://x/flight/vertical-static/flight/airlines/JL.png'] };
  const done = scanIntl([row]);
  assert.strictEqual(done.complete, true);
  assert.deepStrictEqual(done.cards[0].airlineCodes, ['KE', 'JL']);
  assert.strictEqual(done.cards[0].airlineCode, 'KE');
  for (const state of [{ done: false }, { loadingBadge: true }]) {
    const r = scanIntl([row], state);
    assert.strictEqual(r.complete, false);
    assert.deepStrictEqual(r.cards, []);
  }
  assert.strictEqual(scanIntl([row, { text: '운임 조회 중' }]).complete, false, '못 읽은 행이 있으면 미완료');
  assert.strictEqual(scanIntl([], { extra: '\n조건에 맞는 항공편이 없습니다' }).none, true);
  assert.strictEqual(scanIntl([], { done: false, extra: '\n조건에 맞는 항공편이 없습니다' }).none, false);
  assert.strictEqual(scanIntl([row], { subopen: ['동일가 다른 일정3', '동일가 다른 일정2'] }).hiddenSchedules, 5);
});

test('도시 검색(TYO:city)은 편마다 다른 공항을 좌석 구분에 붙이고, 공항 검색은 그대로 둔다', () => {
  const nrt = { text: intlRow }, hnd = { text: intlRow.replace('09:30NRT', '09:30HND') };
  const city = scanIntl([nrt, hnd], { href: 'https://flight.naver.com/flights/international/ICN-TYO:city-20261220?adult=1&fareType=Y' });
  assert.deepStrictEqual(city.cards.map(c => c.cls), ['경유 1 · ICN→NRT', '경유 1 · ICN→HND']);
  assert.deepStrictEqual(scanIntl([nrt]).cards.map(c => c.cls), ['경유 1']);
});

console.log(`\n${n}개 통과`);

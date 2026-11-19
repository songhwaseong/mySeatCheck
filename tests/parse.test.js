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
  for (const f of fs.readdirSync(dir)) {
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

console.log(`\n${n}개 통과`);

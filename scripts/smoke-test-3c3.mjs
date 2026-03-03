/**
 * Phase 3c3 — Dönem Bazlı Muhasebe Smoke Test
 * Çalıştırmak için: node scripts/smoke-test-3c3.mjs
 * Önkoşul: API http://localhost:5100'de çalışıyor olmalı
 */

const SUPABASE_URL   = 'https://jkyzxiyemxkbhifohpcg.supabase.co';
const SUPABASE_ANON  = 'sb_publishable_IWlNZIGHzhapQv3h9Y-d-Q_ea3vmIWI';
const API_URL        = 'http://localhost:5100';
const EMAIL          = 'mehmet.ayan1741@gmail.com';
const PASSWORD       = '123456789';

let passed = 0;
let failed = 0;

function ok(label, condition, info = '') {
  if (condition) {
    console.log(`  ✓ ${label}`);
    passed++;
  } else {
    console.log(`  ✗ ${label}${info ? ' — ' + info : ''}`);
    failed++;
  }
}

async function api(method, path, body, token) {
  const res = await fetch(`${API_URL}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  let data;
  try { data = await res.json(); } catch { data = null; }
  return { status: res.status, data };
}

// ─── 1. Auth ──────────────────────────────────────────────────────────────────

console.log('\n[1] Auth — Supabase login');
const authRes = await fetch(
  `${SUPABASE_URL}/auth/v1/token?grant_type=password`,
  {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', apikey: SUPABASE_ANON },
    body: JSON.stringify({ email: EMAIL, password: PASSWORD }),
  }
);
const authData = await authRes.json();
const token = authData.access_token;
ok('Login başarılı', !!token, authData.error_description ?? authData.error ?? '');
if (!token) { console.log('\n❌ Token alınamadı, test durduruluyor.'); process.exit(1); }

// ─── 2. orgId ─────────────────────────────────────────────────────────────────

console.log('\n[2] GET /me — orgId al');
const me = await api('GET', '/api/v1/me', null, token);
ok('/me 200', me.status === 200);
const orgId = me.data?.memberships?.[0]?.organizationId;
ok('orgId var', !!orgId);
if (!orgId) { console.log('\n❌ orgId alınamadı.'); process.exit(1); }

const base = `/api/v1/organizations/${orgId}/finance`;

// ─── 3. GET /finance/records — period_year, period_month alanları dolu mu? ───

console.log('\n[3] Migration sonrası veri kontrolü');
const records = await api('GET', `${base}/records`, null, token);
ok('GET records 200', records.status === 200);
const firstRecord = records.data?.items?.[0];
if (firstRecord) {
  ok('periodYear alanı dolu', firstRecord.periodYear != null, `val=${firstRecord.periodYear}`);
  ok('periodMonth alanı dolu', firstRecord.periodMonth != null, `val=${firstRecord.periodMonth}`);
} else {
  console.log('  ⚠ Kayıt yok, period alanları kontrol edilemiyor');
}

// ─── 4. POST /finance/records — Dönem seçimli kayıt ─────────────────────────

console.log('\n[4] CRUD + Dönem');

// Önce kategori bul (API ağaç yapısı döndürür: root → children)
const cats = await api('GET', `${base}/categories`, null, token);

// Ağaçtan leaf (alt) kategori bul: children'ı olan root'un ilk child'ı
let catId = null;
for (const root of (cats.data ?? [])) {
  if (root.type === 'expense' && root.children?.length > 0) {
    catId = root.children[0].id;
    break;
  }
}
// Hiç alt kategori yoksa root kullan
if (!catId) {
  const expenseRoot = (cats.data ?? []).find(c => c.type === 'expense');
  catId = expenseRoot?.id;
}

if (!catId) {
  console.log('  ⚠ Gider kategorisi bulunamadı, CREATE test atlanıyor');
} else {
  // Create with explicit period
  const createRes = await api('POST', `${base}/records`, {
    categoryId: catId,
    type: 'expense',
    amount: 150.50,
    recordDate: '2026-03-01',
    description: 'Smoke test 3c3 dönem kayıt',
    paymentMethod: 'cash',
    periodYear: 2026,
    periodMonth: 1, // Kayıt Mart'ta girildi ama Ocak dönemine ait
  }, token);
  ok('POST records 201 (dönem seçimli)', createRes.status === 201, `status=${createRes.status} err=${JSON.stringify(createRes.data?.detail ?? createRes.data?.message ?? '')}`);

  const createdId = createRes.data?.id;
  if (createdId) {
    ok('periodYear=2026', createRes.data?.periodYear === 2026);
    ok('periodMonth=1', createRes.data?.periodMonth === 1);

    // Update period
    const updateRes = await api('PUT', `${base}/records/${createdId}`, {
      categoryId: catId,
      amount: 175.00,
      recordDate: '2026-03-01',
      description: 'Smoke test 3c3 güncellendi',
      paymentMethod: 'cash',
      periodYear: 2026,
      periodMonth: 2,
    }, token);
    ok('PUT records 200 (dönem güncelle)', updateRes.status === 200, `status=${updateRes.status}`);
    ok('periodMonth=2 (güncellendi)', updateRes.data?.periodMonth === 2);

    // Cleanup - soft delete
    const delRes = await api('DELETE', `${base}/records/${createdId}`, null, token);
    ok('DELETE records 204', delRes.status === 204);
  }

  // Invalid period test
  const invalidRes = await api('POST', `${base}/records`, {
    categoryId: catId,
    type: 'expense',
    amount: 50,
    recordDate: '2026-03-01',
    description: 'Geçersiz dönem testi',
    paymentMethod: 'cash',
    periodYear: 2019,
    periodMonth: 1,
  }, token);
  ok('POST invalid period → 422', invalidRes.status === 422, `status=${invalidRes.status}`);
}

// ─── 5. Rapor — Period bazlı vs Cash bazlı ──────────────────────────────────

console.log('\n[5] Rapor — reportBasis toggle');

// Monthly - period
const monthlyP = await api('GET', `${base}/reports/monthly?year=2026&month=3&reportBasis=period`, null, token);
ok('GET monthly period 200', monthlyP.status === 200, `status=${monthlyP.status}`);

// Monthly - cash
const monthlyC = await api('GET', `${base}/reports/monthly?year=2026&month=3&reportBasis=cash`, null, token);
ok('GET monthly cash 200', monthlyC.status === 200, `status=${monthlyC.status}`);

// Annual - period
const annualP = await api('GET', `${base}/reports/annual?year=2026&reportBasis=period`, null, token);
ok('GET annual period 200', annualP.status === 200, `status=${annualP.status}`);

// Annual - cash
const annualC = await api('GET', `${base}/reports/annual?year=2026&reportBasis=cash`, null, token);
ok('GET annual cash 200', annualC.status === 200, `status=${annualC.status}`);

// Budget comparison - period
const budgetP = await api('GET', `${base}/reports/budget-comparison?year=2026&month=3&reportBasis=period`, null, token);
ok('GET budget-comparison period 200', budgetP.status === 200, `status=${budgetP.status}`);

// Resident summary - period
const residentP = await api('GET', `${base}/reports/resident-summary?year=2026&month=3&reportBasis=period`, null, token);
ok('GET resident-summary period 200', residentP.status === 200, `status=${residentP.status}`);

// Resident summary - cash
const residentC = await api('GET', `${base}/reports/resident-summary?year=2026&month=3&reportBasis=cash`, null, token);
ok('GET resident-summary cash 200', residentC.status === 200, `status=${residentC.status}`);

// ─── 6. Default davranış (reportBasis parametresi yok) ──────────────────────

console.log('\n[6] Default davranış (reportBasis yok → period)');
const monthlyDefault = await api('GET', `${base}/reports/monthly?year=2026&month=3`, null, token);
ok('GET monthly (no param) 200', monthlyDefault.status === 200);

// ─── 7. Kayıt listesi dönem filtresi ────────────────────────────────────────

console.log('\n[7] Kayıt listesi dönem filtresi');
const filteredRecords = await api('GET', `${base}/records?periodYear=2026&periodMonth=3`, null, token);
ok('GET records?periodYear=2026&periodMonth=3 → 200', filteredRecords.status === 200, `status=${filteredRecords.status}`);
// Her kayıt periodYear=2026, periodMonth=3 olmalı
const allMatch = (filteredRecords.data?.items ?? []).every(r => r.periodYear === 2026 && r.periodMonth === 3);
ok('Filtre doğru çalışıyor (tüm kayıtlar Mart 2026)', allMatch || (filteredRecords.data?.items ?? []).length === 0);

// ─── 8. reportBasis validasyonu ─────────────────────────────────────────────

console.log('\n[8] reportBasis validasyonu');
const invalidBasis = await api('GET', `${base}/reports/monthly?year=2026&month=3&reportBasis=invalid`, null, token);
ok('GET monthly reportBasis=invalid → 422', invalidBasis.status === 422, `status=${invalidBasis.status}`);

// ─── Sonuç ──────────────────────────────────────────────────────────────────

console.log(`\n${'═'.repeat(50)}`);
console.log(`  ✓ ${passed} passed   ✗ ${failed} failed   (toplam ${passed + failed})`);
console.log(`${'═'.repeat(50)}\n`);
process.exit(failed > 0 ? 1 : 0);

/**
 * Phase 3e — Ariza Bildirimi Smoke Test
 * Calistirmak icin: node scripts/smoke-test-3e.mjs
 * Onkosul: API http://localhost:5100'de calisiyor olmali + migration uygulanmis olmali
 */

const SUPABASE_URL   = 'https://jkyzxiyemxkbhifohpcg.supabase.co';
const SUPABASE_ANON  = 'sb_publishable_IWlNZIGHzhapQv3h9Y-d-Q_ea3vmIWI';
const API_URL        = 'http://localhost:5100';
const ADMIN_EMAIL    = 'mehmet.ayan1741@gmail.com';
const ADMIN_PASSWORD = '123456789';
const SAKIN_EMAIL    = 'mehmet.ayan1741+kubra@gmail.com';
const SAKIN_PASSWORD = '123456789';

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

async function apiPatch(path, body, token) {
  return api('PATCH', path, body, token);
}

async function login(email, password) {
  const res = await fetch(
    `${SUPABASE_URL}/auth/v1/token?grant_type=password`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', apikey: SUPABASE_ANON },
      body: JSON.stringify({ email, password }),
    }
  );
  const data = await res.json();
  return data.access_token;
}

// ─── Auth ────────────────────────────────────────────────────────────────────

console.log('\n[1] Auth — Admin login');
const adminToken = await login(ADMIN_EMAIL, ADMIN_PASSWORD);
ok('Admin login basarili', !!adminToken);
if (!adminToken) { console.log('\n❌ Token alinamadi, test durduruluyor.'); process.exit(1); }

console.log('\n[2] Auth — Sakin login');
const sakinToken = await login(SAKIN_EMAIL, SAKIN_PASSWORD);
ok('Sakin login basarili', !!sakinToken);

// ─── orgId + unitId ──────────────────────────────────────────────────────────

console.log('\n[3] GET /me — orgId al');
const me = await api('GET', '/api/v1/me', null, adminToken);
ok('/me 200', me.status === 200);
const orgId = me.data?.memberships?.[0]?.organizationId;
ok('orgId var', !!orgId);
if (!orgId) { console.log('\n❌ orgId alinamadi.'); process.exit(1); }

const base = `/api/v1/organizations/${orgId}/maintenance-requests`;

// ─── TEST 1: Sakin ariza olusturur ───────────────────────────────────────────

console.log('\n[4] POST / — Sakin ariza olusturur');
const create1 = await api('POST', base, {
  title: 'Asansor arizasi — test',
  description: 'Asansor 3. katta durdu, acilmiyor',
  category: 'asansor',
  priority: 'yuksek',
  locationType: 'common_area',
  locationNote: 'A Blok asansoru'
}, sakinToken);
ok('201 Created', create1.status === 201, `status=${create1.status}`);
ok('status=reported', create1.data?.status === 'reported');
ok('isRecurring=false', create1.data?.isRecurring === false);
ok('slaDeadlineAt var', !!create1.data?.slaDeadlineAt);
const mrId = create1.data?.id;
ok('id var', !!mrId);

// ─── TEST 2: Ariza listesi (sakin) ──────────────────────────────────────────

console.log('\n[5] GET / — Sakin ariza listesi');
const list1 = await api('GET', base, null, sakinToken);
ok('200 OK', list1.status === 200);
ok('items array', Array.isArray(list1.data?.items));
ok('totalCount >= 1', list1.data?.totalCount >= 1);

// ─── TEST 3: Ariza listesi (admin) ──────────────────────────────────────────

console.log('\n[6] GET / — Admin ariza listesi');
const list2 = await api('GET', base, null, adminToken);
ok('200 OK', list2.status === 200);
ok('admin tum arizalari gorur', list2.data?.totalCount >= 1);

// ─── TEST 4: Ariza detay ────────────────────────────────────────────────────

console.log('\n[7] GET /{id} — Ariza detay');
const detail1 = await api('GET', `${base}/${mrId}`, null, sakinToken);
ok('200 OK', detail1.status === 200);
ok('detail var', !!detail1.data?.detail);
ok('timeline var', Array.isArray(detail1.data?.timeline));
ok('comments var', Array.isArray(detail1.data?.comments));
ok('timeline[0].toStatus=reported', detail1.data?.timeline?.[0]?.toStatus === 'reported');

// ─── TEST 5: Durum guncelle: reported→in_review ─────────────────────────────

console.log('\n[8] PATCH /{id}/status — reported→in_review');
const status1 = await apiPatch(`${base}/${mrId}/status`, { status: 'in_review', note: 'Incelemeye alindi' }, adminToken);
ok('204 NoContent', status1.status === 204, `status=${status1.status}`);

// ─── TEST 6: Gecersiz gecis: in_review→resolved ─────────────────────────────

console.log('\n[9] PATCH /{id}/status — in_review→resolved (gecersiz)');
const status2 = await apiPatch(`${base}/${mrId}/status`, { status: 'resolved' }, adminToken);
ok('422 Gecersiz gecis', status2.status === 422, `status=${status2.status}`);

// ─── TEST 7: Usta ata ───────────────────────────────────────────────────────

console.log('\n[10] PATCH /{id}/assignee — Usta ata');
const assign1 = await apiPatch(`${base}/${mrId}/assignee`, {
  name: 'Mehmet Usta', phone: '05551234567', note: 'Asansor tamircisi'
}, adminToken);
ok('204 NoContent', assign1.status === 204, `status=${assign1.status}`);

// Durum assigned olmus mu kontrol et
const detail2 = await api('GET', `${base}/${mrId}`, null, adminToken);
ok('status=assigned', detail2.data?.detail?.status === 'assigned', `status=${detail2.data?.detail?.status}`);
ok('assigneeName=Mehmet Usta', detail2.data?.detail?.assigneeName === 'Mehmet Usta');

// ─── TEST 8: Yorum ekle (sakin, multipart text only) ────────────────────────

console.log('\n[11] POST /{id}/comments — Sakin yorum');
const commentForm = new FormData();
commentForm.append('content', 'Asansor hala calismyor, acil bakilmali');
const commentRes = await fetch(`${API_URL}${base}/${mrId}/comments`, {
  method: 'POST',
  headers: { Authorization: `Bearer ${sakinToken}` },
  body: commentForm,
});
let commentData;
try { commentData = await commentRes.json(); } catch { commentData = null; }
ok('201 Created', commentRes.status === 201, `status=${commentRes.status}`);
ok('content var', !!commentData?.content);

// ─── TEST 9: Yorum ekle (admin) ─────────────────────────────────────────────

console.log('\n[12] POST /{id}/comments — Admin yorum');
const commentForm2 = new FormData();
commentForm2.append('content', 'Usta yarin gelecek');
const commentRes2 = await fetch(`${API_URL}${base}/${mrId}/comments`, {
  method: 'POST',
  headers: { Authorization: `Bearer ${adminToken}` },
  body: commentForm2,
});
ok('201 Created', commentRes2.status === 201, `status=${commentRes2.status}`);

// ─── TEST 10: Sakin durum degistiremez ───────────────────────────────────────

console.log('\n[13] PATCH /{id}/status — Sakin durum degistiremez');
const status3 = await apiPatch(`${base}/${mrId}/status`, { status: 'in_progress' }, sakinToken);
ok('403 Forbidden', status3.status === 403, `status=${status3.status}`);

// ─── TEST 11: Maliyet ekle ──────────────────────────────────────────────────

console.log('\n[14] POST /{id}/costs — Maliyet ekle');
// Once in_progress'e gecirelim
await apiPatch(`${base}/${mrId}/status`, { status: 'in_progress' }, adminToken);
const cost1 = await api('POST', `${base}/${mrId}/costs`, {
  amount: 1500.50, description: 'Asansor motor tamiri'
}, adminToken);
ok('201 Created', cost1.status === 201, `status=${cost1.status}`);
ok('amount=1500.50', cost1.data?.amount === 1500.5);
const costId = cost1.data?.id;

// total_cost guncellenmis mi?
const detail3 = await api('GET', `${base}/${mrId}`, null, adminToken);
ok('totalCost=1500.50', detail3.data?.detail?.totalCost === 1500.5, `totalCost=${detail3.data?.detail?.totalCost}`);

// ─── TEST 12: Maliyeti gelir-gidere aktar ───────────────────────────────────

console.log('\n[15] POST /{id}/costs/{cid}/transfer — Maliyeti gelir-gidere aktar');
const transfer1 = await api('POST', `${base}/${mrId}/costs/${costId}/transfer`, null, adminToken);
ok('200 OK', transfer1.status === 200, `status=${transfer1.status}`);
ok('financeRecordId var', !!transfer1.data?.financeRecordId);

// Tekrar transfer → 422
const transfer2 = await api('POST', `${base}/${mrId}/costs/${costId}/transfer`, null, adminToken);
ok('422 zaten aktarilmis', transfer2.status === 422, `status=${transfer2.status}`);

// ─── TEST 13: resolved → rate ───────────────────────────────────────────────

console.log('\n[16] Resolved + Rate akisi');
// in_progress → resolved
const status4 = await apiPatch(`${base}/${mrId}/status`, { status: 'resolved', note: 'Asansor tamiri tamamlandi' }, adminToken);
ok('204 resolved', status4.status === 204, `status=${status4.status}`);

// Puan ver (sakin, resolved)
const rate1 = await api('POST', `${base}/${mrId}/rate`, { rating: 5, comment: 'Harika is cikardi' }, sakinToken);
ok('204 puan verildi', rate1.status === 204, `status=${rate1.status}`);

// Artik closed olmali
const detail4 = await api('GET', `${base}/${mrId}`, null, adminToken);
ok('status=closed', detail4.data?.detail?.status === 'closed', `status=${detail4.data?.detail?.status}`);
ok('satisfactionRating=5', detail4.data?.detail?.satisfactionRating === 5);

// ─── TEST 14: Puan ver (resolved degil) ─────────────────────────────────────

console.log('\n[17] POST /{id}/rate — Closed arizaya puan (422)');
const rate2 = await api('POST', `${base}/${mrId}/rate`, { rating: 3 }, sakinToken);
ok('422 resolved degil', rate2.status === 422, `status=${rate2.status}`);

// ─── TEST 15: Closed arizaya yorum eklenemez ────────────────────────────────

console.log('\n[18] POST /{id}/comments — Closed arizaya yorum (422)');
const commentForm3 = new FormData();
commentForm3.append('content', 'Test yorum');
const commentRes3 = await fetch(`${API_URL}${base}/${mrId}/comments`, {
  method: 'POST',
  headers: { Authorization: `Bearer ${sakinToken}` },
  body: commentForm3,
});
ok('422 kapatilmis arizaya yorum', commentRes3.status === 422, `status=${commentRes3.status}`);

// ─── TEST 16: Istatistikler ─────────────────────────────────────────────────

console.log('\n[19] GET /stats — Istatistikler');
const stats1 = await api('GET', `${base}/stats`, null, adminToken);
ok('200 OK', stats1.status === 200, `status=${stats1.status}`);
ok('totalClosed >= 1', stats1.data?.totalClosed >= 1);
ok('totalCostSum > 0', stats1.data?.totalCostSum > 0);

// ─── TEST 17: Tekrarlayan ariza ─────────────────────────────────────────────

console.log('\n[20] POST / — 2. ariza (ayni kategori → isRecurring)');
const create2 = await api('POST', base, {
  title: 'Asansor yine bozuldu',
  description: 'Ayni asansor tekrar arizalandi',
  category: 'asansor',
  priority: 'acil',
  locationType: 'common_area',
  locationNote: 'A Blok asansoru'
}, sakinToken);
ok('201 Created', create2.status === 201, `status=${create2.status}`);
ok('isRecurring=true', create2.data?.isRecurring === true, `isRecurring=${create2.data?.isRecurring}`);
const mrId2 = create2.data?.id;

// ─── TEST 18: Cancelled arizanin durumu degistiremez ────────────────────────

console.log('\n[21] Cancelled ariza durumu');
// reported → cancelled
await apiPatch(`${base}/${mrId2}/status`, { status: 'cancelled', note: 'Yanlis bildirim' }, adminToken);
const status5 = await apiPatch(`${base}/${mrId2}/status`, { status: 'in_review' }, adminToken);
ok('422 cancelled→in_review gecersiz', status5.status === 422, `status=${status5.status}`);

// ─── TEST 19: Soft delete ───────────────────────────────────────────────────

console.log('\n[22] DELETE /{id} — Soft delete');
const del1 = await api('DELETE', `${base}/${mrId2}`, null, adminToken);
ok('204 NoContent', del1.status === 204, `status=${del1.status}`);

// Silinen ariza listede gorunmemeli
const list3 = await api('GET', `${base}?status=cancelled`, null, adminToken);
const foundDeleted = list3.data?.items?.find(i => i.id === mrId2);
ok('Silinen listede yok', !foundDeleted);

// ─── TEST 20: Filtre testi ──────────────────────────────────────────────────

console.log('\n[23] GET /?category=asansor — Filtre');
const list4 = await api('GET', `${base}?category=asansor`, null, adminToken);
ok('200 OK', list4.status === 200);
ok('filtrelenmis', list4.data?.items?.every(i => i.category === 'asansor'));

// ─── Ozet ────────────────────────────────────────────────────────────────────

console.log(`\n${'═'.repeat(50)}`);
console.log(`  Sonuc: ${passed} basarili, ${failed} basarisiz`);
console.log(`${'═'.repeat(50)}\n`);
process.exit(failed > 0 ? 1 : 0);

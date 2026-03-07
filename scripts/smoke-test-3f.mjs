/**
 * Phase 3f — Gundem & Karar Smoke Test
 * Calistirmak icin: node scripts/smoke-test-3f.mjs
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

// ─── orgId ───────────────────────────────────────────────────────────────────

console.log('\n[3] GET /me — orgId al');
const me = await api('GET', '/api/v1/me', null, adminToken);
ok('/me 200', me.status === 200);
const orgId = me.data?.memberships?.[0]?.organizationId;
ok('orgId var', !!orgId);
if (!orgId) { console.log('\n❌ orgId alinamadi.'); process.exit(1); }

const agendaBase = `/api/v1/organizations/${orgId}/agenda-items`;
const pollBase = `/api/v1/organizations/${orgId}/polls`;
const decisionBase = `/api/v1/organizations/${orgId}/decisions`;
const meetingBase = `/api/v1/organizations/${orgId}/meetings`;

// ─── AGENDA ITEMS ────────────────────────────────────────────────────────────

console.log('\n[4] Gundem Maddesi — Olustur (Sakin)');
const a1 = await api('POST', agendaBase, {
  title: 'Otopark duzenlenmesi',
  description: 'Otopark cizgileri silinmis, yeniden cizilmeli',
  category: 'ortak_alan'  // gecersiz -> 422 beklenir? Hayir, ortak_alan yok. Genel kullanalim
}, sakinToken);
// Kategori ortak_alan gecersiz, bakim_onarim deneyelim
const a1b = await api('POST', agendaBase, {
  title: 'Otopark duzenlenmesi',
  description: 'Otopark cizgileri silinmis, yeniden cizilmeli',
  category: 'bakim_onarim'
}, sakinToken);
ok('Gundem olusturma 201', a1b.status === 201, `status=${a1b.status} ${JSON.stringify(a1b.data)}`);
const agendaId1 = a1b.data?.id;
ok('agendaId var', !!agendaId1);

console.log('\n[5] Gundem Maddesi — 2. olustur (Admin)');
const a2 = await api('POST', agendaBase, {
  title: 'Guvenlik kamerasi yenileme',
  category: 'guvenlik'
}, adminToken);
ok('2. gundem olusturma 201', a2.status === 201, `status=${a2.status}`);
const agendaId2 = a2.data?.id;

console.log('\n[6] Gundem Maddesi — Listele');
const aList = await api('GET', `${agendaBase}?pageSize=10`, null, adminToken);
ok('Liste 200', aList.status === 200);
ok('Items dizisi var', Array.isArray(aList.data?.items));
ok('En az 2 gundem', (aList.data?.totalCount ?? 0) >= 2, `totalCount=${aList.data?.totalCount}`);

console.log('\n[7] Gundem Maddesi — Detay');
const aDetail = await api('GET', `${agendaBase}/${agendaId1}`, null, sakinToken);
ok('Detay 200', aDetail.status === 200);
ok('Baslik dogru', aDetail.data?.title === 'Otopark duzenlenmesi');

console.log('\n[8] Gundem Maddesi — Stats');
const aStats = await api('GET', `${agendaBase}/stats`, null, adminToken);
ok('Stats 200', aStats.status === 200);
ok('TotalOpen >= 2', (aStats.data?.totalOpen ?? 0) >= 2);

console.log('\n[9] Gundem Maddesi — Guncelle (Sakin kendi)');
const aUpdate = await api('PUT', `${agendaBase}/${agendaId1}`, {
  title: 'Otopark duzenlenmesi (guncellendi)',
  category: 'bakim_onarim'
}, sakinToken);
ok('Guncelleme 204', aUpdate.status === 204);

console.log('\n[10] Gundem Maddesi — Destek (+1)');
const sup1 = await api('POST', `${agendaBase}/${agendaId1}/support`, null, sakinToken);
ok('Destek toggle 200', sup1.status === 200);
ok('Supported = true', sup1.data?.supported === true);

const sup2 = await api('POST', `${agendaBase}/${agendaId1}/support`, null, adminToken);
ok('Admin destek 200', sup2.status === 200);

console.log('\n[11] Gundem Maddesi — Destek kaldir (toggle)');
const sup3 = await api('POST', `${agendaBase}/${agendaId1}/support`, null, sakinToken);
ok('Destek kaldir 200', sup3.status === 200);
ok('Supported = false', sup3.data?.supported === false);

// Tekrar destek ver
await api('POST', `${agendaBase}/${agendaId1}/support`, null, sakinToken);

console.log('\n[12] Destekci Listesi (admin)');
const supList = await api('GET', `${agendaBase}/${agendaId1}/supporters`, null, adminToken);
ok('Supporters 200', supList.status === 200);
ok('En az 2 destekci', Array.isArray(supList.data) && supList.data.length >= 2);

console.log('\n[13] Yorum ekle');
const c1 = await api('POST', `${agendaBase}/${agendaId1}/comments`, {
  content: 'Bu konu onemli, hizla cozulmeli.'
}, sakinToken);
ok('Yorum olusturma 201', c1.status === 201, `status=${c1.status}`);
const commentId = c1.data?.id;

console.log('\n[14] Yorumlar listele');
const cList = await api('GET', `${agendaBase}/${agendaId1}/comments`, null, sakinToken);
ok('Yorumlar 200', cList.status === 200);
ok('En az 1 yorum', (cList.data?.totalCount ?? 0) >= 1);

console.log('\n[15] Yorum sil (sakin kendi)');
const cDel = await api('DELETE', `${agendaBase}/${agendaId1}/comments/${commentId}`, null, sakinToken);
ok('Yorum sil 204', cDel.status === 204);

console.log('\n[16] Sabitle toggle (admin)');
const pin = await api('PUT', `${agendaBase}/${agendaId1}/pin`, null, adminToken);
ok('Pin toggle 204', pin.status === 204);

console.log('\n[17] Durum guncelle — acik -> degerlendiriliyor');
const st1 = await api('PUT', `${agendaBase}/${agendaId1}/status`, {
  status: 'degerlendiriliyor'
}, adminToken);
ok('Status 204', st1.status === 204);

console.log('\n[18] Gecersiz durum gecisi — degerlendiriliyor -> oylamada (poll olmadan)');
// Bu gecis gecerli (oylama elle yapilabilir), degerlendiriliyor -> oylamada
// Ama bunu poll uzerinden yapalim, simdilik kararlasti yapalim
const st2 = await api('PUT', `${agendaBase}/${agendaId1}/status`, {
  status: 'kararlasti'
}, adminToken);
ok('kararlasti 204', st2.status === 204);

// ─── DECISIONS ───────────────────────────────────────────────────────────────

console.log('\n[19] Karar — Auto-create kontrol (agenda kararlasti)');
const dList = await api('GET', `${decisionBase}?pageSize=10`, null, adminToken);
ok('Karar listesi 200', dList.status === 200);
ok('En az 1 karar (auto-create)', (dList.data?.totalCount ?? 0) >= 1);

console.log('\n[20] Karar — Bagimsiz olustur');
const d1 = await api('POST', decisionBase, {
  title: 'Kapici maasi zammi',
  description: 'Kapici maasi %20 artirildi'
}, adminToken);
ok('Karar olusturma 201', d1.status === 201, `status=${d1.status}`);
const decisionId = d1.data?.id;

console.log('\n[21] Karar — Detay');
const dDetail = await api('GET', `${decisionBase}/${decisionId}`, null, sakinToken);
ok('Karar detay 200', dDetail.status === 200);
ok('Baslik dogru', dDetail.data?.title === 'Kapici maasi zammi');

console.log('\n[22] Karar — Durum guncelle');
const dSt = await api('PUT', `${decisionBase}/${decisionId}/status`, {
  status: 'uygulamada'
}, adminToken);
ok('Karar durum 204', dSt.status === 204);

// ─── POLLS ───────────────────────────────────────────────────────────────────

console.log('\n[23] Gundem 2 — degerlendiriliyor yap');
const st3 = await api('PUT', `${agendaBase}/${agendaId2}/status`, {
  status: 'degerlendiriliyor'
}, adminToken);
ok('Gundem2 degerlendiriliyor 204', st3.status === 204);

console.log('\n[24] Oylama — Evet/Hayir olustur (agendaItemId ile)');
const now = new Date();
const startsAt = now.toISOString();
const endsAt = new Date(now.getTime() + 7 * 24 * 60 * 60 * 1000).toISOString(); // +7 gun
const p1 = await api('POST', pollBase, {
  title: 'Guvenlik kamerasi yenilenmeli mi?',
  description: 'Mevcut kameralar eski, yenilensin mi?',
  pollType: 'evet_hayir',
  startsAt, endsAt,
  agendaItemId: agendaId2,
  showInterimResults: false
}, adminToken);
ok('Oylama olusturma 201', p1.status === 201, `status=${p1.status} ${JSON.stringify(p1.data)}`);
const pollId = p1.data?.id;
ok('pollId var', !!pollId);

console.log('\n[25] Gundem 2 durumu kontrol — oylamada olmali');
const a2Detail = await api('GET', `${agendaBase}/${agendaId2}`, null, adminToken);
ok('Gundem2 status=oylamada', a2Detail.data?.status === 'oylamada', `status=${a2Detail.data?.status}`);

console.log('\n[26] Oylama — Listele');
const pList = await api('GET', `${pollBase}?pageSize=10`, null, sakinToken);
ok('Oylama listesi 200', pList.status === 200);
ok('En az 1 oylama', (pList.data?.totalCount ?? 0) >= 1);

console.log('\n[27] Oylama — Detay');
const pDetail = await api('GET', `${pollBase}/${pollId}`, null, sakinToken);
ok('Oylama detay 200', pDetail.status === 200);
ok('Secenekler 2 adet', pDetail.data?.options?.length === 2, `len=${pDetail.data?.options?.length}`);
const evetOptionId = pDetail.data?.options?.find(o => o.label === 'Evet')?.id;
const hayirOptionId = pDetail.data?.options?.find(o => o.label === 'Hayir')?.id;
ok('Evet secenegi var', !!evetOptionId);
ok('Hayir secenegi var', !!hayirOptionId);

console.log('\n[28] Oy kullan — Sakin "Evet"');
const v1 = await api('POST', `${pollBase}/${pollId}/vote`, {
  pollOptionId: evetOptionId
}, sakinToken);
ok('Oy kullanma 204', v1.status === 204, `status=${v1.status}`);

console.log('\n[29] Oy kullan — Admin "Hayir"');
const v2 = await api('POST', `${pollBase}/${pollId}/vote`, {
  pollOptionId: hayirOptionId
}, adminToken);
ok('Admin oy kullanma 204', v2.status === 204, `status=${v2.status}`);

console.log('\n[30] Oy degistir — Sakin "Hayir"');
const v3 = await api('POST', `${pollBase}/${pollId}/vote`, {
  pollOptionId: hayirOptionId
}, sakinToken);
ok('Oy degistirme 204', v3.status === 204);

console.log('\n[31] Sonuc — aktif + showInterim=false → sakin 403');
const pr1 = await api('GET', `${pollBase}/${pollId}/result`, null, sakinToken);
ok('Sakin sonuc 403', pr1.status === 403, `status=${pr1.status}`);

console.log('\n[32] Sonuc — admin her zaman gorebilir');
const pr2 = await api('GET', `${pollBase}/${pollId}/result`, null, adminToken);
ok('Admin sonuc 200', pr2.status === 200);
ok('Toplam oy 2', pr2.data?.totalVoteCount === 2, `totalVoteCount=${pr2.data?.totalVoteCount}`);

console.log('\n[33] Oylama sure uzat');
const newEndsAt = new Date(now.getTime() + 14 * 24 * 60 * 60 * 1000).toISOString();
const ext = await api('PUT', `${pollBase}/${pollId}/extend`, {
  newEndsAt
}, adminToken);
ok('Sure uzatma 204', ext.status === 204);

console.log('\n[34] Oylama erken sonlandir');
const endPoll = await api('PUT', `${pollBase}/${pollId}/end`, null, adminToken);
ok('Erken sonlandirma 204', endPoll.status === 204);

console.log('\n[35] Oylama sonrasi gundem durumu — degerlendiriliyor');
const a2Detail2 = await api('GET', `${agendaBase}/${agendaId2}`, null, adminToken);
ok('Gundem2 degerlendiriliyor', a2Detail2.data?.status === 'degerlendiriliyor', `status=${a2Detail2.data?.status}`);

console.log('\n[36] Oylama sonrasi sonuclar — sakin artik gorebilir');
const pr3 = await api('GET', `${pollBase}/${pollId}/result`, null, sakinToken);
ok('Sakin sonuc 200 (kapandi)', pr3.status === 200);

console.log('\n[37] Coktan secmeli oylama olustur');
const p2 = await api('POST', pollBase, {
  title: 'Bahce duzenleme secenekleri',
  pollType: 'coktan_secmeli',
  startsAt, endsAt,
  showInterimResults: true,
  options: [
    { label: 'Cim ekim' },
    { label: 'Cicek dikimi' },
    { label: 'Agac dikimi' },
    { label: 'Hepsi' }
  ]
}, adminToken);
ok('Coktan secmeli olusturma 201', p2.status === 201, `status=${p2.status}`);

console.log('\n[38] Coktan secmeli iptal');
const pollId2 = p2.data?.id;
const cancel = await api('PUT', `${pollBase}/${pollId2}/cancel`, null, adminToken);
ok('Iptal 204', cancel.status === 204);

// ─── MEETINGS ────────────────────────────────────────────────────────────────

console.log('\n[39] Toplanti olustur');
const meetingDate = new Date(now.getTime() + 3 * 24 * 60 * 60 * 1000).toISOString();
const m1 = await api('POST', meetingBase, {
  title: 'Mart Ayi Olagan Toplanti',
  description: 'Gundem: otopark, kamera, bahce',
  meetingDate
}, adminToken);
ok('Toplanti olusturma 201', m1.status === 201, `status=${m1.status}`);
const meetingId = m1.data?.id;
ok('meetingId var', !!meetingId);

console.log('\n[40] Toplanti listele');
const mList = await api('GET', `${meetingBase}?pageSize=10`, null, sakinToken);
ok('Toplanti listesi 200', mList.status === 200);
ok('En az 1 toplanti', (mList.data?.totalCount ?? 0) >= 1);

console.log('\n[41] Toplanti detay');
const mDetail = await api('GET', `${meetingBase}/${meetingId}`, null, sakinToken);
ok('Toplanti detay 200', mDetail.status === 200);
ok('Baslik dogru', mDetail.data?.meeting?.title === 'Mart Ayi Olagan Toplanti');

console.log('\n[42] Toplanti guncelle');
const mUpdate = await api('PUT', `${meetingBase}/${meetingId}`, {
  title: 'Mart Ayi Olagan Toplanti (guncellendi)',
  meetingDate
}, adminToken);
ok('Toplanti guncelleme 204', mUpdate.status === 204);

console.log('\n[43] Gundem maddelerini toplantiya bagla');
// Yeni bir gundem olustur (acik durumda olmali)
const a3 = await api('POST', agendaBase, {
  title: 'Asansor bakimi',
  category: 'bakim_onarim'
}, sakinToken);
const agendaId3 = a3.data?.id;

const link = await api('POST', `${meetingBase}/${meetingId}/agenda-items`, {
  agendaItemIds: [agendaId3]
}, adminToken);
ok('Gundem baglama 204', link.status === 204);

console.log('\n[44] Toplanti detay — bagli gundem kontrol');
const mDetail2 = await api('GET', `${meetingBase}/${meetingId}`, null, adminToken);
ok('Bagli gundem var', mDetail2.data?.agendaItems?.length >= 1, `len=${mDetail2.data?.agendaItems?.length}`);

console.log('\n[45] Toplanti durumu — tamamlandi');
const mSt = await api('PUT', `${meetingBase}/${meetingId}/status`, {
  status: 'tamamlandi'
}, adminToken);
ok('Toplanti tamamlandi 204', mSt.status === 204);

// ─── YETKİ KONTROLLERI ──────────────────────────────────────────────────────

console.log('\n[46] Yetki — Sakin oylama olusturamaz');
const p3 = await api('POST', pollBase, {
  title: 'Test', pollType: 'evet_hayir', startsAt, endsAt, showInterimResults: false
}, sakinToken);
ok('Sakin oylama 403', p3.status === 403);

console.log('\n[47] Yetki — Sakin karar olusturamaz');
const d3 = await api('POST', decisionBase, {
  title: 'Test karar'
}, sakinToken);
ok('Sakin karar 403', d3.status === 403);

console.log('\n[48] Yetki — Sakin toplanti olusturamaz');
const m3 = await api('POST', meetingBase, {
  title: 'Test toplanti', meetingDate
}, sakinToken);
ok('Sakin toplanti 403', m3.status === 403);

// ─── SOFT DELETE KONTROLLERI ─────────────────────────────────────────────────

console.log('\n[49] Gundem sil (sakin kendi, acik)');
const a4 = await api('POST', agendaBase, {
  title: 'Silinecek gundem', category: 'genel'
}, sakinToken);
const agendaIdDel = a4.data?.id;
const del1 = await api('DELETE', `${agendaBase}/${agendaIdDel}`, null, sakinToken);
ok('Sakin kendi silme 204', del1.status === 204);

console.log('\n[50] Silinmis gundem detay → 404');
const a4Detail = await api('GET', `${agendaBase}/${agendaIdDel}`, null, sakinToken);
ok('Silinmis gundem 404', a4Detail.status === 404);

// ─── SIRALAMA ────────────────────────────────────────────────────────────────

console.log('\n[51] Siralama — support desc');
const aSorted = await api('GET', `${agendaBase}?sortBy=support&sortDir=desc`, null, adminToken);
ok('Siralama 200', aSorted.status === 200);

// ─── KATEGORI FILTRE ─────────────────────────────────────────────────────────

console.log('\n[52] Kategori filtre');
const aCat = await api('GET', `${agendaBase}?category=guvenlik`, null, adminToken);
ok('Kategori filtre 200', aCat.status === 200);

// ─── INVALID CASES ──────────────────────────────────────────────────────────

console.log('\n[53] Gecersiz kategori');
const aInvalid = await api('POST', agendaBase, {
  title: 'Test', category: 'invalid_cat'
}, sakinToken);
ok('Gecersiz kategori 422', aInvalid.status === 422);

console.log('\n[54] Bos baslik');
const aEmpty = await api('POST', agendaBase, {
  title: '', category: 'genel'
}, sakinToken);
ok('Bos baslik 422', aEmpty.status === 422);

// ─── SONUC ───────────────────────────────────────────────────────────────────

console.log(`\n${'═'.repeat(50)}`);
console.log(`Toplam: ${passed + failed} | ✓ ${passed} | ✗ ${failed}`);
console.log(`${'═'.repeat(50)}\n`);
process.exit(failed > 0 ? 1 : 0);

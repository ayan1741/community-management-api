/**
 * Phase 3d — Duyuru & Bildirim Smoke Test
 * Çalıştırmak için: node scripts/smoke-test-3d.mjs
 * Önkoşul: API http://localhost:5100'de çalışıyor olmalı
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

// ─── 1. Auth ──────────────────────────────────────────────────────────────────

console.log('\n[1] Auth — Admin login');
const adminToken = await login(ADMIN_EMAIL, ADMIN_PASSWORD);
ok('Admin login başarılı', !!adminToken);
if (!adminToken) { console.log('\n❌ Token alınamadı, test durduruluyor.'); process.exit(1); }

console.log('\n[2] Auth — Sakin login');
const sakinToken = await login(SAKIN_EMAIL, SAKIN_PASSWORD);
ok('Sakin login başarılı', !!sakinToken);

// ─── 2. orgId ─────────────────────────────────────────────────────────────────

console.log('\n[3] GET /me — orgId al');
const me = await api('GET', '/api/v1/me', null, adminToken);
ok('/me 200', me.status === 200);
const orgId = me.data?.memberships?.[0]?.organizationId;
ok('orgId var', !!orgId);
if (!orgId) { console.log('\n❌ orgId alınamadı.'); process.exit(1); }

const base = `/api/v1/organizations/${orgId}/announcements`;
const notifBase = `/api/v1/organizations/${orgId}/notifications`;

// ─── 3. Duyuru Oluştur (Draft) ───────────────────────────────────────────────

console.log('\n[4] POST /announcements — Duyuru oluştur (taslak)');
const createRes = await api('POST', base, {
  title: 'Smoke Test Duyurusu',
  body: 'Bu bir smoke test duyurusudur. Phase 3d testi.',
  category: 'general',
  priority: 'normal',
  targetType: 'all',
  targetIds: null,
  expiresAt: null,
}, adminToken);
ok('POST 201', createRes.status === 201, `status=${createRes.status}`);
const announcementId = createRes.data?.id;
ok('id döndü', !!announcementId);
ok('status=draft', createRes.data?.status === 'draft', `status=${createRes.data?.status}`);
ok('category=general', createRes.data?.category === 'general');
ok('isPinned=false', createRes.data?.isPinned === false);

// ─── 4. Duyuru Listele ───────────────────────────────────────────────────────

console.log('\n[5] GET /announcements — Admin listele (draft dahil)');
const listRes = await api('GET', `${base}?status=draft`, null, adminToken);
ok('GET 200', listRes.status === 200);
ok('items dizisi', Array.isArray(listRes.data?.items));
const found = listRes.data?.items?.find(a => a.id === announcementId);
ok('Oluşturulan duyuru listede', !!found);

// ─── 5. Duyuru Güncelle ──────────────────────────────────────────────────────

console.log('\n[6] PUT /announcements/{id} — Güncelle (taslak)');
const updateRes = await api('PUT', `${base}/${announcementId}`, {
  title: 'Güncellenmiş Duyuru Başlığı',
  body: 'Güncellenmiş duyuru içeriği.',
  category: 'maintenance',
  priority: 'important',
  targetType: 'all',
  targetIds: null,
  expiresAt: null,
}, adminToken);
ok('PUT 200', updateRes.status === 200, `status=${updateRes.status}`);
ok('title güncellendi', updateRes.data?.title === 'Güncellenmiş Duyuru Başlığı');
ok('category güncellendi', updateRes.data?.category === 'maintenance');
ok('priority güncellendi', updateRes.data?.priority === 'important');

// ─── 6. Duyuru Detay ─────────────────────────────────────────────────────────

console.log('\n[7] GET /announcements/{id} — Detay');
const detailRes = await api('GET', `${base}/${announcementId}`, null, adminToken);
ok('GET 200', detailRes.status === 200, `status=${detailRes.status}`);
ok('body doğru', detailRes.data?.body === 'Güncellenmiş duyuru içeriği.');
ok('isRead alanı var', detailRes.data?.isRead !== undefined);

// ─── 7. Yayınla ──────────────────────────────────────────────────────────────

console.log('\n[8] POST /announcements/{id}/publish — Yayınla');
const publishRes = await api('POST', `${base}/${announcementId}/publish`, {}, adminToken);
ok('POST 200', publishRes.status === 200, `status=${publishRes.status}`);
ok('publishedAt döndü', !!publishRes.data?.publishedAt);
ok('targetMemberCount > 0', publishRes.data?.targetMemberCount > 0, `count=${publishRes.data?.targetMemberCount}`);
ok('notificationCount döndü', publishRes.data?.notificationCount >= 0, `count=${publishRes.data?.notificationCount}`);

// ─── 8. Yayınlanmış duyuru tekrar yayınlanamaz ───────────────────────────────

console.log('\n[9] POST /announcements/{id}/publish — Tekrar yayınlama (hata)');
const republishRes = await api('POST', `${base}/${announcementId}/publish`, {}, adminToken);
ok('Tekrar yayınlama 409', republishRes.status === 409, `status=${republishRes.status}`);

// ─── 9. Taslak olmayan duyuru güncellenemez ──────────────────────────────────

console.log('\n[10] PUT /announcements/{id} — Yayınlanmış güncelleme (hata)');
const updatePublishedRes = await api('PUT', `${base}/${announcementId}`, {
  title: 'X', body: 'X', category: 'general', priority: 'normal',
  targetType: 'all', targetIds: null, expiresAt: null,
}, adminToken);
ok('Yayınlanmış güncelleme 403/409', [403, 409].includes(updatePublishedRes.status), `status=${updatePublishedRes.status}`);

// ─── 10. Pin ─────────────────────────────────────────────────────────────────

console.log('\n[11] PATCH /announcements/{id}/pin — Pinle');
const pinRes = await apiPatch(`${base}/${announcementId}/pin`, { isPinned: true }, adminToken);
ok('PATCH 204', pinRes.status === 204, `status=${pinRes.status}`);

const afterPin = await api('GET', `${base}/${announcementId}`, null, adminToken);
ok('isPinned=true', afterPin.data?.isPinned === true);

// Unpinle
const unpinRes = await apiPatch(`${base}/${announcementId}/pin`, { isPinned: false }, adminToken);
ok('Unpin 204', unpinRes.status === 204);

// ─── 11. Okunma İstatistikleri ───────────────────────────────────────────────

console.log('\n[12] GET /announcements/{id}/reads — Okunma istatistikleri');
const readsRes = await api('GET', `${base}/${announcementId}/reads?tab=readers`, null, adminToken);
ok('GET reads 200', readsRes.status === 200, `status=${readsRes.status}`);
ok('readCount alanı var', readsRes.data?.readCount >= 0, `readCount=${readsRes.data?.readCount}`);
ok('readersTotal alanı var', readsRes.data?.readersTotal >= 0);
ok('readers dizisi', Array.isArray(readsRes.data?.readers));

const unreadsRes = await api('GET', `${base}/${announcementId}/reads?tab=nonReaders`, null, adminToken);
ok('GET nonReaders 200', unreadsRes.status === 200);
ok('nonReaders dizisi', Array.isArray(unreadsRes.data?.nonReaders));

// ─── 12. Sakin Bildirim Kontrolleri ──────────────────────────────────────────

if (sakinToken) {
  console.log('\n[13] GET /notifications — Sakin bildirimleri');
  const notifRes = await api('GET', notifBase, null, sakinToken);
  ok('GET notifications 200', notifRes.status === 200, `status=${notifRes.status}`);
  ok('items dizisi', Array.isArray(notifRes.data?.items));
  const announcementNotif = notifRes.data?.items?.find(n => n.referenceId === announcementId);
  ok('Duyuru bildirimi geldi', !!announcementNotif, announcementNotif ? '' : 'bildirim bulunamadı');

  console.log('\n[14] GET /notifications/unread-count — Okunmamış sayısı');
  const unreadRes = await api('GET', `${notifBase}/unread-count`, null, sakinToken);
  ok('GET unread-count 200', unreadRes.status === 200);
  ok('unreadCount >= 1', unreadRes.data?.unreadCount >= 1, `count=${unreadRes.data?.unreadCount}`);

  console.log('\n[15] POST /notifications/mark-read — Toplu okundu işaretle');
  const markReadRes = await api('POST', `${notifBase}/mark-read`, { notificationIds: null }, sakinToken);
  ok('POST mark-read 204', markReadRes.status === 204, `status=${markReadRes.status}`);

  const afterMarkRead = await api('GET', `${notifBase}/unread-count`, null, sakinToken);
  ok('unreadCount = 0', afterMarkRead.data?.unreadCount === 0, `count=${afterMarkRead.data?.unreadCount}`);

  console.log('\n[16] GET /announcements/{id} — Sakin duyuru detayı (ilk açış okundu kaydı yapar)');
  const sakinDetail1 = await api('GET', `${base}/${announcementId}`, null, sakinToken);
  ok('Sakin detay 200', sakinDetail1.status === 200);
  // İlk çağrı okundu kaydını yapar, ikinci çağrıda isRead=true döner
  const sakinDetail2 = await api('GET', `${base}/${announcementId}`, null, sakinToken);
  ok('Sakin isRead=true (2. çağrıda)', sakinDetail2.data?.isRead === true);
}

// ─── 13. Validasyon Testleri ─────────────────────────────────────────────────

console.log('\n[17] Validasyon — Boş başlık');
const emptyTitle = await api('POST', base, {
  title: '', body: 'test', category: 'general', priority: 'normal',
  targetType: 'all', targetIds: null, expiresAt: null,
}, adminToken);
ok('Boş başlık 422', emptyTitle.status === 422, `status=${emptyTitle.status}`);

console.log('\n[18] Validasyon — Geçersiz kategori');
const invalidCat = await api('POST', base, {
  title: 'Test', body: 'test', category: 'invalid', priority: 'normal',
  targetType: 'all', targetIds: null, expiresAt: null,
}, adminToken);
ok('Geçersiz kategori 422', invalidCat.status === 422, `status=${invalidCat.status}`);

console.log('\n[19] Validasyon — Block target, boş targetIds');
const noTargetIds = await api('POST', base, {
  title: 'Test', body: 'test', category: 'general', priority: 'normal',
  targetType: 'block', targetIds: null, expiresAt: null,
}, adminToken);
ok('Block target + null ids 422', noTargetIds.status === 422, `status=${noTargetIds.status}`);

// ─── 14. Sakin yetki kontrolü ────────────────────────────────────────────────

if (sakinToken) {
  console.log('\n[20] Sakin — Duyuru oluşturma yetkisi yok');
  const sakinCreate = await api('POST', base, {
    title: 'Test', body: 'test', category: 'general', priority: 'normal',
    targetType: 'all', targetIds: null, expiresAt: null,
  }, sakinToken);
  ok('Sakin POST 403', sakinCreate.status === 403, `status=${sakinCreate.status}`);

  console.log('\n[21] Sakin — Silme yetkisi yok');
  const sakinDelete = await api('DELETE', `${base}/${announcementId}`, null, sakinToken);
  ok('Sakin DELETE 403', sakinDelete.status === 403, `status=${sakinDelete.status}`);
}

// ─── 15. Soft Delete ─────────────────────────────────────────────────────────

console.log('\n[22] DELETE /announcements/{id} — Soft delete');
const deleteRes = await api('DELETE', `${base}/${announcementId}`, null, adminToken);
ok('DELETE 204', deleteRes.status === 204, `status=${deleteRes.status}`);

const afterDelete = await api('GET', `${base}/${announcementId}`, null, adminToken);
ok('Silinen duyuru 404', afterDelete.status === 404, `status=${afterDelete.status}`);

// ─── 16. Yeni duyuru — block hedefli ────────────────────────────────────────

console.log('\n[23] Block hedefli duyuru — geçersiz blok');
const invalidBlock = await api('POST', base, {
  title: 'Block Test', body: 'test', category: 'general', priority: 'normal',
  targetType: 'block', targetIds: ['00000000-0000-0000-0000-000000000001'], expiresAt: null,
}, adminToken);
ok('Geçersiz blok 404', invalidBlock.status === 404, `status=${invalidBlock.status}`);

// ─── 17. Rol hedefli duyuru ──────────────────────────────────────────────────

console.log('\n[24] Rol hedefli duyuru — geçersiz rol');
const invalidRole = await api('POST', base, {
  title: 'Rol Test', body: 'test', category: 'general', priority: 'normal',
  targetType: 'role', targetIds: ['invalid_role'], expiresAt: null,
}, adminToken);
ok('Geçersiz rol 422', invalidRole.status === 422, `status=${invalidRole.status}`);

console.log('\n[25] Rol hedefli duyuru — geçerli');
const roleTargeted = await api('POST', base, {
  title: 'Admin Duyurusu', body: 'Sadece adminlere.', category: 'financial', priority: 'urgent',
  targetType: 'role', targetIds: ['admin'], expiresAt: null,
}, adminToken);
ok('Rol hedefli POST 201', roleTargeted.status === 201, `status=${roleTargeted.status}`);
ok('targetType=role', roleTargeted.data?.targetType === 'role');

// Temizlik
if (roleTargeted.data?.id) {
  await api('DELETE', `${base}/${roleTargeted.data.id}`, null, adminToken);
}

// ─── Özet ────────────────────────────────────────────────────────────────────

console.log('\n' + '═'.repeat(50));
console.log(`  Toplam: ${passed + failed} | Geçen: ${passed} | Kalan: ${failed}`);
console.log('═'.repeat(50));
process.exit(failed > 0 ? 1 : 0);

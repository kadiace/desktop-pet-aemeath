import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export const DEFAULT_PORT = 17515;
export const DEFAULT_PUBLIC_HOST = '172.21.10.15';

const sha256Cache = new Map();

export function normalizeVersion(tag) {
    return String(tag || '').trim().replace(/^v/i, '');
}

export function parseSemVer(tag) {
    const clean = normalizeVersion(tag);
    const [corePart, prePart = ''] = clean.split('-', 2);
    const nums = corePart.split('.').map(n => {
        const parsed = Number.parseInt(n, 10);
        return Number.isFinite(parsed) ? parsed : 0;
    });
    return {
        major: nums[0] ?? 0,
        minor: nums[1] ?? 0,
        patch: nums[2] ?? 0,
        pre: prePart,
        raw: tag
    };
}

export function compareSemVerDesc(aTag, bTag) {
    const a = parseSemVer(aTag);
    const b = parseSemVer(bTag);
    if (a.major !== b.major) return b.major - a.major;
    if (a.minor !== b.minor) return b.minor - a.minor;
    if (a.patch !== b.patch) return b.patch - a.patch;
    // Release > pre-release
    if (!a.pre && b.pre) return -1;
    if (a.pre && !b.pre) return 1;
    if (a.pre !== b.pre) return b.pre.localeCompare(a.pre);
    return b.raw.localeCompare(a.raw);
}

function getFileSha256(filePath, stat) {
    const cacheKey = `${filePath}|${stat.size}|${stat.mtimeMs}`;
    const cached = sha256Cache.get(cacheKey);
    if (cached) return cached;

    const hash = crypto.createHash('sha256');
    hash.update(fs.readFileSync(filePath));
    const digest = hash.digest('hex');
    sha256Cache.set(cacheKey, digest);
    return digest;
}

function findReleaseArchive(releasesDir, tag) {
    if (!fs.existsSync(releasesDir)) return null;
    const ver = normalizeVersion(tag);
    const candidates = [
        path.join(releasesDir, `AemeathCompanion-${tag}.zip`),
        path.join(releasesDir, `AemeathCompanion-v${ver}.zip`),
        path.join(releasesDir, `AemeathCompanion-${ver}.zip`),
        path.join(releasesDir, `${tag}.zip`),
        path.join(releasesDir, `${ver}.zip`),
        path.join(releasesDir, tag, `AemeathCompanion-${tag}.zip`),
        path.join(releasesDir, tag, `AemeathCompanion.zip`)
    ];

    for (const candidate of candidates) {
        if (fs.existsSync(candidate) && fs.statSync(candidate).isFile()) {
            return candidate;
        }
    }

    // Check if releases/<tag>/ contains any single .zip file
    const tagDir = path.join(releasesDir, tag);
    if (fs.existsSync(tagDir) && fs.statSync(tagDir).isDirectory()) {
        const zips = fs.readdirSync(tagDir).filter(f => f.toLowerCase().endsWith('.zip'));
        if (zips.length > 0) {
            return path.join(tagDir, zips[0]);
        }
    }

    return null;
}

export function loadGitReleases(repoDir, releasesDir) {
    let rawOutput = '';
    try {
        rawOutput = execFileSync(
            'git',
            [
                '-c', 'safe.directory=*',
                '-C', repoDir,
                'for-each-ref',
                '--sort=-version:refname',
                '--format=%(refname:short)\t%(objecttype)\t%(creatordate:iso-strict)\t%(subject)',
                'refs/tags'
            ],
            { encoding: 'utf8', windowsHide: true }
        );
    } catch {
        return [];
    }

    const lines = rawOutput
        .split(/\r?\n/)
        .map(line => line.trim())
        .filter(Boolean);

    const items = lines.map(line => {
        const [tag = '', objectType = '', releasedAt = '', ...noteParts] = line.split('\t');
        // Lightweight tags (objectType === 'commit') have no tag release note of their own;
        // do not fall back to the raw commit message — show '-' instead.
        const rawNote = noteParts.join('\t').trim();
        const notes = objectType === 'tag' && rawNote ? rawNote : '-';
        const archivePath = findReleaseArchive(releasesDir, tag);
        let fileName = null;
        let sizeBytes = 0;
        let sha256 = null;

        if (archivePath) {
            const stat = fs.statSync(archivePath);
            fileName = path.basename(archivePath);
            sizeBytes = stat.size;
            sha256 = getFileSha256(archivePath, stat);
        }

        return {
            tag,
            version: normalizeVersion(tag),
            releasedAt,
            notes,
            hasDownload: Boolean(archivePath),
            fileName,
            sizeBytes,
            sha256,
            downloadUrl: archivePath ? `/download/${encodeURIComponent(tag)}` : null,
            _archivePath: archivePath
        };
    });

    items.sort((a, b) => compareSemVerDesc(a.tag, b.tag));
    return items;
}

function toPublicRelease(item) {
    const { _archivePath, ...pub } = item;
    return pub;
}

function formatBytes(bytes) {
    if (!bytes || bytes <= 0) return '-';
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

function formatDate(iso) {
    if (!iso) return '-';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    const pad = n => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function escapeHtml(str) {
    return String(str ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function computeReleaseSignature(releases) {
    return releases
        .map(r => `${r.tag}|${r.hasDownload ? 1 : 0}|${r.sizeBytes}|${r.sha256 ?? ''}|${r.notes ?? ''}`)
        .join('\n');
}

function renderPortalHtml(releases, publicUrl, initialSignature) {
    const latest = releases[0] ?? null;
    const latestDownloadable = releases.find(r => r.hasDownload) ?? null;
    const heroRelease = latest;

    const heroButtonHtml = heroRelease
        ? (heroRelease.hasDownload
            ? `<a class="hero-btn" href="${escapeHtml(heroRelease.downloadUrl)}">
                 <span class="hero-btn-icon">⬇</span>
                 <span class="hero-btn-text">
                   <strong>최신 버전 다운로드 (${escapeHtml(heroRelease.tag)})</strong>
                   <small>${escapeHtml(heroRelease.fileName)} · ${formatBytes(heroRelease.sizeBytes)}</small>
                 </span>
               </a>`
            : `<div class="hero-btn disabled">
                 <span class="hero-btn-icon">⏳</span>
                 <span class="hero-btn-text">
                   <strong>최신 태그 ${escapeHtml(heroRelease.tag)} 패키지 대기 중</strong>
                   <small>scripts/publish-release.ps1 -Version ${escapeHtml(heroRelease.version)} 로 패키지를 생성해 주세요</small>
                 </span>
               </div>`)
        : `<div class="hero-btn disabled">
             <span class="hero-btn-icon">✦</span>
             <span class="hero-btn-text">
               <strong>등록된 Git 태그가 없습니다</strong>
               <small>git tag v1.0.0 생성 후 자동으로 표시됩니다</small>
             </span>
           </div>`;

    const latestMetaHtml = heroRelease
        ? `<div class="hero-meta">
             <span>버전: <b>${escapeHtml(heroRelease.tag)}</b></span>
             <span>배포일: <b>${escapeHtml(formatDate(heroRelease.releasedAt))}</b></span>
             ${heroRelease.notes && heroRelease.notes !== '-' ? `<span>변경 사항: <b>${escapeHtml(heroRelease.notes)}</b></span>` : ''}
           </div>`
        : '';

    const rowsHtml = releases.length === 0
        ? `<tr><td colspan="5" class="empty">아직 Git 태그가 없습니다.</td></tr>`
        : releases.map((r, idx) => {
            const isLatest = idx === 0;
            const badge = isLatest ? `<span class="badge latest">LATEST</span>` : '';
            const dlCell = r.hasDownload
                ? `<a class="dl-btn" href="${escapeHtml(r.downloadUrl)}">⬇ 다운로드</a>`
                : `<span class="muted-pill">패키지 없음</span>`;
            return `
              <tr class="${isLatest ? 'row-latest' : ''}">
                <td class="col-ver"><strong>${escapeHtml(r.tag)}</strong> ${badge}</td>
                <td class="col-date">${escapeHtml(formatDate(r.releasedAt))}</td>
                <td class="col-notes">${escapeHtml(r.notes || '-')}</td>
                <td class="col-size">${formatBytes(r.sizeBytes)}</td>
                <td class="col-dl">${dlCell}</td>
              </tr>`;
        }).join('');

    return `<!DOCTYPE html>
<html lang="ko">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Aemeath, Digital Ghost — 내부 배포 포털</title>
  <style>
    :root {
      --bg: #18111d;
      --outline: #241928;
      --base: #231829;
      --panel: #312138;
      --track: #3e2a45;
      --plum: #684858;
      --rose: #a04068;
      --pink: #f0a0b8;
      --blush: #ffd0e0;
      --glow: #ffe0e0;
      --cream: #fff4f6;
      --lavender: #b0b0d8;
      --cyan: #88e8e8;
      --glint: #d0ffff;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      background:
        repeating-linear-gradient(0deg, rgba(18,10,22,0.22) 0 2px, transparent 2px 4px),
        radial-gradient(circle at 20% 0%, rgba(160,64,104,0.22), transparent 45%),
        radial-gradient(circle at 80% 10%, rgba(136,232,232,0.14), transparent 42%),
        var(--bg);
      color: var(--cream);
      font-family: 'Pretendard', 'Malgun Gothic', 'Segoe UI', sans-serif;
      padding: 28px 18px 48px;
    }
    .wrap {
      max-width: 920px;
      margin: 0 auto;
    }
    .pixel-card {
      background: var(--base);
      border: 2px solid var(--blush);
      box-shadow: 0 0 0 2px var(--outline), inset 0 0 0 2px var(--rose), 0 12px 32px rgba(0,0,0,0.45);
      padding: 24px;
      margin-bottom: 22px;
      position: relative;
    }
    .header-top {
      display: flex;
      align-items: center;
      gap: 20px;
      flex-wrap: wrap;
    }
    .mascot {
      width: 92px;
      height: 92px;
      image-rendering: pixelated;
      background: var(--panel);
      border: 2px solid var(--plum);
      padding: 4px;
      flex-shrink: 0;
    }
    .title-block h1 {
      margin: 0;
      font-size: 26px;
      color: var(--cream);
      letter-spacing: -0.02em;
    }
    .title-block .sub {
      margin-top: 6px;
      color: var(--cyan);
      font-size: 13px;
      font-weight: 700;
    }
    .hero-btn {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 14px;
      margin-top: 22px;
      padding: 18px 24px;
      text-decoration: none;
      background: linear-gradient(180deg, var(--rose), #7d2f52);
      color: var(--cream);
      border: 2px solid var(--blush);
      box-shadow: 0 0 0 2px var(--outline), inset 0 2px 0 rgba(255,224,224,0.35);
      transition: transform 0.08s ease, border-color 0.12s ease, filter 0.12s ease;
    }
    .hero-btn:not(.disabled):hover {
      border-color: var(--glint);
      filter: brightness(1.09);
      transform: translateY(-1px);
    }
    .hero-btn:not(.disabled):active {
      transform: translateY(1px);
    }
    .hero-btn.disabled {
      background: var(--panel);
      border-color: var(--plum);
      color: var(--lavender);
      cursor: default;
    }
    .hero-btn-icon {
      font-size: 26px;
      color: var(--glint);
    }
    .hero-btn-text {
      display: flex;
      flex-direction: column;
      gap: 3px;
      text-align: left;
    }
    .hero-btn-text strong {
      font-size: 19px;
      color: var(--cream);
    }
    .hero-btn-text small {
      font-size: 12px;
      color: var(--blush);
    }
    .hero-meta {
      margin-top: 14px;
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
      font-size: 13px;
      color: var(--lavender);
    }
    .hero-meta b { color: var(--glow); }
    h2 {
      margin: 0 0 14px;
      font-size: 16px;
      color: var(--cyan);
      display: flex;
      align-items: center;
      justify-content: space-between;
    }
    .count-tag {
      font-size: 12px;
      color: var(--blush);
      background: var(--panel);
      border: 1px solid var(--plum);
      padding: 2px 8px;
    }
    .table-wrap { overflow-x: auto; }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: 13.5px;
    }
    th {
      text-align: left;
      padding: 10px 12px;
      background: var(--panel);
      color: var(--pink);
      border-bottom: 2px solid var(--plum);
      font-size: 12px;
      white-space: nowrap;
    }
    td {
      padding: 12px;
      border-bottom: 1px solid var(--track);
      vertical-align: middle;
    }
    tr.row-latest {
      background: rgba(160, 64, 104, 0.14);
    }
    .badge.latest {
      display: inline-block;
      margin-left: 6px;
      padding: 2px 6px;
      font-size: 10px;
      font-weight: 800;
      color: var(--outline);
      background: var(--cyan);
    }
    .col-ver { white-space: nowrap; }
    .col-date, .col-size, .col-dl { white-space: nowrap; }
    .col-notes { color: var(--glow); }
    .dl-btn {
      display: inline-block;
      padding: 6px 12px;
      background: var(--panel);
      border: 1px solid var(--blush);
      color: var(--cream);
      text-decoration: none;
      font-weight: 700;
      font-size: 12px;
    }
    .dl-btn:hover {
      background: var(--rose);
      border-color: var(--glint);
      color: var(--glint);
    }
    .muted-pill {
      display: inline-block;
      padding: 5px 10px;
      background: var(--track);
      color: var(--lavender);
      font-size: 11.5px;
    }
    .empty {
      text-align: center;
      color: var(--lavender);
      padding: 28px 12px;
    }
    .footer-links {
      display: flex;
      flex-wrap: wrap;
      gap: 14px;
      justify-content: flex-end;
      align-items: center;
      font-size: 12px;
      color: var(--lavender);
    }
    .live-pill {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 3px 9px;
      background: var(--panel);
      border: 1px solid var(--plum);
      font-size: 11px;
      color: var(--cyan);
    }
    .live-dot {
      width: 6px;
      height: 6px;
      background: var(--cyan);
      display: inline-block;
    }
    .live-pill.offline { color: var(--pink); }
    .live-pill.offline .live-dot { background: var(--pink); }
    .reload-toast {
      position: fixed;
      right: 20px;
      bottom: 20px;
      padding: 12px 16px;
      background: var(--rose);
      color: var(--cream);
      border: 2px solid var(--glint);
      box-shadow: 0 0 0 2px var(--outline), 0 8px 24px rgba(0,0,0,0.45);
      font-size: 13px;
      font-weight: 700;
      display: none;
      z-index: 9999;
    }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="pixel-card">
      <div class="header-top">
        <img class="mascot" src="/assets/mascot.gif" alt="Aemeath" />
        <div class="title-block">
          <h1>✦ Aemeath, Digital Ghost</h1>
          <div class="sub">WINDOWS DESKTOP COMPANION // INTERNAL RELEASE PORTAL</div>
        </div>
      </div>
      ${heroButtonHtml}
      ${latestMetaHtml}
      ${!heroRelease?.hasDownload && latestDownloadable
        ? `<div class="hero-meta"><span>바로 다운로드 가능한 최근 패키지: <a style="color:var(--cyan)" href="${escapeHtml(latestDownloadable.downloadUrl)}">${escapeHtml(latestDownloadable.tag)} (${formatBytes(latestDownloadable.sizeBytes)})</a></span></div>`
        : ''}
    </section>

    <section class="pixel-card">
      <h2>
        <span>전체 버전 목록</span>
        <span class="count-tag">총 ${releases.length}개 버전</span>
      </h2>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>버전 (Tag)</th>
              <th>배포 일시</th>
              <th>릴리즈 노트</th>
              <th>파일 크기</th>
              <th>다운로드</th>
            </tr>
          </thead>
          <tbody>
            ${rowsHtml}
          </tbody>
        </table>
      </div>
    </section>

    <div class="footer-links">
      <div class="live-pill" id="livePill">
        <span class="live-dot"></span>
        <span id="liveText">새 태그 자동 새로고침 대기 중</span>
      </div>
    </div>
  </div>
  <div class="reload-toast" id="reloadToast"></div>
  <script>
    (function () {
      const initialSignature = ${JSON.stringify(initialSignature)};
      const livePill = document.getElementById('livePill');
      const liveText = document.getElementById('liveText');
      const toast = document.getElementById('reloadToast');
      let reloading = false;

      function triggerReload(tag) {
        if (reloading) return;
        reloading = true;
        toast.textContent = '✦ 새 릴리즈(' + (tag || 'updated') + ') 감지 — 자동 새로고침 중...';
        toast.style.display = 'block';
        setTimeout(() => window.location.reload(), 300);
      }

      async function checkForChange() {
        if (reloading) return;
        try {
          const resp = await fetch('/api/releases?t=' + Date.now(), { cache: 'no-store' });
          if (!resp.ok) return;
          const data = await resp.json();
          if (data.signature && data.signature !== initialSignature) {
            triggerReload(data.latestTag);
          }
        } catch {}
      }

      if (typeof EventSource !== 'undefined') {
        const es = new EventSource('/api/events');
        es.addEventListener('connected', (ev) => {
          livePill.classList.remove('offline');
          liveText.textContent = '실시간 태그 감지 연결됨';
          try {
            const data = JSON.parse(ev.data);
            if (data.signature && data.signature !== initialSignature) {
              triggerReload(data.latestTag);
            }
          } catch {}
        });
        es.addEventListener('releases-updated', (ev) => {
          try {
            const data = JSON.parse(ev.data);
            if (data.force || !data.signature || data.signature !== initialSignature) {
              triggerReload(data.latestTag);
            }
          } catch {
            triggerReload('');
          }
        });
        es.onerror = () => {
          livePill.classList.add('offline');
          liveText.textContent = '실시간 감지 재연결 중...';
        };
      }

      // Guaranteed fallback polling every 3s + immediate check when tab regains focus
      setInterval(checkForChange, 3000);
      document.addEventListener('visibilitychange', () => {
        if (!document.hidden) checkForChange();
      });
    })();
  </script>
</body>
</html>`;
}

export function createReleaseServer({
    repoDir = path.resolve(__dirname, '..'),
    releasesDir = path.resolve(__dirname, '..', 'releases'),
    publicHost = DEFAULT_PUBLIC_HOST,
    port = DEFAULT_PORT,
    enableWatcher = true
} = {}) {
    if (enableWatcher) {
        fs.mkdirSync(releasesDir, { recursive: true });
    }
    const mascotPath = path.join(repoDir, 'Assets', 'Sprites', 'idle1.gif');
    const sseClients = new Set();
    let lastReleases = loadGitReleases(repoDir, releasesDir);
    let lastSignature = computeReleaseSignature(lastReleases);
    let debounceTimer = null;
    const watchers = [];

    function broadcast(eventName, payload) {
        const frame = `event: ${eventName}\ndata: ${JSON.stringify(payload)}\n\n`;
        for (const client of sseClients) {
            try {
                client.write(frame);
            } catch {
                sseClients.delete(client);
            }
        }
    }

    function checkAndNotify(reason = 'watch', force = false) {
        const current = loadGitReleases(repoDir, releasesDir);
        const sig = computeReleaseSignature(current);
        const changed = sig !== lastSignature;
        lastReleases = current;
        lastSignature = sig;

        if (changed || force) {
            broadcast('releases-updated', {
                latestTag: current[0]?.tag ?? null,
                latestVersion: current[0]?.version ?? null,
                count: current.length,
                signature: sig,
                force,
                reason
            });
        }
        return { changed, latestTag: current[0]?.tag ?? null, count: current.length, notified: sseClients.size };
    }

    function scheduleWatchCheck(reason) {
        if (debounceTimer) clearTimeout(debounceTimer);
        debounceTimer = setTimeout(() => {
            debounceTimer = null;
            checkAndNotify(reason, false);
        }, 250);
    }

    let pollInterval = null;
    let heartbeatInterval = null;

    if (enableWatcher) {
        const watchTargets = [
            path.join(repoDir, '.git', 'refs', 'tags'),
            path.join(repoDir, '.git'),
            releasesDir
        ];
        for (const dir of watchTargets) {
            try {
                if (fs.existsSync(dir)) {
                    const w = fs.watch(dir, { persistent: false }, (_eventType, filename) => {
                        if (dir.endsWith('.git') && filename && filename !== 'packed-refs') return;
                        scheduleWatchCheck('fs-watch');
                    });
                    w.on('error', () => {});
                    watchers.push(w);
                }
            } catch {}
        }

        // Lightweight fallback poll every 3s in case Git atomic renames skip an fs.watch event
        pollInterval = setInterval(() => {
            if (sseClients.size > 0) checkAndNotify('poll', false);
        }, 3000);
        pollInterval.unref();

        // SSE keepalive every 25s
        heartbeatInterval = setInterval(() => {
            for (const client of sseClients) {
                try { client.write(': keepalive\n\n'); } catch { sseClients.delete(client); }
            }
        }, 25000);
        heartbeatInterval.unref();
    }

    const server = http.createServer((req, res) => {
        const reqUrl = new URL(req.url || '/', `http://${req.headers.host || 'localhost'}`);
        const pathname = reqUrl.pathname;
        const publicUrl = `http://${publicHost}:${port}`;

        const sendJson = (status, payload) => {
            const body = JSON.stringify(payload, null, 2);
            res.writeHead(status, {
                'Content-Type': 'application/json; charset=utf-8',
                'Cache-Control': 'no-store',
                'Access-Control-Allow-Origin': '*'
            });
            res.end(body);
        };

        // Webhook endpoint so publish-release.ps1 or Git hooks can push instant refresh notifications
        if (pathname === '/api/webhook/refresh' && (req.method === 'POST' || req.method === 'GET')) {
            const result = checkAndNotify('webhook', true);
            sendJson(200, { ok: true, ...result });
            return;
        }

        if (req.method !== 'GET' && req.method !== 'HEAD') {
            sendJson(405, { error: 'Method Not Allowed' });
            return;
        }

        if (pathname === '/api/events') {
            req.socket?.setNoDelay?.(true);
            res.writeHead(200, {
                'Content-Type': 'text/event-stream; charset=utf-8',
                'Cache-Control': 'no-cache, no-transform',
                'Connection': 'keep-alive',
                'X-Accel-Buffering': 'no',
                'Access-Control-Allow-Origin': '*'
            });
            res.flushHeaders?.();
            const current = loadGitReleases(repoDir, releasesDir);
            const sig = computeReleaseSignature(current);
            lastReleases = current;
            lastSignature = sig;
            res.write(`event: connected\ndata: ${JSON.stringify({ latestTag: current[0]?.tag ?? null, signature: sig })}\n\n`);
            sseClients.add(res);
            req.on('close', () => sseClients.delete(res));
            return;
        }

        if (pathname === '/api/health') {
            const releases = loadGitReleases(repoDir, releasesDir);
            sendJson(200, {
                ok: true,
                latestTag: releases[0]?.tag ?? null,
                releaseCount: releases.length,
                sseClients: sseClients.size
            });
            return;
        }

        if (pathname === '/api/version') {
            const releases = loadGitReleases(repoDir, releasesDir);
            const latest = releases[0];
            if (!latest) {
                sendJson(404, { error: 'No git tags found' });
                return;
            }
            sendJson(200, {
                latestTag: latest.tag,
                latestVersion: latest.version,
                releasedAt: latest.releasedAt,
                notes: latest.notes,
                hasDownload: latest.hasDownload,
                fileName: latest.fileName,
                sizeBytes: latest.sizeBytes,
                sha256: latest.sha256,
                downloadUrl: latest.downloadUrl,
                portalUrl: publicUrl
            });
            return;
        }

        if (pathname === '/api/releases') {
            const raw = loadGitReleases(repoDir, releasesDir);
            const releases = raw.map(toPublicRelease);
            sendJson(200, {
                latestTag: releases[0]?.tag ?? null,
                latestVersion: releases[0]?.version ?? null,
                signature: computeReleaseSignature(raw),
                portalUrl: publicUrl,
                releases
            });
            return;
        }

        if (pathname === '/assets/mascot.gif') {
            if (fs.existsSync(mascotPath)) {
                const stat = fs.statSync(mascotPath);
                res.writeHead(200, {
                    'Content-Type': 'image/gif',
                    'Content-Length': stat.size,
                    'Cache-Control': 'public, max-age=3600'
                });
                if (req.method === 'HEAD') { res.end(); return; }
                fs.createReadStream(mascotPath).pipe(res);
            } else {
                res.writeHead(404);
                res.end();
            }
            return;
        }

        if (pathname.startsWith('/download/')) {
            const requested = decodeURIComponent(pathname.slice('/download/'.length)).trim();
            const releases = loadGitReleases(repoDir, releasesDir);
            const target = requested.toLowerCase() === 'latest'
                ? (releases.find(r => r.hasDownload) ?? null)
                : (releases.find(r => r.tag === requested && r.hasDownload) ?? null);

            if (!target || !target._archivePath || !fs.existsSync(target._archivePath)) {
                sendJson(404, { error: `Release archive not found for '${requested}'` });
                return;
            }

            const stat = fs.statSync(target._archivePath);
            res.writeHead(200, {
                'Content-Type': 'application/zip',
                'Content-Length': stat.size,
                'Content-Disposition': `attachment; filename="${target.fileName}"`,
                'Cache-Control': 'no-store'
            });
            if (req.method === 'HEAD') { res.end(); return; }
            fs.createReadStream(target._archivePath).pipe(res);
            return;
        }

        if (pathname === '/' || pathname === '/index.html') {
            const releases = loadGitReleases(repoDir, releasesDir);
            const sig = computeReleaseSignature(releases);
            const html = renderPortalHtml(releases, publicUrl, sig);
            res.writeHead(200, {
                'Content-Type': 'text/html; charset=utf-8',
                'Cache-Control': 'no-store'
            });
            res.end(req.method === 'HEAD' ? undefined : html);
            return;
        }

        sendJson(404, { error: 'Not Found' });
    });

    server.on('close', () => {
        if (debounceTimer) clearTimeout(debounceTimer);
        if (pollInterval) clearInterval(pollInterval);
        if (heartbeatInterval) clearInterval(heartbeatInterval);
        for (const w of watchers) {
            try { w.close(); } catch {}
        }
        for (const client of sseClients) {
            try { client.end(); } catch {}
        }
        sseClients.clear();
    });

    return server;
}

// Direct CLI entrypoint
const isMain = process.argv[1] && path.resolve(process.argv[1]) === path.resolve(__filename);
if (isMain) {
    const args = process.argv.slice(2);
    let port = Number(process.env.PORT || DEFAULT_PORT);
    let host = process.env.HOST || '0.0.0.0';
    let publicHost = process.env.PUBLIC_HOST || DEFAULT_PUBLIC_HOST;

    for (let i = 0; i < args.length; i++) {
        if (args[i] === '--port' && args[i + 1]) port = Number(args[++i]);
        else if (args[i] === '--host' && args[i + 1]) host = args[++i];
        else if (args[i] === '--public-host' && args[i + 1]) publicHost = args[++i];
    }

    const repoDir = path.resolve(__dirname, '..');
    const releasesDir = path.join(repoDir, 'releases');
    fs.mkdirSync(releasesDir, { recursive: true });

    const server = createReleaseServer({ repoDir, releasesDir, publicHost, port });
    server.listen(port, host, () => {
        const releases = loadGitReleases(repoDir, releasesDir);
        console.log(`[Aemeath Release Server] Listening on http://${host}:${port}`);
        console.log(`[Aemeath Release Server] LAN Portal : http://${publicHost}:${port}/`);
        console.log(`[Aemeath Release Server] Version API: http://${publicHost}:${port}/api/version`);
        console.log(`[Aemeath Release Server] Git tags found: ${releases.length} (latest: ${releases[0]?.tag ?? 'none'})`);
    });
}

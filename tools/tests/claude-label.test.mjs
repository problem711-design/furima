// フェーズ 6-10 (§41.5): 共通ゴールデン仕様表の JS 側テスト (node --test)。
// DashboardHtml.cs の verbatim 文字列から導出関数を切り出し、
// app/src/test/resources/claude_label_fixtures.json の全ケースで
// 写真ラベル (claudePhotoLabel) とミューズカラム 5 タイル (updateClaudeColumn) を検証する。
// 実行: node --test tools/tests/ (リポジトリのどこからでも動くようパスは相対解決する)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');

const cs = readFileSync(path.join(repoRoot, 'tools', 'DashboardHtml.cs'), 'utf8');
const scriptMatch = cs.match(/<script>([\s\S]*?)<\/script>/);
assert.ok(scriptMatch, 'DashboardHtml.cs の inline script が見つからない');
const js = scriptMatch[1].replace(/""/g, '"');

// 宣言の開始位置から波括弧の対応で 1 ブロック分を切り出す
function extractBlock(src, marker) {
    const start = src.indexOf(marker);
    assert.ok(start >= 0, 'marker が見つからない: ' + marker);
    const braceStart = src.indexOf('{', start);
    let depth = 0;
    for (let i = braceStart; i < src.length; i++) {
        const ch = src[i];
        if (ch === '{') depth++;
        else if (ch === '}') {
            depth--;
            if (depth === 0) return src.slice(start, i + 1);
        }
    }
    throw new Error('波括弧が閉じない: ' + marker);
}

const windowDecl = js.match(/const CLAUDE_LABEL_DONE_WINDOW_MS = [^;]+;/);
assert.ok(windowDecl, 'CLAUDE_LABEL_DONE_WINDOW_MS が見つからない');

const prelude = `
let claudeJobsCache = [];
const __els = {};
const document = { getElementById: (id) => (__els[id] || (__els[id] = { innerText: '' })) };
`;
const api = `
globalThis.__api = {
    claudePhotoLabel: claudePhotoLabel,
    updateClaudeColumn: updateClaudeColumn,
    setJobs: (jobs) => { claudeJobsCache = jobs; },
    tileText: (id) => (__els[id] ? __els[id].innerText : null),
    resetTiles: () => { for (const k of Object.keys(__els)) delete __els[k]; }
};`;
const code = [
    prelude,
    extractBlock(js, 'const CLAUDE_LABEL_META'),
    windowDecl[0],
    extractBlock(js, 'function claudePhotoLabel'),
    extractBlock(js, 'function isClaudeErrorJob'),
    extractBlock(js, 'function updateClaudeColumn'),
    api
].join('\n');
const ctx = vm.createContext({});
vm.runInContext(code, ctx);
const apiObj = ctx.__api;
assert.ok(apiObj && typeof apiObj.claudePhotoLabel === 'function', '導出関数の読み込みに失敗');

const DAY = 24 * 60 * 60 * 1000;
const now = Date.now();
const fixtures = JSON.parse(readFileSync(path.join(repoRoot, 'app', 'src', 'test', 'resources', 'claude_label_fixtures.json'), 'utf8'));

function toJobs(c) {
    return c.jobs.map(j => ({
        itemUuid: j.itemUuid,
        queue: j.queue,
        status: j.status,
        photos: (j.photos || []).map(md5 => ({ md5: md5 })),
        results: (j.results || []).map(r => ({ sourceMd5: r.sourceMd5, resultMd5: r.resultMd5 })),
        errors: (j.errors || []).map(md5 => ({ sourceMd5: md5 })),
        errorDismissed: !!j.errorDismissed,
        createdAt: now - Math.round((j.createdDaysAgo || 0) * DAY),
        completedAt: (j.completedDaysAgo === null || j.completedDaysAgo === undefined) ? 0 : now - Math.round(j.completedDaysAgo * DAY)
    }));
}

function toSeenMap(c) {
    const m = {};
    for (const [k, v] of Object.entries(c.seenAtByItemUuid || {})) {
        m[String(k).toLowerCase()] = now - Math.round(v * DAY);
    }
    return m;
}

const TILE_IDS = {
    processing: 'claude-col-processing',
    startwait: 'claude-col-startwait',
    approval: 'claude-col-approval',
    waiting: 'claude-col-waiting',
    error: 'claude-col-error'
};

for (const c of fixtures.cases) {
    test('case ' + c.id + ': ' + (c.note || ''), () => {
        const jobs = toJobs(c);
        apiObj.setJobs(jobs);
        const label = apiObj.claudePhotoLabel(c.bundleUuids, c.targetMd5, toSeenMap(c));
        assert.equal(label ? label.key : null, c.expectLabel, '写真ラベルが期待と違う');

        apiObj.resetTiles();
        apiObj.updateClaudeColumn();
        for (const [key, id] of Object.entries(TILE_IDS)) {
            const text = apiObj.tileText(id);
            assert.ok(text !== null, 'タイル要素が更新されていない: ' + id);
            const n = parseInt(text, 10);
            assert.equal(n, (c.expectTiles || {})[key] || 0, 'タイル ' + key + ' の件数が期待と違う');
        }
    });
}

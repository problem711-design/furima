// Build 17: PC 管理画面の写真削除ターゲット構築 (空 MD5 壊れ参照の救済除去) と
// DashboardHtml.cs / WebDashboardHtml.kt 内スクリプト構文検証 (node --test tools/tests/)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');

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

test('DashboardHtml.cs 内の inline script 構文が有効で buildViewerTrashTargets が正常動作する', () => {
    const html = readFileSync(path.join(repoRoot, 'tools', 'web', 'dashboard.html'), 'utf8');
    const scriptMatch = html.match(/<script>([\s\S]*?)<\/script>/);
    assert.ok(scriptMatch, 'tools/web/dashboard.html の inline script が見つからない');
    const js = scriptMatch[1];

    // JS 全体の構文チェック
    new vm.Script(js, { filename: 'DashboardHtml.inline.js' });

    // フェーズ 6-14 (§48.6): buildViewerTrashTargets は選択キー (md5Hash 一次キー) を受け取り、
    // 現在の写真一覧へ解決してから対象を組み立てる。依存するヘルパーも併せて抽出する。
    const fnCode = extractBlock(js, 'function buildViewerTrashTargets');
    const keyCode = extractBlock(js, 'function viewerPhotoKey');
    const resolveCode = extractBlock(js, 'function resolveViewerPhotoByKey');
    const ctx = vm.createContext({});
    vm.runInContext(keyCode + '\n' + resolveCode + '\n' + fnCode + '\nglobalThis.__fn = buildViewerTrashTargets; globalThis.__key = viewerPhotoKey;', ctx);

    const photos = [
        { id: 101, md5Hash: 'abc123', childItemId: 0 },
        { id: 102, md5Hash: '', childItemId: 0 },
        { id: 103, md5Hash: '', childItemId: 55 },
        { id: 104, md5Hash: 'def456', childItemId: 55 }
    ];
    const keySetOf = list => new Set(list.map((p, i) => ctx.__key(p, i)));

    const mixedPlan = ctx.__fn(28, photos, keySetOf(photos));
    assert.equal(mixedPlan.normalCount, 2);
    assert.equal(mixedPlan.brokenCount, 2);
    assert.equal(mixedPlan.targets.length, 4);
    assert.deepEqual(
        JSON.parse(JSON.stringify(mixedPlan.targets.filter(t => !t.md5Hash))),
        [
            { itemId: 28, md5Hash: '', childItemId: 0, photoIndex: 1, photoId: 102 },
            { itemId: 28, md5Hash: '', childItemId: 55, photoIndex: 2, photoId: 103 }
        ]
    );

    const brokenOnlyPlan = ctx.__fn(28, photos, new Set([ctx.__key(photos[1], 1)]));
    assert.equal(brokenOnlyPlan.normalCount, 0);
    assert.equal(brokenOnlyPlan.brokenCount, 1);
    assert.equal(brokenOnlyPlan.targets[0].photoIndex, 1);
    assert.equal(brokenOnlyPlan.targets[0].photoId, 102);

    // 写真一覧の並びが変わっても、キーが指す写真そのものが対象になる (添字ズレ耐性 §48.6)
    const shuffled = [photos[2], photos[0], photos[3], photos[1]];
    const shuffledPlan = ctx.__fn(28, shuffled, keySetOf(photos));
    assert.equal(shuffledPlan.normalCount, 2);
    assert.equal(shuffledPlan.brokenCount, 2);
    assert.deepEqual(
        JSON.parse(JSON.stringify(shuffledPlan.targets.filter(t => !t.md5Hash).map(t => t.photoId).sort())),
        [102, 103]
    );
    const idxByPhotoId = new Map(shuffled.map((p, i) => [p.id, i]));
    for (const t of shuffledPlan.targets.filter(t => !t.md5Hash)) {
        assert.equal(t.photoIndex, idxByPhotoId.get(t.photoId));
    }
});

test('DashboardHtml.cs と WebDashboardHtml.kt のバージョン表記が v1.5 (Build 20) に統一されている', () => {
    const cs = readFileSync(path.join(repoRoot, 'tools', 'web', 'dashboard.html'), 'utf8');
    const kt = readFileSync(
        path.join(repoRoot, 'app', 'src', 'main', 'java', 'com', 'example', 'myapplication', 'data', 'server', 'WebDashboardHtml.kt'),
        'utf8'
    );
    const gradle = readFileSync(path.join(repoRoot, 'app', 'build.gradle.kts'), 'utf8');

    assert.ok(cs.includes('v1.5 (Build 20)'), 'DashboardHtml.cs のバージョン表記が未更新');
    assert.ok(kt.includes('v1.5 (Build 20)'), 'WebDashboardHtml.kt のバージョン表記が未更新');
    assert.ok(gradle.includes('versionCode = 1520'), 'app/build.gradle.kts の versionCode が未更新');
});

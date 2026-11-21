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
    const cs = readFileSync(path.join(repoRoot, 'tools', 'DashboardHtml.cs'), 'utf8');
    const scriptMatch = cs.match(/<script>([\s\S]*?)<\/script>/);
    assert.ok(scriptMatch, 'DashboardHtml.cs の inline script が見つからない');
    const js = scriptMatch[1].replace(/""/g, '"');

    // JS 全体の構文チェック
    new vm.Script(js, { filename: 'DashboardHtml.inline.js' });

    const fnCode = extractBlock(js, 'function buildViewerTrashTargets');
    const ctx = vm.createContext({});
    vm.runInContext(fnCode + '\nglobalThis.__fn = buildViewerTrashTargets;', ctx);

    const photos = [
        { id: 101, md5Hash: 'abc123', childItemId: 0 },
        { id: 102, md5Hash: '', childItemId: 0 },
        { id: 103, md5Hash: '', childItemId: 55 },
        { id: 104, md5Hash: 'def456', childItemId: 55 }
    ];

    const mixedPlan = ctx.__fn(28, photos, new Set([0, 1, 2, 3]));
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

    const brokenOnlyPlan = ctx.__fn(28, photos, new Set([1]));
    assert.equal(brokenOnlyPlan.normalCount, 0);
    assert.equal(brokenOnlyPlan.brokenCount, 1);
    assert.equal(brokenOnlyPlan.targets[0].photoIndex, 1);
    assert.equal(brokenOnlyPlan.targets[0].photoId, 102);
});

test('DashboardHtml.cs と WebDashboardHtml.kt のバージョン表記が v1.4 (Build 17) に統一されている', () => {
    const cs = readFileSync(path.join(repoRoot, 'tools', 'DashboardHtml.cs'), 'utf8');
    const kt = readFileSync(
        path.join(repoRoot, 'app', 'src', 'main', 'java', 'com', 'example', 'myapplication', 'data', 'server', 'WebDashboardHtml.kt'),
        'utf8'
    );
    const gradle = readFileSync(path.join(repoRoot, 'app', 'build.gradle.kts'), 'utf8');

    assert.ok(cs.includes('v1.4 (Build 17)'), 'DashboardHtml.cs のバージョン表記が未更新');
    assert.ok(kt.includes('v1.4 (Build 17)'), 'WebDashboardHtml.kt のバージョン表記が未更新');
    assert.ok(gradle.includes('versionCode = 1417'), 'app/build.gradle.kts の versionCode が未更新');
});

// フェーズ 6-16 (§51): PC 管理画面 UI/UX 改善・レイアウト不具合是正の契約検証
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');
const csPath = path.join(repoRoot, 'tools', 'DashboardHtml.cs');

test('DashboardHtml.cs の C# verbatim 文字列内に未エスケープのダブルクォートが存在しない', () => {
    const cs = readFileSync(csPath, 'utf8');
    const startIdx = cs.indexOf('return @"');
    assert.ok(startIdx >= 0, 'return @" が見つからない');
    const bodyStart = startIdx + 'return @"'.length;
    const bodyEnd = cs.lastIndexOf('";');
    assert.ok(bodyEnd > bodyStart, '末尾の "; が見つからない');
    const body = cs.slice(bodyStart, bodyEnd);
    for (let i = 0; i < body.length; i++) {
        if (body[i] === '"') {
            assert.equal(body[i + 1], '"', `未エスケープの " を検出 (offset ${i})`);
            i++;
        }
    }
});

test('§51 項目1: ヘッダーが 1 行＋狭幅時ドロップダウン選択へ切り替わる', () => {
    const cs = readFileSync(csPath, 'utf8');
    const headerMatch = cs.match(/<header[\s\S]*?<\/header>/);
    assert.ok(headerMatch, '<header> が見つからない');
    const header = headerMatch[0];
    assert.ok(header.includes("id='filter-select'"), '狭幅用 #filter-select がない');
    assert.ok(!header.includes('overflow-x-auto'), '左方向溢れの原因となる overflow-x-auto がヘッダーに残っている');
    assert.ok(cs.includes("filterSelect.value = filter"), 'setFilter に #filter-select 同期がない');
    assert.ok(cs.includes("['filter-opt-all'"), 'updateStats に <option> 件数同期がない');
});

test('§51 共通・項目2・3・4: 3 モーダルのスクロール枠分離とプレビュー・単品操作の契約を満たす', () => {
    const cs = readFileSync(csPath, 'utf8');
    const html = cs.replace(/""/g, '"');
    for (const id of ['claude-references-grid', 'claude-reference-picker-grid', 'photo-trash-grid']) {
        const line = html.split('\n').find(l => l.includes(`id='${id}'`));
        assert.ok(line, `${id} が見つからない`);
        assert.ok(!line.includes('flex-1'), `${id} 自体に flex-1 が残っている (スクロール潰れバグ再発リスク)`);
        assert.ok(!line.includes('overflow-y-auto'), `${id} 自体に overflow-y-auto が残っている`);
    }
    assert.ok(html.includes("id='claude-reference-preview-modal'"), '参考画像拡大プレビューモーダルがない');
    assert.ok(html.includes("id='photo-trash-preview-modal'"), '写真ゴミ箱拡大プレビューモーダルがない');
    assert.ok(html.includes("id='photo-trash-perm-count'"), '#photo-trash-perm-count がない');
    assert.ok(html.includes('function syncPhotoTrashCardVisual('), 'syncPhotoTrashCardVisual がない');
    assert.ok(html.includes('openClaudeReferencePreview(${idx}, "picker")'), 'ピッカーが案A(クリックで拡大プレビュー)になっていない');
    assert.ok(html.includes("id='viewer-claude-reference-thumb' src='' class='w-8 h-8 rounded object-contain"), '選択済みミニサムネイルが object-contain でない');

    // inline JS の構文検証
    const scriptMatch = html.match(/<script>([\s\S]*?)<\/script>/);
    assert.ok(scriptMatch, 'inline script が見つからない');
    new vm.Script(scriptMatch[1], { filename: 'DashboardHtml.phase6-16.js' });
});

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

function extractJsBlock(src, marker) {
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

test('§52 項目1: #toast が z-[200] 最前面で _hideTimer により連打時の早期消滅を防ぐ', () => {
    const html = readFileSync(csPath, 'utf8').replace(/""/g, '"');
    const toastLine = html.split('\n').find(l => l.includes("id='toast'"));
    assert.ok(toastLine, '#toast が見つからない');
    assert.ok(toastLine.includes('z-[200]'), '#toast に z-[200] が設定されていない');
    assert.ok(!toastLine.includes('z-50'), '#toast に旧 z-50 が残っている');

    const script = html.match(/<script>([\s\S]*?)<\/script>/)[1];
    const showToastSrc = extractJsBlock(script, 'function showToast');
    assert.ok(showToastSrc.includes('_hideTimer'), 'showToast が _hideTimer を制御していない');

    const classSet = new Set(['translate-y-20', 'opacity-0']);
    const toastEl = {
        _hideTimer: null,
        classList: {
            add: (...cls) => cls.forEach(c => classSet.add(c)),
            remove: (...cls) => cls.forEach(c => classSet.delete(c)),
            contains: (c) => classSet.has(c)
        }
    };
    const msgEl = { innerText: '' };
    const timers = new Map();
    let nextTimerId = 1;
    const ctx = vm.createContext({
        document: {
            getElementById: (id) => (id === 'toast' ? toastEl : id === 'toast-msg' ? msgEl : null)
        },
        setTimeout: (fn, ms) => {
            const id = nextTimerId++;
            timers.set(id, { fn, ms });
            return id;
        },
        clearTimeout: (id) => {
            timers.delete(id);
        }
    });
    vm.runInContext(showToastSrc + '; globalThis.__showToast = showToast;', ctx);

    ctx.__showToast('1件目');
    assert.equal(timers.size, 1);
    const firstId = [...timers.keys()][0];
    ctx.__showToast('2件目');
    assert.equal(timers.has(firstId), false, '連続通知時に前回のタイマーが clearTimeout されていない');
    assert.equal(timers.size, 1);
    assert.equal(msgEl.innerText, '2件目');
    assert.ok(classSet.has('opacity-100'));

    const remaining = [...timers.values()][0];
    remaining.fn();
    assert.ok(classSet.has('opacity-0'));
    assert.ok(classSet.has('translate-y-20'));
});

test('§52 項目2: 全 15 モーダル背景が bindSafeBackdropClose 経由となりドラッグアウトで閉じない', () => {
    const html = readFileSync(csPath, 'utf8').replace(/""/g, '"');
    const modalIds = [
        'batch-edit-modal',
        'merge-modal',
        'photo-viewer-modal',
        'photo-lightbox-modal',
        'claude-references-modal',
        'claude-reference-picker-modal',
        'claude-reference-preview-modal',
        'photo-trash-modal',
        'photo-trash-preview-modal',
        'claude-jobs-modal',
        'cleanup-modal',
        'add-modal',
        'edit-modal',
        'settings-modal',
        'template-editor-modal'
    ];
    for (const id of modalIds) {
        const line = html.split('\n').find(l => l.includes(`id='${id}'`));
        assert.ok(line, `${id} が見つからない`);
        assert.ok(!/onclick=['"]close/.test(line), `${id} 背景に inline onclick='close...()' が残っている`);
    }

    const script = html.match(/<script>([\s\S]*?)<\/script>/)[1];
    const bindSrc = extractJsBlock(script, 'function bindSafeBackdropClose');
    const initSrc = extractJsBlock(script, 'function initSafeModalBackdrops');
    for (const id of modalIds) {
        assert.ok(initSrc.includes(`'${id}'`), `initSafeModalBackdrops に ${id} が登録されていない`);
    }

    const els = new Map();
    const makeEl = (id) => {
        const listeners = {};
        return {
            id,
            addEventListener: (type, fn) => {
                (listeners[type] = listeners[type] || []).push(fn);
            },
            dispatch: (type, target) => {
                for (const fn of (listeners[type] || [])) fn({ target });
            }
        };
    };
    const stubNames = [
        'closeBatchEditModal', 'closeMergeModal', 'closePhotoViewer', 'closePhotoLightbox',
        'closeClaudeReferencesModal', 'closeClaudeReferencePicker', 'closeClaudeReferencePreview',
        'closePhotoTrashModal', 'closePhotoTrashPreview', 'closeClaudeJobsModal',
        'closeCleanupModal', 'closeAddModal', 'closeEditModal', 'closeSettingsModal',
        'closeTemplateEditorModal'
    ];
    const sandbox = {
        document: {
            getElementById: (id) => {
                if (!els.has(id)) els.set(id, makeEl(id));
                return els.get(id);
            }
        },
        closedCount: {}
    };
    for (const fnName of stubNames) {
        sandbox.closedCount[fnName] = 0;
        sandbox[fnName] = () => { sandbox.closedCount[fnName]++; };
    }
    const ctx = vm.createContext(sandbox);
    vm.runInContext(bindSrc + '\n' + initSrc + '; initSafeModalBackdrops();', ctx);

    const editModal = els.get('edit-modal');
    assert.ok(editModal, 'edit-modal が登録されていない');
    const innerInput = { id: 'edit-title-input' };

    // 入力欄で mousedown -> 背景外へドラッグアウトして click が背景に発火 -> 閉じないこと
    editModal.dispatch('pointerdown', innerInput);
    editModal.dispatch('mousedown', innerInput);
    editModal.dispatch('click', editModal);
    assert.equal(ctx.closedCount.closeEditModal, 0, 'モーダル内からのドラッグアウトで閉じてしまった');

    // 背景を直接押して離した場合のみ閉じること
    editModal.dispatch('pointerdown', editModal);
    editModal.dispatch('mousedown', editModal);
    editModal.dispatch('click', editModal);
    assert.equal(ctx.closedCount.closeEditModal, 1, '背景直接クリックでモーダルが閉じなかった');
});

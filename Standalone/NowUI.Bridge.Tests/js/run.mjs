// W2/W3 - the JavaScript half of the acceptance, run under node with no browser and no wasm.
//
// Docs/Standalone/M3-Spec.md section 9 (W2, W3). The transport, the header, the string tables and the growth
// protocol are W2's; the trie, the rids and the three checks of section 3.6 are W3's. Both halves of the bridge
// are pure data structures on this side, which is the whole reason they can be checked this way - a browser is
// needed to prove that the word "hello" reaches the canvas, and for nothing else.
//
// Run it directly (`node run.mjs`) or through `dotnet test` - Standalone/NowUI.Bridge.Tests/JsChecks.cs shells out
// to exactly this file, so one command covers both halves.
//
// `node run.mjs --surface-hash` prints the surface hash and nothing else, which is how the C# side asserts that
// abi.js and Abi.cs agree (gate G3) rather than assuming it.

import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const nowui = resolve(here, '../../Web/NowUI.Web/wwwroot/nowui');

const abi = await import(`file://${nowui}/abi.js`);
const { Recorder } = await import(`file://${nowui}/recorder.js`);
const { makeSurface } = await import(`file://${nowui}/surface.stub.js`);
const bridge = await import(`file://${nowui}/bridge.js`);
const results = await import(`file://${nowui}/results.js`);

if (process.argv.includes('--surface-hash')) {
    process.stdout.write(String(abi.SURFACE_HASH));
    process.exit(0);
}

// `--emit-frame <path> [hello|w3]` records one frame through the real recorder and writes it to disk, so the
// managed tests replay bytes JavaScript produced rather than bytes a second C# writer produced. The format is the
// smallest thing both sides can read without a library: int32 usedSlots, int32 textBytes, the ops, the text.
if (process.argv.includes('--emit-frame')) {
    const { writeFileSync } = await import('node:fs');
    const { Recorder: Rec } = await import(`file://${nowui}/recorder.js`);
    const { makeSurface: mk } = await import(`file://${nowui}/surface.stub.js`);

    const outPath = process.argv[process.argv.indexOf('--emit-frame') + 1];
    const mode = process.argv[process.argv.indexOf('--emit-frame') + 2] || 'hello';

    // exactLayout is a frame FLAG, so the one-pass variant is the same draw recorded by a recorder that was told
    // not to ask for the measure cycle. That is what makes W4's "both branches produce the same draw list" a
    // comparison of two REAL frames rather than of one frame replayed under two host settings.
    const W = new Rec({ exactLayout: mode !== 'w4-onepass' });
    const ui = mk(W);

    const bodies = {
        hello: u => u.text('hello'),

        w3: u => {
            u.text('W3');
            u.column({}, () => u.button('shared', { label: 'A' }));
            u.column({}, () => u.button('shared', { label: 'B' }));
            u.column({ key: 'roster' }, () => {
                u.list('team', ['ada', 'grace'], n => n, n => {
                    u.row({}, () => { u.text(n); u.button('remove', { label: 'Remove' }); });
                });
            });
        },

        // W4: all six opcodes in one frame - TEXT, COLUMN, ROW, LIST_ITEM, BUTTON, TEXT_FIELD.
        w4: u => {
            u.text('W4');
            u.column({ key: 'form' }, () => {
                u.row({}, () => {
                    u.button('add', { label: 'Add' });
                    u.textField('name', 'ab', { placeholder: 'Full name' });
                });
            });
            u.list('team', ['ada'], n => n, n => u.text(n));
        },

        // W5: a button FIRST, so its rect starts at the root area's content origin and a test can click it
        // without guessing, then the value control the reconciliation rule is written for.
        w5: u => {
            u.button('add', { label: 'Add' });
            u.textField('name', 'ab', { placeholder: 'Full name' });
        },

        // W4's fourth acceptance. The throw happens with one scope open and one op already emitted; the
        // recorder's finally closes the scope, so what crosses is a balanced PREFIX with the faulted flag set.
        throw: u => {
            u.text('before the throw');
            u.column({ key: 'open' }, () => {
                u.text('inside the scope');
                throw new Error('a deliberate author error, thrown mid-draw');
            });
        },
    };

    bodies['w4-onepass'] = bodies.w4;

    const body = bodies[mode] || bodies.hello;

    W.reset(1);
    try {
        body(ui);
    } catch (e) {
        // Exactly what bridge.js's record() does (section 4.4): mark the frame faulted and keep what was
        // recorded before the throw. Without this the emitter would just die and the managed side would have
        // nothing to replay.
        W.fault(e);
    } finally {
        W.closeAll();
    }
    W.flush();

    const header = Buffer.alloc(8);
    header.writeInt32LE(W.used, 0);
    header.writeInt32LE(W.textUsed, 4);
    const ops = Buffer.from(W.out.buffer, W.out.byteOffset, W.used * 4);
    const text = Buffer.from(W.u8.buffer, W.u8.byteOffset, W.textUsed);
    writeFileSync(outPath, Buffer.concat([header, ops, text]));
    process.exit(0);
}

// `--read-results <path>` decodes a result table that a REAL replay wrote and drives the surface with it, then
// prints what an author would have seen as JSON.
//
// This is the one check that puts both halves of section 6 in the same sentence. Everything else about the table
// is asserted twice - Standalone/NowUI.Bridge.Tests/ResultTests.cs asserts what C# wrote, and the checks in this
// file assert what JavaScript reads - and neither of those would notice if the two disagreed about the layout.
// Here the bytes come from BridgeResults and the reading comes from results.js.
//
// The file format is the smallest thing both sides read without a library, and matches --emit-frame's:
// int32 slotCount, int32 textBytes, the slots, the text.
if (process.argv.includes('--read-results')) {
    const { readFileSync } = await import('node:fs');

    const path = process.argv[process.argv.indexOf('--read-results') + 1];
    const buf = readFileSync(path);

    const slotCount = buf.readInt32LE(0);
    const textBytes = buf.readInt32LE(4);

    const slots = new Int32Array(slotCount);
    for (let i = 0; i < slotCount; i++) slots[i] = buf.readInt32LE(8 + i * 4);
    const text = new Uint8Array(buf.subarray(8 + slotCount * 4, 8 + slotCount * 4 + textBytes));

    const W = new Recorder();
    const ui = makeSurface(W);

    let name = 'ab';
    let clicked = false;

    // The author's loop, exactly as js/run.mjs's `w5` mode records it - same keys, same order, so the trie hands
    // out the same rids the emitting recorder did and the table's rids mean the same controls here.
    function body() {
        clicked = ui.button('add', { label: 'Add' });
        name = ui.textField('name', name, { placeholder: 'Full name' });
    }

    // Frame 1 with no table: this is what seeds `lastSent`, without which the author's own value would win on
    // frame 2 and nothing would round-trip.
    W.results.load(null, new Uint8Array(0), 1);
    W.reset(1);
    try { body(); } finally { W.closeAll(); }
    W.flush();

    // Frame 2 with the managed table.
    W.results.load(slots, text, 2);
    W.reset(2);
    try { body(); } finally { W.closeAll(); }
    W.flush();

    const fieldRid = W.trie.nodes.filter(n => n && n.label === 'name')[0].rid;
    const emitted = [];
    {
        const out = W.out;
        const internCount = out[abi.HDR_INTERN_COUNT];
        for (let i = out[abi.HDR_OP_START]; i < out[abi.HDR_OP_END];) {
            const opcode = out[i] & 0xffff;
            const nslots = (out[i] >> 16) & 0xffff;
            if (opcode === abi.OPS.TEXT_FIELD.opcode) {
                const slot = out[i + 3];
                if (slot >= 0) {
                    for (const [t, h] of W.interned) if (h === slot) emitted.push(t);
                } else {
                    const base = abi.HDR_SLOTS + internCount * 3 + (~slot) * 2;
                    emitted.push(new TextDecoder().decode(W.u8.subarray(out[base], out[base] + out[base + 1])));
                }
            }
            i += 1 + nslots;
        }
    }

    process.stdout.write(JSON.stringify({
        records: W.results.records,
        clicked,
        name,
        emitted: emitted[0] === undefined ? null : emitted[0],
        rect: W.results.rectOf(fieldRid),
    }));
    process.exit(0);
}

// ------------------------------------------------------------------------------------------------ harness

let passed = 0;
const failures = [];

function test(name, body) {
    try {
        body();
        passed++;
        console.log('  PASS  ' + name);
    } catch (e) {
        failures.push({ name, error: e });
        console.log('  FAIL  ' + name + '\n        ' + String(e && e.stack ? e.stack.split('\n').slice(0, 4).join('\n        ') : e));
    }
}

function assert(condition, message) {
    if (!condition) throw new Error(message || 'assertion failed');
}

function assertEqual(actual, expected, message) {
    if (actual !== expected)
        throw new Error((message || 'values differ') + ': expected ' + JSON.stringify(expected) + ', got ' + JSON.stringify(actual));
}

function assertThrows(body, contains) {
    let threw = null;
    try { body(); } catch (e) { threw = e; }
    if (threw === null) throw new Error('expected a throw' + (contains ? ' containing ' + JSON.stringify(contains) : ''));
    if (contains && String(threw.message).indexOf(contains) < 0)
        throw new Error('the throw did not contain ' + JSON.stringify(contains) + '. It was:\n' + threw.message);
    return threw;
}

/// Records one frame with the recorder and the stub surface directly, so an author error propagates instead of
/// being folded into the fault path the way bridge.js's record() folds it.
function draw(W, ui, frame, body) {
    W.reset(frame);
    try {
        body(ui);
    } finally {
        W.closeAll();
    }
    return W.flush();
}

function newRecorder(options) {
    const W = new Recorder(options);
    return { W, ui: makeSurface(W) };
}

/// Walks a flushed frame and returns { header, ops } for assertions.
function decode(W) {
    const out = W.out;
    const header = {
        magic: out[abi.HDR_MAGIC],
        surfaceHash: out[abi.HDR_SURFACE_HASH],
        flags: out[abi.HDR_FRAME_FLAGS],
        internCount: out[abi.HDR_INTERN_COUNT],
        volatileCount: out[abi.HDR_VOLATILE_COUNT],
        textBytes: out[abi.HDR_TEXT_BYTES],
        opStart: out[abi.HDR_OP_START],
        opEnd: out[abi.HDR_OP_END],
    };

    const ops = [];
    for (let i = header.opStart; i < header.opEnd;) {
        const opcode = out[i] & 0xffff;
        const slots = (out[i] >> 16) & 0xffff;
        ops.push({ opcode, slots, args: Array.from(out.subarray(i + 1, i + 1 + slots)) });
        i += 1 + slots;
    }

    return { header, ops };
}

/// The managed side, as far as the JavaScript half can see it: two growable buffers and the two-attempt protocol
/// of section 5.1. BridgeRecorder.Record does exactly this in C#.
class Managed {
    constructor(slots = 1024, textBytes = 4096) {
        this.ops = new Int32Array(slots);
        this.opsText = new Uint8Array(textBytes);
        this.results = new Int32Array(64);
        this.resultsText = new Uint8Array(64);
        this.frame = 0;
        this.grows = 0;
    }

    run() {
        for (let attempt = 0; attempt < 2; attempt++) {
            const returned = bridge.record(
                this.ops, this.opsText, this.results, this.resultsText, this.results.length, this.frame);

            if (returned !== abi.NEED_MORE) {
                this.used = returned;
                this.frame++;
                return returned;
            }

            this.grows++;
            const needSlots = this.ops[0];
            const needText = this.ops[1];
            this.ops = new Int32Array(Math.max(needSlots, 2));
            this.opsText = new Uint8Array(Math.max(needText, 1));
        }

        throw new Error('record() asked to grow twice for one frame');
    }
}

const utf8 = new TextDecoder();

// ================================================================================================ W2
console.log('\nW2 - buffers, the op stream, the header and the string tables');

test('a hello frame carries the header, one intern declaration and one op', () => {
    const { W, ui } = newRecorder();
    draw(W, ui, 1, u => u.text('hello'));
    const { header, ops } = decode(W);

    assertEqual(header.magic, abi.MAGIC, 'magic');
    assertEqual(header.surfaceHash, abi.SURFACE_HASH, 'surfaceHash');
    assertEqual(header.opStart, abi.HDR_SLOTS + header.internCount * 3 + header.volatileCount * 2, 'opStart');
    assertEqual(header.opEnd, W.used, 'opEnd is the end of the frame');
    assertEqual(ops.length, 1, 'one op');
    assertEqual(ops[0].opcode, abi.OPS.TEXT.opcode, 'the op is TEXT');
    assertEqual(ops[0].slots, 1, 'TEXT carries one slot');
    assertEqual(header.textBytes, 5, '"hello" is five UTF-8 bytes');
    assertEqual(utf8.decode(W.u8.subarray(0, 5)), 'hello', 'the bytes say hello');
});

test('the whole hello frame is 12 slots: 8 of header, 2 of table, 2 of op stream', () => {
    const { W, ui } = newRecorder();
    draw(W, ui, 1, u => u.text('hello'));
    const { header } = decode(W);

    // First sighting of a string takes the volatile path (section 5.4: a str position interns only when the value
    // has been seen before), so the table is one 2-slot volatile pair rather than a 3-slot intern triple.
    assertEqual(header.internCount, 0, 'nothing interned on first sighting');
    assertEqual(header.volatileCount, 1, 'one volatile string');
    assertEqual(W.used, 8 + 2 + 2, 'total slots');
});

test('a string seen twice interns, and its second frame costs one slot and no bytes', () => {
    const { W, ui } = newRecorder();
    draw(W, ui, 1, u => u.text('hello'));
    draw(W, ui, 2, u => u.text('hello'));
    const second = decode(W);

    assertEqual(second.header.internCount, 1, 'interned on the second sighting');
    assertEqual(second.header.volatileCount, 0, 'and no longer volatile');

    draw(W, ui, 3, u => u.text('hello'));
    const third = decode(W);
    assertEqual(third.header.internCount, 0, 'already declared, so nothing new is declared');
    assertEqual(third.header.textBytes, 0, 'and no bytes ride along');
    assertEqual(third.ops[0].args[0], 0, 'the op carries the intern handle');
});

test('keys always intern, on their first sighting', () => {
    const { W, ui } = newRecorder();
    draw(W, ui, 1, u => u.column({ key: 'roster' }, () => {}));
    const { header } = decode(W);
    assertEqual(header.internCount, 1, 'the key interned immediately');
    assertEqual(utf8.decode(W.u8.subarray(0, header.textBytes)), 'roster');
});

test('intern declarations are contiguous from the previous count', () => {
    const { W, ui } = newRecorder();
    draw(W, ui, 1, u => u.column({ key: 'a' }, () => u.column({ key: 'b' }, () => {})));
    assertEqual(W.out[abi.HDR_SLOTS + 0], 0, 'first handle');
    assertEqual(W.out[abi.HDR_SLOTS + 3], 1, 'second handle');

    draw(W, ui, 2, u => u.column({ key: 'a' }, () => u.column({ key: 'c' }, () => {})));
    assertEqual(W.out[abi.HDR_INTERN_COUNT], 1, 'only the new key is declared');
    assertEqual(W.out[abi.HDR_SLOTS + 0], 2, 'and it continues from 2');
});

test('every scope open has a matching OP_SCOPE_CLOSE, and they nest', () => {
    const { W, ui } = newRecorder();
    draw(W, ui, 1, u => {
        u.column({}, () => {
            u.row({}, () => u.text('x'));
            u.row({}, () => u.text('y'));
        });
    });

    const { ops } = decode(W);
    let depth = 0;
    let maxDepth = 0;
    for (const op of ops) {
        if (op.opcode === abi.OP_SCOPE_CLOSE) depth--;
        else if (op.opcode === abi.OPS.COLUMN.opcode || op.opcode === abi.OPS.ROW.opcode) { depth++; maxDepth = Math.max(maxDepth, depth); }
        assert(depth >= 0, 'a close preceded its open');
    }
    assertEqual(depth, 0, 'balanced');
    assertEqual(maxDepth, 2, 'and nested two deep');
});

test('a draw function that throws mid-scope still produces a balanced buffer', () => {
    const { W, ui } = newRecorder();

    let threw = false;
    W.reset(1);
    try {
        ui.column({}, () => {
            ui.row({}, () => {
                ui.text('before');
                throw new Error('author error');
            });
        });
    } catch (e) {
        threw = true;
        W.fault(e);
    } finally {
        W.closeAll();
    }
    W.flush();

    assert(threw, 'the throw propagated to the caller');
    assert((W.out[abi.HDR_FRAME_FLAGS] & abi.FLAG_FAULTED) !== 0, 'the frame is flagged faulted');

    const { ops } = decode(W);
    let depth = 0;
    for (const op of ops) {
        if (op.opcode === abi.OP_SCOPE_CLOSE) depth--;
        else if (op.opcode === abi.OPS.COLUMN.opcode || op.opcode === abi.OPS.ROW.opcode) depth++;
    }
    assertEqual(depth, 0, 'closeAll balanced the prefix');
});

test('record() returns NEED_MORE with the two sizes, and the retry does not re-run the draw', () => {
    let draws = 0;
    bridge.start(u => { draws++; u.text('hello'); });

    const managed = new Managed(4, 2);      // deliberately far too small, but >= 2 slots
    const used = managed.run();

    assertEqual(managed.grows, 1, 'grew exactly once');
    assertEqual(draws, 1, 'the author draw function ran exactly once');
    assert(used > 0 && used !== abi.NEED_MORE, 'the retry succeeded');
    assertEqual(managed.ops[abi.HDR_MAGIC], abi.MAGIC, 'and the frame landed in the grown buffer');
});

test('a second frame at the grown size needs no growth', () => {
    let draws = 0;
    bridge.start(u => { draws++; u.text('hello'); });
    const managed = new Managed(1024, 4096);
    managed.run();
    managed.run();
    assertEqual(managed.grows, 0, 'no growth at a realistic starting size');
    assertEqual(draws, 2, 'one draw per frame');
});

test('the 4M-slot cap fails with the last scope keys in the message', () => {
    const { W, ui } = newRecorder({ maxSlots: 512 });
    const error = assertThrows(() => {
        draw(W, ui, 1, u => {
            u.column({ key: 'outer' }, () => {
                u.column({ key: 'inner' }, () => {
                    for (let i = 0; i < 10000; i++) u.text('x' + i);
                });
            });
        });
    }, 'command slots');
    assert(String(error.message).indexOf('inner') >= 0, 'the message names the open scopes: ' + error.message);
});

// ================================================================================================ W3
console.log('\nW3 - identity: the path trie, rids, and the three checks of section 3.6');

test('(b) two buttons with the same key in DIFFERENT columns coexist', () => {
    const { W, ui } = newRecorder();
    draw(W, ui, 1, u => {
        u.column({}, () => u.button('shared'));
        u.column({}, () => u.button('shared'));
    });

    const { ops } = decode(W);
    const buttons = ops.filter(o => o.opcode === abi.OPS.BUTTON.opcode);
    assertEqual(buttons.length, 2, 'both buttons were emitted');
    assert(buttons[0].args[0] !== buttons[1].args[0], 'they have different rids');
    assertEqual(buttons[0].args[1], buttons[1].args[1], 'and the same segment, because the KEY is the same');

    const paths = W.trie.nodes.filter(n => n && n.label === 'shared').map(n => W.trie.path(n)).sort();
    assertEqual(paths.join(' '), '/#0/shared /#1/shared', 'the two canonical paths');
});

test('(b) two buttons with the same key in the SAME column throw, with the canonical path', () => {
    const { W, ui } = newRecorder();
    const error = assertThrows(() => {
        draw(W, ui, 1, u => {
            u.column({}, () => {
                u.button('shared');
                u.button('shared');
            });
        });
    }, 'duplicate control key');

    assert(String(error.message).indexOf('path:  /#0/shared') >= 0,
        'the message carries the canonical path. It was:\n' + error.message);
    assert(String(error.message).indexOf('first: draw call #') >= 0, 'and both draw call indices');
    assert(String(error.message).indexOf('ui.list(key, items, keyOf, render)') >= 0, 'and the fix');
});

test('(b) two ui.list calls with the same key under one parent collide on the ITEM, per section 3.7.5', () => {
    const { W, ui } = newRecorder();
    const error = assertThrows(() => {
        draw(W, ui, 1, u => {
            u.list('team', ['ada'], n => n, () => {});
            u.list('team', ['ada'], n => n, () => {});
        });
    }, 'duplicate scope key');
    assert(String(error.message).indexOf('/team[ada]') >= 0, 'the message names an item: ' + error.message);
});

test('rids are dense, stable across frames, and assigned once per path', () => {
    const { W, ui } = newRecorder();
    const body = u => {
        u.column({ key: 'roster' }, () => {
            u.list('team', ['ada', 'grace'], n => n, n => u.button('remove'));
        });
    };

    draw(W, ui, 1, body);
    const first = decode(W).ops.map(o => o.args[0]);
    draw(W, ui, 2, body);
    const second = decode(W).ops.map(o => o.args[0]);

    assertEqual(JSON.stringify(second), JSON.stringify(first), 'the same paths get the same rids');

    const rids = W.trie.nodes.filter(n => n).map(n => n.rid).sort((a, b) => a - b);
    assertEqual(rids[0], 0, 'rids start at 0');
    assertEqual(rids[rids.length - 1], rids.length - 1, 'and are dense');
});

test('the anonymous ordinal counts only anonymous scopes, so a keyed sibling never shifts one', () => {
    const { W, ui } = newRecorder();

    draw(W, ui, 1, u => {
        u.column({}, () => {});
        u.column({}, () => {});
    });
    const before = W.trie.nodes.filter(n => n && n.label === '#1')[0].rid;

    draw(W, ui, 2, u => {
        u.column({}, () => {});
        u.column({ key: 'inserted' }, () => {});
        u.column({}, () => {});
    });
    const after = W.trie.nodes.filter(n => n && n.label === '#1')[0].rid;

    assertEqual(after, before, 'inserting a KEYED sibling did not renumber the anonymous ones');
});

test('(c) check 2 fires when a conditional anonymous sibling disappears', () => {
    const reports = [];
    const { W, ui } = newRecorder({ onReport: r => reports.push(r) });

    draw(W, ui, 1, u => {
        u.column({}, () => {});      // the toolbar
        u.column({}, () => {});      // the one that will be renumbered
    });
    assertEqual(reports.length, 0, 'nothing to report on the first frame');

    draw(W, ui, 2, u => {
        u.column({}, () => {});      // the toolbar is gone; this used to be #1
    });

    assertEqual(reports.length, 1, 'check 2 fired exactly once');
    const message = reports[0].message;
    assert(message.indexOf('changed shape') >= 0, 'the message: ' + message);
    assert(message.indexOf('automatic key') >= 0, 'names the automatic key');
    assert(message.indexOf('Previous: [#0, #1]') >= 0, 'and shows the before vector: ' + message);
    assert(message.indexOf('Now:      [#0]') >= 0, 'and the after vector: ' + message);
    assert(message.indexOf('ui.when(cond, body)') >= 0, 'and the fix');

    draw(W, ui, 3, u => { u.column({}, () => {}); });
    assertEqual(reports.length, 1, 'and it reports once per path, not once per frame');
});

test('(c) check 2 stays silent when ui.when holds the slot', () => {
    const reports = [];
    const { W, ui } = newRecorder({ onReport: r => reports.push(r) });

    for (let frame = 1; frame <= 6; frame++) {
        draw(W, ui, frame, u => {
            u.when(frame % 2 === 0, () => u.text('toolbar'));
            u.column({}, () => u.text('body'));
        });
    }

    assertEqual(reports.length, 0, 'ui.when consumed its ordinal on every frame: ' + JSON.stringify(reports.map(r => r.message)));
});

test('(c) check 2 stays silent when a KEYED list grows or shrinks', () => {
    const reports = [];
    const { W, ui } = newRecorder({ onReport: r => reports.push(r) });

    draw(W, ui, 1, u => u.list('team', ['ada', 'grace'], n => n, () => {}));
    draw(W, ui, 2, u => u.list('team', ['ada', 'grace', 'edsger'], n => n, () => {}));
    draw(W, ui, 3, u => u.list('team', ['grace'], n => n, () => {}));

    assertEqual(reports.length, 0, 'every altered slot carried an explicit key: ' + JSON.stringify(reports.map(r => r.message)));
});

test('check 3: keyOf must return a distinct non-empty string', () => {
    const { W, ui } = newRecorder();

    assertThrows(() => draw(W, ui, 1, u => u.list('team', ['a', 'b'], () => 'same', () => {})),
        'duplicate key');
    assertThrows(() => draw(W, ui, 2, u => u.list('team', ['a'], () => undefined, () => {})),
        'keyOf returned undefined');
    assertThrows(() => draw(W, ui, 3, u => u.list('team', ['a'], (_, i) => i, () => {})),
        'keyOf returned 0');
    assertThrows(() => draw(W, ui, 4, u => u.list('team', ['a'], () => '', () => {})),
        'keyOf returned ""');
});

test('a non-string scope key is refused before it can become an identity', () => {
    const { W, ui } = newRecorder();
    assertThrows(() => draw(W, ui, 1, u => u.column({ key: 3 }, () => {})), 'must be a string');
});

test('the two integer namespaces cannot collide: handles are >= 0, ordinals <= -1', () => {
    const { W, ui } = newRecorder();
    draw(W, ui, 1, u => {
        u.column({ key: 'keyed' }, () => {});
        u.column({}, () => {});
        u.column({}, () => {});
    });

    for (const node of W.trie.nodes) {
        if (!node || node.kind === 0) continue;
        if (node.label.charAt(0) === '#') assert(node.seg <= -1, 'anonymous segment ' + node.seg + ' is not <= -1');
        else assert(node.seg >= 0, 'keyed segment ' + node.seg + ' is not >= 0');
    }
});

test('the canonical path of section 3.2 renders exactly as the spec prints it', () => {
    const { W, ui } = newRecorder();
    let removeNode = null;

    draw(W, ui, 1, u => {
        u.column({}, () => {                                     // #0
            u.column({ key: 'roster' }, () => {                   // roster (a scroll in the spec's example)
                u.list('team', ['grace'], n => n, () => {          // team[grace]
                    u.row({}, () => {                              // #0
                        removeNode = W.trie.control('Remove');     // Remove
                    });
                });
            });
        });
    });

    assertEqual(W.trie.path(removeNode), '/#0/roster/team[grace]/#0/Remove');
});

test('a node untouched for 600 frames is evicted and its rid recycled', () => {
    const { W, ui } = newRecorder();
    draw(W, ui, 1, u => u.column({ key: 'transient' }, () => {}));
    const rid = W.trie.nodes.filter(n => n && n.label === 'transient')[0].rid;

    for (let frame = 2; frame <= 700; frame++) draw(W, ui, frame, () => {});

    assertEqual(W.trie.nodes[rid], null, 'the node was dropped');
    assert(W.trie.free.indexOf(rid) >= 0, 'and its rid went on the free list');
});

// ================================================================================================ W4/W5
console.log('\nW4/W5 - the sixth op, the result table, the latch and the reconciliation rule');

/// Builds a result table in the exact shape Standalone/NowUI.Bridge/Results.cs writes, so these checks read the
/// wire format rather than a JavaScript-shaped stand-in. That the C# writer really produces this shape is not
/// assumed here either: `node run.mjs --read-results <file>` decodes a table a REAL replay wrote, and
/// ResultTests.TheManagedTableIsReadBackByResultsJs is what calls it.
function buildTable(records) {
    const slots = [0, 0];
    const bytes = [];
    const encoder = new TextEncoder();

    for (const r of records) {
        slots.push(r.rid);
        slots.push(((r.flags | results.F_PRESENT) << results.FLAGS_SHIFT) | (r.kind & results.KIND_MASK));

        if (r.kind === results.KIND_STR) {
            const utf = encoder.encode(r.value === undefined ? '' : r.value);
            slots.push(bytes.length);
            slots.push(utf.length);
            for (const b of utf) bytes.push(b);
        } else if (r.kind !== results.KIND_NONE) {
            slots.push(r.value | 0);
        }

        if (r.rect) { for (let i = 0; i < 4; i++) slots.push(0); }
    }

    slots[0] = records.length;
    slots[1] = bytes.length;

    const table = { slots: Int32Array.from(slots), text: Uint8Array.from(bytes) };

    // The rect slots are f32 and are written through a Float32Array over the same buffer, exactly as Results.cs
    // writes them and as the loader reads them back.
    let cursor = results.HDR_SLOTS;
    const f32 = new Float32Array(table.slots.buffer);
    for (const r of records) {
        cursor += 2;
        if (r.kind === results.KIND_STR) cursor += 2;
        else if (r.kind !== results.KIND_NONE) cursor += 1;
        if (r.rect) { for (let i = 0; i < 4; i++) f32[cursor + i] = r.rect[i]; cursor += 4; }
    }

    return table;
}

function withTable(W, records, frame) {
    const t = buildTable(records);
    W.results.load(t.slots, t.text, frame);
}

function noTable(W, frame) {
    W.results.load(Int32Array.from([0, 0]), new Uint8Array(0), frame);
}

function ridOf(W, label) {
    const node = W.trie.nodes.filter(n => n && n.label === label)[0];
    assert(node !== undefined, 'no trie node labelled ' + JSON.stringify(label));
    return node.rid;
}

/// Reads a `str` argument back out of a flushed frame, resolving an intern handle or a volatile index the same
/// way BridgeRecorder.Text does on the managed side.
function readString(W, slot) {
    const out = W.out;
    const internCount = out[abi.HDR_INTERN_COUNT];

    if (slot >= 0) {
        for (const [text, handle] of W.interned) if (handle === slot) return text;
        return null;
    }

    const base = abi.HDR_SLOTS + internCount * 3 + (~slot) * 2;
    return utf8.decode(W.u8.subarray(out[base], out[base] + out[base + 1]));
}

test('the sixth op carries rid, segment, the resolved value and the placeholder', () => {
    const { W, ui } = newRecorder();
    W.results.load(null, new Uint8Array(0), 1);
    draw(W, ui, 1, u => u.textField('name', 'ab', { placeholder: 'Full name' }));

    const op = decode(W).ops.filter(o => o.opcode === abi.OPS.TEXT_FIELD.opcode)[0];
    assertEqual(op.slots, 4, 'four argument slots');
    assertEqual(readString(W, op.args[2]), 'ab', 'the value');
    assertEqual(readString(W, op.args[3]), 'Full name', 'the placeholder');
});

test('a click fires exactly once per click (section 6.3)', () => {
    const { W, ui } = newRecorder();

    // Frame 1: no table at all - the first frame of a session. Every read is the identity default.
    W.results.load(null, new Uint8Array(0), 1);
    let fired = 0;
    draw(W, ui, 1, u => { if (u.button('add')) fired++; });
    assertEqual(fired, 0, 'a button nobody has clicked does not fire');

    const rid = ridOf(W, 'add');

    // Frame 2: the replay reported a click on that rid. It fires, once, and reading it twice does not double it.
    withTable(W, [{ rid, flags: results.F_CLICKED, kind: results.KIND_NONE }], 2);
    let reads = 0;
    draw(W, ui, 2, u => { if (u.button('add')) reads++; });
    assertEqual(reads, 1, 'and one that was clicked fires exactly once');

    // Frame 3: the button was drawn and NOT clicked, which is a record with the flag clear. This is the case that
    // separates "fires once per click" from "fires once and then never again".
    withTable(W, [{ rid, flags: 0, kind: results.KIND_NONE }], 3);
    let again = 0;
    draw(W, ui, 3, u => { if (u.button('add')) again++; });
    assertEqual(again, 0, 'the click is not re-delivered on the next frame');

    // Frame 4: a second click. It fires again, so the latch is per-record and not a one-per-session fuse.
    withTable(W, [{ rid, flags: results.F_CLICKED, kind: results.KIND_NONE }], 4);
    let second = 0;
    draw(W, ui, 4, u => { if (u.button('add')) second++; });
    assertEqual(second, 1, 'a second click fires again');
});

test('reading the same button twice in one frame fires once', () => {
    const { W, ui } = newRecorder();
    W.results.load(null, new Uint8Array(0), 1);
    draw(W, ui, 1, u => u.button('add'));
    const rid = ridOf(W, 'add');

    withTable(W, [{ rid, flags: results.F_CLICKED, kind: results.KIND_NONE }], 2);

    // Not through the surface - drawing the same key twice is check 1's duplicate-path throw. Straight at the
    // loader, which is where the latch lives.
    assertEqual(W.results.event(rid, results.F_CLICKED), true, 'the first read has it');
    assertEqual(W.results.event(rid, results.F_CLICKED), false, 'and the second does not');
});

test('a control that stopped being drawn cannot deliver its click late', () => {
    const { W, ui } = newRecorder();
    W.results.load(null, new Uint8Array(0), 1);
    draw(W, ui, 1, u => u.button('add'));
    const rid = ridOf(W, 'add');

    // The click was recorded but never read - the author's `if` was inside a branch that did not run.
    withTable(W, [{ rid, flags: results.F_CLICKED, kind: results.KIND_NONE }], 2);

    // The button then stopped being drawn, so the replay wrote no record for it. The stamp is stale and the read
    // is served the identity default rather than a click from two frames ago.
    noTable(W, 3);

    let fired = 0;
    draw(W, ui, 3, u => { if (u.button('add')) fired++; });
    assertEqual(fired, 0, 'an event can never be delivered late');
});

test('a typed character round-trips with no revert (section 6.4)', () => {
    const { W, ui } = newRecorder();
    let name = 'ab';

    // Frame 1 seeds the control with the author's value: there is no lastSent yet.
    W.results.load(null, new Uint8Array(0), 1);
    draw(W, ui, 1, u => { name = u.textField('name', name); });
    assertEqual(name, 'ab', 'the first frame returns what the author passed');

    const rid = ridOf(W, 'name');

    // Frame 2. The replay produced "abc" from "ab" and set `changed`. The author's variable is still "ab" - it
    // only receives "abc" from THIS call's return - so the incoming value equals lastSent, the author is echoing,
    // and the freshest known value wins.
    withTable(W, [{ rid, flags: results.F_CHANGED, kind: results.KIND_STR, value: 'abc' }], 2);
    draw(W, ui, 2, u => { name = u.textField('name', name); });
    assertEqual(name, 'abc', 'the keystroke reached the author');

    // AND the op carries "abc", not "ab". This is the half Design B got wrong: emitting the caller's value and
    // then consulting the result sends "ab" back to a field whose edit state is clamped to whatever it is handed
    // (NowTextField.cs:1398), which reverts the character one frame after it was typed.
    const ops = decode(W).ops.filter(o => o.opcode === abi.OPS.TEXT_FIELD.opcode);
    assertEqual(ops.length, 1, 'one text field op');
    assertEqual(readString(W, ops[0].args[2]), 'abc', 'and it carries the RESOLVED value');
});

test('a programmatic write always lands, even against a fresher result', () => {
    const { W, ui } = newRecorder();
    let name = 'ab';

    W.results.load(null, new Uint8Array(0), 1);
    draw(W, ui, 1, u => { name = u.textField('name', name); });
    const rid = ridOf(W, 'name');

    withTable(W, [{ rid, flags: results.F_CHANGED, kind: results.KIND_STR, value: 'abc' }], 2);
    draw(W, ui, 2, u => { name = u.textField('name', name); });
    assertEqual(name, 'abc');

    // The Clear handler. The table still says "abc" and will keep saying it; the author wrote "", and the author
    // wins - which is the property the resolve-then-emit version does not have on its own.
    withTable(W, [{ rid, flags: 0, kind: results.KIND_STR, value: 'abc' }], 3);
    name = '';
    draw(W, ui, 3, u => { name = u.textField('name', name); });
    assertEqual(name, '', 'the programmatic write survived');

    const ops = decode(W).ops.filter(o => o.opcode === abi.OPS.TEXT_FIELD.opcode);
    assertEqual(readString(W, ops[0].args[2]), '', 'and it is what crossed the boundary');
});

test('an echo of an unchanged value is bit-identical, frame after frame', () => {
    const { W, ui } = newRecorder();
    let name = 'steady';

    W.results.load(null, new Uint8Array(0), 1);
    draw(W, ui, 1, u => { name = u.textField('name', name); });
    const rid = ridOf(W, 'name');

    for (let frame = 2; frame < 8; frame++) {
        withTable(W, [{ rid, flags: 0, kind: results.KIND_STR, value: 'steady' }], frame);
        draw(W, ui, frame, u => { name = u.textField('name', name); });
        assertEqual(name, 'steady', 'no drift on frame ' + frame);
    }
});

test('onSubmit fires from the submitted flag, once', () => {
    const { W, ui } = newRecorder();
    let submits = 0;
    let name = 'ab';

    W.results.load(null, new Uint8Array(0), 1);
    draw(W, ui, 1, u => { name = u.textField('name', name, { onSubmit: () => submits++ }); });
    const rid = ridOf(W, 'name');

    withTable(W, [{ rid, flags: results.F_SUBMITTED, kind: results.KIND_STR, value: 'ab' }], 2);
    draw(W, ui, 2, u => { name = u.textField('name', name, { onSubmit: () => submits++ }); });
    assertEqual(submits, 1, 'the handler ran');

    withTable(W, [{ rid, flags: 0, kind: results.KIND_STR, value: 'ab' }], 3);
    draw(W, ui, 3, u => { name = u.textField('name', name, { onSubmit: () => submits++ }); });
    assertEqual(submits, 1, 'and not again on the next frame');
});

test('a record carries its rect when the control had one', () => {
    const { W, ui } = newRecorder();
    W.results.load(null, new Uint8Array(0), 1);
    draw(W, ui, 1, u => u.textField('name', 'ab'));
    const rid = ridOf(W, 'name');

    withTable(W, [{
        rid, flags: results.F_HAS_RECT, kind: results.KIND_STR, value: 'ab', rect: [16, 68, 200, 36],
    }], 2);

    const rect = W.results.rectOf(rid);
    assert(rect !== null, 'a rect was decoded');
    assertEqual(rect[0], 16); assertEqual(rect[1], 68); assertEqual(rect[2], 200); assertEqual(rect[3], 36);
});

test('two records in one table are walked correctly despite different value widths', () => {
    // The variable-length record walk of section 6.1, which is the part of the layout most likely to be wrong:
    // an action record is 2 slots, a string record is 4, and a string record with a rect is 8.
    const { W, ui } = newRecorder();
    W.results.load(null, new Uint8Array(0), 1);
    draw(W, ui, 1, u => { u.button('add'); u.textField('name', 'ab'); });

    const button = ridOf(W, 'add');
    const field = ridOf(W, 'name');

    withTable(W, [
        { rid: button, flags: results.F_CLICKED, kind: results.KIND_NONE },
        { rid: field, flags: results.F_CHANGED | results.F_HAS_RECT, kind: results.KIND_STR, value: 'abc', rect: [1, 2, 3, 4] },
    ], 2);

    assertEqual(W.results.records, 2, 'both records decoded');
    assertEqual(W.results.event(button, results.F_CLICKED), true, 'the action record');
    assertEqual(W.results.state(field, results.F_CHANGED), true, 'the value record survived the walk');
    assertEqual(W.results.rectOf(field)[2], 3, 'and so did its rect');
});

test('the missed-read check reports a control the replay never reaches, and only that', () => {
    const reports = [];
    const W = new Recorder({ debug: true, onResultReport: m => reports.push(m) });
    const ui = makeSurface(W);

    // Drawn every frame, never in a table. After three frames it is reported once and never again.
    for (let frame = 1; frame <= 6; frame++) {
        noTable(W, frame);
        draw(W, ui, frame, u => u.button('ghost'));
    }

    assertEqual(reports.length, 1, 'reported once, not once per frame');
    assert(reports[0].indexOf('never produced a result') >= 0, 'and it says what it means: ' + reports[0]);
});

test('a control that IS being served is never reported as missed', () => {
    const reports = [];
    const W = new Recorder({ debug: true, onResultReport: m => reports.push(m) });
    const ui = makeSurface(W);

    W.results.load(null, new Uint8Array(0), 1);
    draw(W, ui, 1, u => u.button('real'));
    const rid = ridOf(W, 'real');

    for (let frame = 2; frame <= 8; frame++) {
        withTable(W, [{ rid, flags: 0, kind: results.KIND_NONE }], frame);
        draw(W, ui, frame, u => u.button('real'));
    }

    assertEqual(reports.length, 0, 'nothing reported: ' + reports.join('\n'));
});

test('a text field does not leak one intern-table entry per keystroke (section 5.4)', () => {
    // The regression this exists for was measured in a browser, not reasoned about: typing "Ada Lovelace" into
    // the w5 page took the session's intern count from 12 to 34, because a value that is volatile on the frame it
    // is typed is echoed back by the author on the NEXT frame and the seen-before heuristic interns it then.
    const { W, ui } = newRecorder();
    let name = '';

    // Two warm-up frames, so the baseline is taken after the CONSTANTS have settled: the placeholder is a `str`
    // position and interns on its second sighting, which is the heuristic working as intended.
    W.results.load(null, new Uint8Array(0), 1);
    draw(W, ui, 1, u => { name = u.textField('name', name, { placeholder: 'Full name' }); });
    const rid = ridOf(W, 'name');
    withTable(W, [{ rid, flags: 0, kind: results.KIND_STR, value: '' }], 2);
    draw(W, ui, 2, u => { name = u.textField('name', name, { placeholder: 'Full name' }); });

    const afterWarmUp = W.internCount;
    assert(W.interned.has('Full name'), 'the placeholder interned during the warm-up, as a constant should');

    // Twenty frames of a user typing, with the author echoing the value back each frame, which is what the
    // reconciliation rule makes them do.
    const typed = 'Ada Lovelace, 1815';
    for (let i = 1; i <= typed.length; i++) {
        const frame = i + 2;
        withTable(W, [{ rid, flags: results.F_CHANGED, kind: results.KIND_STR, value: typed.slice(0, i) }], frame);
        draw(W, ui, frame, u => { name = u.textField('name', name, { placeholder: 'Full name' }); });
        // And a second frame at the same value - the echo, which is where the leak used to happen.
        withTable(W, [{ rid, flags: 0, kind: results.KIND_STR, value: typed.slice(0, i) }], frame + 100);
        draw(W, ui, frame + 100, u => { name = u.textField('name', name, { placeholder: 'Full name' }); });
    }

    assertEqual(name, typed, 'the value round-tripped the whole way');
    assertEqual(W.internCount, afterWarmUp,
        'typing ' + typed.length + ' characters interned ' + (W.internCount - afterWarmUp) +
        ' new strings. A text field\'s value must never intern: the table has no eviction (section 8.14) and its ' +
        'ceiling is 65,536, so a few minutes of typing would reach it.');
});

test('the exactLayout flag rides in the header and is what the frame asked for', () => {
    // W4's branch is chosen from this bit, so the recorder has to be able to clear it.
    const off = new Recorder({ exactLayout: false });
    draw(off, makeSurface(off), 1, u => u.text('x'));
    assertEqual((off.out[abi.HDR_FRAME_FLAGS] & abi.FLAG_EXACT_LAYOUT) !== 0, false, 'off');

    const on = new Recorder({});
    draw(on, makeSurface(on), 1, u => u.text('x'));
    assertEqual((on.out[abi.HDR_FRAME_FLAGS] & abi.FLAG_EXACT_LAYOUT) !== 0, true, 'on by default');
});

test('a throw mid-draw leaves a balanced prefix with the faulted flag set', () => {
    const { W, ui } = newRecorder();
    W.results.load(null, new Uint8Array(0), 1);

    W.reset(1);
    try {
        ui.text('before');
        ui.column({ key: 'open' }, () => {
            ui.text('inside');
            throw new Error('deliberate');
        });
    } catch (e) {
        W.fault(e);
    } finally {
        W.closeAll();
    }
    W.flush();

    const { header, ops } = decode(W);
    assertEqual((header.flags & abi.FLAG_FAULTED) !== 0, true, 'the faulted flag is set');

    // TEXT, COLUMN, TEXT, OP_SCOPE_CLOSE - the scope the author left open was closed by the finally, so the
    // stream nests and the managed validator's rule 5 accepts it.
    assertEqual(ops.length, 4, 'four ops');
    assertEqual(ops[3].opcode, abi.OP_SCOPE_CLOSE, 'and the last is the close the recorder emitted');

    let depth = 0;
    for (const op of ops) {
        if (op.opcode === abi.OP_SCOPE_CLOSE) depth--;
        else if (op.opcode === abi.OPS.COLUMN.opcode || op.opcode === abi.OPS.ROW.opcode ||
                 op.opcode === abi.OPS.LIST_ITEM.opcode) depth++;
        assert(depth >= 0, 'never unbalanced');
    }
    assertEqual(depth, 0, 'and balanced at the end');
});

// ================================================================================================ done

console.log('\n' + passed + ' passed, ' + failures.length + ' failed.');
if (failures.length > 0) process.exit(1);

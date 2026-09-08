// W2/W3/W4/W5 - the sliver of the tier-1 surface these four units need. Docs/Standalone/M3-Spec.md sections 2,
// 3.5, 4.1, 6.3 and 6.4.
//
// This is NOT the 41-function surface. W6 hand-writes those into nowui.js and W8 regenerates them from
// Standalone/Surface/surface.json; this file holds the six functions W2's op stream, W3's identity model and W5's
// result table have to be exercised by, and is deleted when nowui.js lands. It is here rather than inside
// bridge.js so that the node checks in Standalone/NowUI.Bridge.Tests/js can drive the recorder, the trie and the
// result table with no wasm and no browser.
//
// Every scope is a function taking a body callback (section 4.1). There is no begin/end pair an author can hold or
// forget, and the four lines that make that true are the same four for all of them:
//
//     open; try { body(); } finally { close(); }
//
// Because the finally runs on every path out of body, the emitted stream is balanced by construction - which is
// what section 4.4 leans on when the author's code throws mid-frame.

import { OPS } from './abi.js';
import { NowUIAuthorError } from './trie.js';
import { F_CLICKED, F_SUBMITTED } from './results.js';

export function makeSurface(W) {
    const trie = W.trie;
    const R = W.results;

    /// A scope. `pushNode` puts the trie node in place and returns it; `writeArgs` writes the op's arguments.
    function scope(record, pushNode, writeArgs, body) {
        const node = pushNode();
        W.beginScope(record, node, () => writeArgs(node));
        try {
            if (typeof body === 'function') body();
        } finally {
            W.endScope();
        }
    }

    function segArgs(node) {
        W.i32(node.rid);
        W.i32(node.seg);
    }

    function container(record, opts, body) {
        // A keyed container is immune to ordinal shifts; an unkeyed one takes the next anonymous ordinal in its
        // parent (section 3.5).
        const key = opts && typeof opts === 'object' ? opts.key : undefined;

        if (key === undefined || key === null) {
            scope(record, () => trie.pushAnon(), segArgs, body);
        } else {
            scope(record, () => trie.pushScope(key), segArgs, body);
        }
    }

    const ui = {
        /// Non-interactive text. No key, no identity, no result - section 2.3's "drawings, none keyed".
        text(value) {
            const record = OPS.TEXT;
            W.op(record);
            W.i32(W.str(value === undefined || value === null ? '' : String(value)));
        },

        column(opts, body) {
            if (typeof opts === 'function') { body = opts; opts = undefined; }
            container(OPS.COLUMN, opts, body);
        },

        row(opts, body) {
            if (typeof opts === 'function') { body = opts; opts = undefined; }
            container(OPS.ROW, opts, body);
        },

        /// Section 3.5 escape 1. It ALWAYS consumes its anonymous ordinal and only runs the body when `cond` is
        /// true, so toggling it shifts nothing. This is why it exists in a 41-function API instead of "just write
        /// an if": an `if` around a control is free, an `if` around a scope is not.
        when(cond, body) {
            const node = trie.pushAnon();
            W.beginScope(OPS.COLUMN, node, () => segArgs(node));
            try {
                if (cond && typeof body === 'function') body();
            } finally {
                W.endScope();
            }
        },

        /// Section 3.6 check 3 lives here: keyOf returning a duplicate, undefined, or a non-string throws
        /// immediately, with the offending index and value. NowControls.KeyedItemIn would itself throw on an empty
        /// key (NowControls.cs:262-266), but by then the message has lost the JavaScript context.
        list(key, items, keyOf, render) {
            if (typeof keyOf !== 'function')
                throw new NowUIAuthorError('NowUI: ui.list(key, items, keyOf, render) requires keyOf to be a function.');

            const seen = new Set();
            const array = items || [];

            for (let i = 0; i < array.length; i++) {
                const itemKey = keyOf(array[i], i);

                if (typeof itemKey !== 'string' || itemKey.length === 0) {
                    throw new NowUIAuthorError(
                        'NowUI: ui.list(\'' + key + '\', ...) - keyOf returned ' +
                        (itemKey === undefined ? 'undefined' : JSON.stringify(itemKey)) +
                        ' for index ' + i + '.\n' +
                        'keyOf must return a non-empty string that identifies the item across frames. Returning\n' +
                        'the index makes state follow position, so a reorder moves carets and scroll offsets.');
                }

                if (seen.has(itemKey)) {
                    throw new NowUIAuthorError(
                        'NowUI: ui.list(\'' + key + '\', ...) - keyOf returned the duplicate key ' +
                        JSON.stringify(itemKey) + ' at index ' + i + '.\n' +
                        'Two items with the same key are one control path: they would share focus, caret and\n' +
                        'drag state.');
                }

                seen.add(itemKey);

                const node = trie.pushItem(key, itemKey);
                W.beginScope(OPS.LIST_ITEM, node, () => {
                    W.i32(node.rid);
                    W.i32(node.seg);
                    W.i32(node.seg2);
                });
                try {
                    render(array[i], i);
                } finally {
                    W.endScope();
                }
            }
        },

        /// A control. Every interactive control contributes an authored segment (section 3.2a), so `key` is
        /// required and doubles as the label unless `opts.label` overrides it - which is why label is an option
        /// rather than a positional argument (section 3.7 item 1).
        ///
        /// Section 1.1 R5: boolean-returning controls are EVENTS, read with `if`. True on exactly the frame the
        /// click is delivered, once - the read consumes the latch (section 6.3), so `if (ui.button('Add'))`
        /// twice in one frame cannot fire twice, and a button that stopped being drawn cannot fire at all.
        button(key, opts) {
            const node = trie.control(key);
            const label = opts && opts.label !== undefined ? String(opts.label) : String(key);

            W.op(OPS.BUTTON);
            W.i32(node.rid);
            W.i32(node.seg);
            W.i32(W.str(label));

            return R.event(node.rid, F_CLICKED);
        },

        /// A VALUE control - section 1.1 R3: values go in and come back out, no refs and no read-backs.
        ///
        ///     state.name = ui.textField('name', state.name, { placeholder: 'Full name' });
        ///
        /// The two lines that matter are the order of the next two statements. `R.value` resolves the value
        /// FIRST - section 6.4 - and the resolved value is what is emitted AND what is returned. Emitting the
        /// caller's value and then consulting the result is the version that reverts a keystroke, because the
        /// caller's string is what NowTextField clamps its edit state to.
        textField(key, value, opts) {
            const node = trie.control(key);
            const placeholder = opts && opts.placeholder !== undefined ? String(opts.placeholder) : '';
            const incoming = value === undefined || value === null ? '' : String(value);

            const resolved = R.value(node.rid, incoming);

            W.op(OPS.TEXT_FIELD);
            W.i32(node.rid);
            W.i32(node.seg);

            // The VALUE is volatile by declaration, not by heuristic (section 5.4). A text field's contents are
            // per-frame data and interning them permanently is what that section's volatile path exists to
            // prevent; see Recorder.volatileStr for what the heuristic does to it if left to guess.
            W.i32(W.volatileStr(resolved));

            // The PLACEHOLDER is a constant. It interns on the second frame and costs one slot thereafter.
            W.i32(W.str(placeholder));

            // Section 2.7: a handler runs synchronously at the point of the call, so a state change it makes is
            // visible to every control drawn after it - the same ordering `if (ui.button(...))` already has.
            if (opts && typeof opts.onSubmit === 'function' && R.event(node.rid, F_SUBMITTED))
                opts.onSubmit(resolved);

            return resolved;
        },
    };

    return ui;
}

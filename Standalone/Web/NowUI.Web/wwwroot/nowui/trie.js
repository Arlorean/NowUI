// W3 - identity. Docs/Standalone/M3-Spec.md sections 3.2 to 3.6.
//
// The path trie: the structure that turns "where am I in the UI" into a dense integer (the rid, section 3.4) and
// into an identity segment the wasm side can hand NowUI (section 3.3). It also carries the three checks of
// section 3.6, all of them on in every build, because NowUI's own duplicate-id guard is compiled out of a Release
// wasm build (NowControls.cs:606-608) and cannot be the thing that catches this.
//
// Nothing in this file touches the DOM, the wasm runtime, or a typed array that crosses the boundary. It is pure
// data structure, which is why Standalone/NowUI.Bridge.Tests/js runs it under node with no browser.
//
// The shape of a node, and why each field is there:
//
//   rid            dense session-local integer, the result table's index (section 3.4). Recycled through a free
//                  list when a node is evicted.
//   seg            what the wasm side is handed: an intern handle (>= 0) for a keyed scope or a control, or
//                  -(ordinal + 1) for an anonymous scope. The two namespaces cannot collide (section 3.3 point 4).
//   label          the human-readable segment used in every diagnostic: 'roster', '#0', 'team[grace]'.
//   scopes/anons/items/controls
//                  the four lazily created child maps. Separate maps because NowUI derives a control keyed 'x' and
//                  a scope keyed 'x' in different domains (section 3.3 point 5), so they are different nodes and
//                  must not collide here either.
//   children       this frame's child label vector; prevChildren is last frame's. Check 2 compares them.
//   frame          the last frame this node was touched, for the 600-frame eviction that matches
//                  NowControlState.EVICT_AFTER_SECONDS.

import { DEFAULT_MAX_STRINGS } from './abi.js';

/// Kinds of trie node, which decide how a segment is rendered and how it reaches NowUI.
export const NODE_ROOT = 0;
export const NODE_SCOPE = 1;     // keyed:   seg = intern handle
export const NODE_ANON = 2;      // unkeyed: seg = -(ordinal + 1)
export const NODE_ITEM = 3;      // one item of a ui.list: seg pair (listHandle, itemHandle)
export const NODE_CONTROL = 4;   // a control: seg = intern handle

/// Section 3.4: "Trie nodes untouched for 600 frames (~10 s at 60 Hz) are dropped", matching
/// NowControlState.EVICT_AFTER_SECONDS = 10f (Controls/NowControlState.cs:35).
const EVICT_AFTER_FRAMES = 600;

/// Sweeping every node every frame would be the one O(nodes) cost in an otherwise O(ops) recorder. Once every this
/// many frames is often enough for a ten-second eviction window.
const SWEEP_EVERY_FRAMES = 60;

/// Section 3.6 check 2: "capped at 64 distinct reports per session".
const MAX_REORDER_REPORTS = 64;

/// An error the bridge raises against the author's own code, as opposed to a protocol or internal fault. It is
/// thrown at record time, from inside the author's draw function, so the stack trace points at their line.
export class NowUIAuthorError extends Error {
    constructor(message) {
        super(message);
        this.name = 'NowUIAuthorError';
        this.nowui = true;
    }
}

class TrieNode {
    constructor(rid, parent, kind, seg, seg2, label) {
        this.rid = rid;
        this.parent = parent;
        this.kind = kind;
        this.seg = seg;
        this.seg2 = seg2;
        this.label = label;

        this.scopes = null;
        this.anons = null;
        this.items = null;
        this.controls = null;

        this.anonNext = 0;
        this.children = [];
        this.prevChildren = null;
        this.reported = false;
        this.frame = -1;
        this.live = true;
    }
}

export class PathTrie {
    /// `intern` is the recorder's string interner: a function from string to a stable non-negative handle. The trie
    /// never invents handles of its own - the segment it stores is the very integer the op stream carries, so a
    /// diagnostic printed here and an identity resolved in wasm cannot describe different things.
    constructor(intern, options = {}) {
        this.intern = intern;
        this.maxStrings = options.maxStrings || DEFAULT_MAX_STRINGS;

        this.nodes = [];        // rid -> node (or null for a recycled slot)
        this.free = [];         // recycled rids
        this.stampFrame = new Int32Array(64).fill(-1);
        this.stampCall = new Int32Array(64);

        this.root = this._alloc(null, NODE_ROOT, 0, 0, '');
        this.frame = -1;
        this.callIndex = 0;
        this.stack = [this.root];

        this.reports = [];              // check 2, capped
        this.reportCount = 0;
        this.onReport = options.onReport || defaultReport;
        this._sweepAt = SWEEP_EVERY_FRAMES;
    }

    // ------------------------------------------------------------------------------------------ frame bracket

    beginFrame(frame) {
        this.frame = frame;
        this.callIndex = 0;
        this.stack.length = 1;
        this.stack[0] = this.root;
        this._enter(this.root);
    }

    /// Closes the root's own child vector and runs the eviction sweep. Called after the author's draw function has
    /// returned (or thrown - the recorder calls it on every path, so a faulted frame still leaves the trie
    /// consistent).
    endFrame() {
        this._leave(this.root);

        if (--this._sweepAt <= 0) {
            this._sweepAt = SWEEP_EVERY_FRAMES;
            this._sweep();
        }
    }

    get depth() {
        return this.stack.length - 1;
    }

    get current() {
        return this.stack[this.stack.length - 1];
    }

    // ------------------------------------------------------------------------------------------ pushing

    /// A keyed scope: ui.scroll('roster'), ui.column({ key: 'main' }). Immune to ordinal shifts (section 3.5).
    pushScope(key) {
        const parent = this.current;
        const handle = this._internKey(key, 'scope key');

        if (parent.scopes === null) parent.scopes = new Map();
        let node = parent.scopes.get(handle);
        if (node === undefined) {
            node = this._alloc(parent, NODE_SCOPE, handle, 0, String(key));
            parent.scopes.set(handle, node);
        }

        this._touch(parent, node);
        this.stack.push(node);
        this._enter(node);
        return node;
    }

    /// An anonymous scope: ui.column({}, body), ui.row, ui.when. The one positional hazard of section 3.5, and the
    /// only thing check 2 exists to report.
    pushAnon() {
        const parent = this.current;
        const ordinal = parent.anonNext++;

        if (parent.anons === null) parent.anons = new Map();
        let node = parent.anons.get(ordinal);
        if (node === undefined) {
            // Section 3.3: -(ordinal + 1), so the anonymous namespace is <= -1 and the intern namespace is >= 0.
            node = this._alloc(parent, NODE_ANON, -(ordinal + 1), 0, '#' + ordinal);
            parent.anons.set(ordinal, node);
        }

        this._touch(parent, node);
        this.stack.push(node);
        this._enter(node);
        return node;
    }

    /// One item of ui.list(listKey, items, keyOf, render). Both halves intern, because both become NowId segments
    /// on the wasm side (NowControls.KeyedItemIn, NowControls.cs:260).
    pushItem(listKey, itemKey) {
        const parent = this.current;
        const listHandle = this._internKey(listKey, 'list key');
        const itemHandle = this._internKey(itemKey, 'list item key');
        const composite = listHandle * 4294967296 + itemHandle;   // exact: both halves are well under 2^31

        if (parent.items === null) parent.items = new Map();
        let node = parent.items.get(composite);
        if (node === undefined) {
            node = this._alloc(parent, NODE_ITEM, listHandle, itemHandle, String(listKey) + '[' + itemKey + ']');
            parent.items.set(composite, node);
        }

        this._touch(parent, node);
        this.stack.push(node);
        this._enter(node);
        return node;
    }

    /// A control. Every interactive control contributes an authored segment (section 3.2a), so a control is never
    /// positional and never pushes an ordinal. It is a leaf: no scope is opened, nothing has to be popped.
    control(key) {
        const parent = this.current;
        const handle = this._internKey(key, 'control key');

        if (parent.controls === null) parent.controls = new Map();
        let node = parent.controls.get(handle);
        if (node === undefined) {
            node = this._alloc(parent, NODE_CONTROL, handle, 0, String(key));
            parent.controls.set(handle, node);
        }

        this._touch(parent, node);
        return node;
    }

    pop() {
        if (this.stack.length <= 1)
            throw new Error('NowUI internal: the identity stack underflowed. The recorder closes every scope in a ' +
                'finally block, so reaching this means a scope was closed twice.');

        this._leave(this.stack.pop());
    }

    /// The canonical rendering of section 3.2, used verbatim in every diagnostic. The root contributes nothing.
    path(node) {
        const parts = [];
        for (let n = node; n && n.parent; n = n.parent) parts.push(n.label);
        parts.reverse();
        return '/' + parts.join('/');
    }

    // ------------------------------------------------------------------------------------------ check 1

    /// Section 3.6 check 1 - duplicate path. Throws on the frame it happens, in every build.
    ///
    /// The trie makes this exact rather than probabilistic: a second sighting of a rid within one frame IS two
    /// things at the same path, with no hash and no chance of a false positive. It is stamped for scopes as well
    /// as for controls, which is what catches section 3.7 item 5 (two ui.list calls with the same key under the
    /// same parent) - on the item scope rather than on the list, as that section says.
    _touch(parent, node) {
        const call = this.callIndex++;
        const rid = node.rid;

        if (this.stampFrame[rid] === this.frame) {
            const what = node.kind === NODE_CONTROL ? 'control' : 'scope';
            throw new NowUIAuthorError(
                'NowUI: duplicate ' + what + ' key.\n' +
                '  path:  ' + this.path(node) + '\n' +
                '  first: draw call #' + this.stampCall[rid] + '\n' +
                '  again: draw call #' + call + '\n' +
                'Two ' + what + 's with the same path share focus, caret and drag state. Use\n' +
                'ui.list(key, items, keyOf, render) for repeated data, or give each one a distinct key.');
        }

        this.stampFrame[rid] = this.frame;
        this.stampCall[rid] = call;
        node.frame = this.frame;
        parent.children.push(node.label);
    }

    // ------------------------------------------------------------------------------------------ check 2

    _enter(node) {
        node.children = [];
        node.anonNext = 0;
    }

    /// Section 3.6 check 2 - key-vector reorder. Reports once per path, on change, in every build.
    ///
    /// Section 3.6 states the classification in three cases: an unchanged vector is silent, a pure append or
    /// truncate at the end is silent, a change in which every altered slot carries an explicit key is silent, and
    /// a change in which any altered slot is an anonymous ordinal is reported. Implementing that literally against
    /// the label vector produces a FALSE POSITIVE that matters, so the rule is stated more precisely here, and the
    /// reason is worth writing down because it is the whole content of the check:
    ///
    ///   An anonymous ordinal counts ONLY anonymous scopes within the same parent (section 3.5, last line). So
    ///   inserting, removing or moving a KEYED sibling cannot renumber a single anonymous one - and yet it does
    ///   change the label vector, at a slot whose old value was an anonymous ordinal, which the literal reading
    ///   reports. It should not: nothing moved. (Verified, not reasoned: the node's rid is unchanged across such
    ///   an insertion, and there is a check for it in Standalone/NowUI.Bridge.Tests/js/run.mjs.)
    ///
    ///   What an anonymous child's ordinal actually depends on is the NUMBER OF ANONYMOUS SIBLINGS, and nothing
    ///   else. So the condition is exactly: report when the count of anonymous children changed.
    ///
    /// Every case section 3.6 names comes out right under that rule, including its own worked example:
    ///
    ///   a conditional anonymous sibling appears or vanishes   2 -> 1 anonymous   REPORT   (the hazard)
    ///   the same conditional wrapped in ui.when               2 -> 2             silent   (it holds its slot)
    ///   a keyed ui.list gains or loses an item                0 -> 0             silent   (author is in control)
    ///   a keyed sibling inserted among anonymous ones         2 -> 2             silent   (nothing renumbered)
    ///   section 3.6's [#0,#1,#2,#3] -> [#0,#1,#2]             4 -> 3             REPORT
    ///
    /// One cosmetic divergence from section 3.6's illustration, stated rather than hidden. For that last example
    /// the spec prints "slot 2 ("#2")"; slot 2 is identical in both vectors and the first slot at which they
    /// disagree is 3. The spec is naming the slot whose CONTENT moved, and which slot that is cannot be recovered
    /// from the vectors - anonymous ordinals renumber, so removing the child at 2 and removing the child at 3
    /// produce byte-identical vectors. This names the first slot at which the vectors disagree, which is data it
    /// has, and says plainly that every anonymous sibling from there on is suspect.
    _leave(node) {
        const cur = node.children;
        const prev = node.prevChildren;
        node.prevChildren = cur;

        if (prev === null) return;                       // first frame for this node: nothing to compare
        if (node.reported) return;                       // once per path
        if (equalVectors(prev, cur)) return;

        if (countAnon(prev) === countAnon(cur)) return;  // no anonymous ordinal moved: silent

        const common = Math.min(prev.length, cur.length);
        let anonSlot = common;
        for (let i = 0; i < common; i++) {
            if (prev[i] !== cur[i]) { anonSlot = i; break; }
        }

        if (this.reportCount >= MAX_REORDER_REPORTS) return;
        this.reportCount++;
        node.reported = true;

        const label = anonSlot < cur.length ? cur[anonSlot] : prev[anonSlot];
        const message =
            'NowUI: the children of "' + this.path(node) + '" changed shape and slot ' + anonSlot +
            ' ("' + label + '") has an automatic key.\n' +
            '  It is now drawing different content than last frame, and it kept the previous scope\'s\n' +
            '  state (scroll position, foldout state, focus, hover animation). Every anonymous sibling\n' +
            '  from slot ' + anonSlot + ' onwards is affected.\n' +
            '  Give it a key - ui.column({ key: \'name\' }, ...) - or wrap the conditional sibling in\n' +
            '  ui.when(cond, body).\n' +
            '  Previous: [' + prev.join(', ') + ']\n' +
            '  Now:      [' + cur.join(', ') + ']';

        const report = { path: this.path(node), slot: anonSlot, label, previous: prev.slice(), now: cur.slice(), message };
        this.reports.push(report);
        this.onReport(report);
    }

    // ------------------------------------------------------------------------------------------ rids

    _alloc(parent, kind, seg, seg2, label) {
        let rid;
        if (this.free.length > 0) {
            rid = this.free.pop();
        } else {
            rid = this.nodes.length;
            if (rid >= this.stampFrame.length) this._growStamps(rid + 1);
        }

        const node = new TrieNode(rid, parent, kind, seg, seg2, label);
        this.nodes[rid] = node;
        this.stampFrame[rid] = -1;
        return node;
    }

    _growStamps(need) {
        let size = this.stampFrame.length;
        while (size < need) size *= 2;
        const frames = new Int32Array(size).fill(-1);
        frames.set(this.stampFrame);
        const calls = new Int32Array(size);
        calls.set(this.stampCall);
        this.stampFrame = frames;
        this.stampCall = calls;
    }

    /// Drops nodes untouched for EVICT_AFTER_FRAMES and recycles their rids (section 3.4). A node is only dropped
    /// with its whole subtree, so a recycled rid can never be reachable from a live parent.
    _sweep() {
        const cutoff = this.frame - EVICT_AFTER_FRAMES;
        if (cutoff < 0) return;
        this._sweepNode(this.root, cutoff);
    }

    _sweepNode(node, cutoff) {
        for (const map of [node.scopes, node.anons, node.items, node.controls]) {
            if (map === null) continue;
            for (const [key, child] of map) {
                if (child.frame <= cutoff) {
                    this._drop(child);
                    map.delete(key);
                } else {
                    this._sweepNode(child, cutoff);
                }
            }
        }
    }

    _drop(node) {
        for (const map of [node.scopes, node.anons, node.items, node.controls]) {
            if (map === null) continue;
            for (const child of map.values()) this._drop(child);
            map.clear();
        }

        node.live = false;
        this.nodes[node.rid] = null;
        this.stampFrame[node.rid] = -1;
        this.free.push(node.rid);
    }

    // ------------------------------------------------------------------------------------------ keys

    /// Section 3.6 check 3, and section 5.4's ceiling. A key is always interned, never volatile, because a key is
    /// by definition repeated across frames and its handle IS the identity segment.
    _internKey(key, what) {
        if (typeof key !== 'string') {
            throw new NowUIAuthorError(
                'NowUI: a ' + what + ' must be a string, and this one is ' +
                (key === undefined ? 'undefined' : key === null ? 'null' : typeof key + ' (' + String(key) + ')') +
                '.\n  at: ' + this.path(this.current) + '\n' +
                'Keys are identity. A number that happens to be an index makes state follow position; see\n' +
                'ui.list(key, items, keyOf, render), whose keyOf must return a stable string.');
        }

        if (key.length === 0) {
            throw new NowUIAuthorError(
                'NowUI: a ' + what + ' cannot be the empty string.\n  at: ' + this.path(this.current));
        }

        return this.intern(key);
    }
}

function equalVectors(a, b) {
    if (a.length !== b.length) return false;
    for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
    return true;
}

function isAnonLabel(label) {
    return typeof label === 'string' && label.charCodeAt(0) === 35 /* # */;
}

function countAnon(vector) {
    let n = 0;
    for (let i = 0; i < vector.length; i++) if (isAnonLabel(vector[i])) n++;
    return n;
}

function defaultReport(report) {
    // console.warn rather than a throw: an ordinal shift is a correctness smell, not a corrupt frame, and stopping
    // the application over it would be worse than the state loss it is reporting.
    if (typeof console !== 'undefined' && console.warn) console.warn(report.message);
}

// W2-W5 - the draw functions the browser acceptance runs. Docs/Standalone/M3-Spec.md section 9.
//
// W6 replaces this with wwwroot/app.js, the application of section 1. Until then these are the smallest draw
// functions that make each unit's acceptance a photograph rather than an assertion:
//
//   hello        W2: eight header slots and one string cross the boundary and the word "hello" appears.
//   w3           W3: nested anonymous and keyed scopes, a keyed list, and controls sharing a key across two
//                different columns - the case that must COEXIST rather than collide.
//   w5           W4/W5: a text field whose value round-trips through a plain JavaScript object, and two buttons
//                whose clicks are read with `if`. The first interactive JavaScript-authored UI in this repository.
//   w5-onepass   the same, with exactLayout off - W4's "both branches draw the same thing", photographed.
//   throw        W4: a JavaScript throw mid-draw, which must leave a balanced short buffer and an untouched NowUI.
//
// The corruption modes drive W2's other half: a deliberately malformed buffer refused by the validator with a slot
// offset, with NowUI never touched. They record a correct frame first and damage exactly one property of it, so
// what is being tested is the validator rather than a hand-written byte array that may not resemble a real frame.

import { start, corrupt } from './bridge.js';

/// W2's acceptance. One op, one string.
function drawHello(ui) {
    ui.text('hello');
}

/// W3's acceptance, drawn.
///
///   - "shared" is the same control key under two different anonymous columns. Two buttons with the same key in
///     different columns must COEXIST: their paths are /#0/#0/shared and /#0/#1/shared, which are different
///     identities, so they are different controls.
///   - the roster list is keyed, so adding or removing a member shifts nothing.
///   - ui.when holds its ordinal whether or not it draws, so toggling it cannot renumber the scroll below it.
let tick = 0;

function drawW3(ui) {
    tick++;

    ui.text('W3 - identity');

    ui.column({}, () => {
        ui.text('column #0');
        ui.button('shared', { label: 'Shared key, column #0' });
    });

    ui.column({}, () => {
        ui.text('column #1');
        ui.button('shared', { label: 'Shared key, column #1' });
    });

    // Keyed, so it is immune to the ordinal shift the two anonymous columns above it would otherwise cause.
    ui.column({ key: 'roster' }, () => {
        ui.text('roster');
        ui.list('team', ['ada', 'grace', 'edsger'], name => name, name => {
            ui.row({}, () => {
                ui.text(name);
                ui.button('remove', { label: 'Remove ' + name });
            });
        });
    });

    // Consumes its ordinal on every frame and draws on half of them. Without ui.when this would shift every
    // anonymous sibling after it, twice a second, and check 2 would say so.
    ui.when((tick % 120) < 60, () => {
        ui.text('conditional row (ui.when holds its slot)');
    });

    ui.text('frame ' + tick);
}

/// W4 and W5's acceptance, drawn. The first application in this repository whose state round-trips through
/// JavaScript: type into the field and the characters stay, click Clear and it empties, click Add and the count
/// goes up - all of it from a plain JavaScript object, with no ref, no read-back and no id.
///
///   - `state.name = ui.textField(...)` is section 1.1 R3 and section 6.4. The assignment is the whole API.
///   - `if (ui.button('add'))` is R5 and section 6.3: true on exactly the frame the click is delivered, once.
///   - the count beside the field is section 6.2(a), visible on purpose: it is one frame behind the field, which
///     at 60 Hz is 16 ms and is the one thing about this design a careful author eventually notices.
const state = { name: '', added: [] };

function drawW5(ui) {
    ui.text('W5 - the result table');

    ui.row({ key: 'form' }, () => {
        state.name = ui.textField('name', state.name, { placeholder: 'Full name' });
    });

    ui.row({ key: 'actions' }, () => {
        if (ui.button('add', { label: 'Add' }) && state.name.trim() !== '') {
            state.added.push(state.name.trim());
            state.name = '';
        }

        if (ui.button('clear', { label: 'Clear' })) {
            // The programmatic write of section 6.4. It must land even though the field's own result from last
            // frame says something else - that is the half of the rule the `lastSent` comparison exists for.
            state.name = '';
        }
    });

    ui.text('typed: "' + state.name + '"');
    ui.text('added: ' + (state.added.length === 0 ? '(nobody yet)' : state.added.join(', ')));
}

/// W4's fourth acceptance: a JavaScript throw mid-draw. Everything emitted before the throw is kept, every scope
/// the draw function left open is closed by the recorder's finally, and NowUI is not touched by the throw at all -
/// it has not been entered yet when it happens.
function drawThrow(ui) {
    ui.text('before the throw');
    ui.column({ key: 'open' }, () => {
        ui.text('inside a scope that is never closed by the author');
        throw new Error('a deliberate author error, thrown mid-draw');
    });
}

/// Called by the managed host once the runtime is up. `mode` is the value of ?bridge=.
export function install(mode) {
    const draws = { w3: drawW3, w5: drawW5, throw: drawThrow, hello: drawHello };
    const draw = draws[mode] || drawHello;

    // `?bridge=w5-onepass` is the same application with exactLayout off - W4's acceptance that the two branches
    // draw the same thing, as a page rather than as an assertion.
    const onePass = mode === 'w5-onepass';
    start(onePass ? drawW5 : draw, { exactLayout: !onePass });

    const corruptions = ['truncate', 'magic', 'surface', 'bounds', 'intern', 'unbalanced'];
    if (corruptions.indexOf(mode) >= 0) corrupt(mode);

    if (onePass) return 'w5-onepass';
    if (corruptions.indexOf(mode) >= 0) return mode;
    return draws[mode] ? mode : 'hello';
}

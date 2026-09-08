// popups.js - the regression fixture for "a popup's choice reaches JavaScript".
//
// Reached as ?app=popups. It exists because of one defect and is kept so that defect cannot come back silently.
//
// THE DEFECT, now FIXED in the package (2026-09-08). Several NowUI controls hand a popup's choice to the NEXT
// frame through a one-shot slot in NowControlState, and used to drain that slot at the top of Draw with no
// passive/measure guard. Under `exactLayout` the bridge runs the whole UI twice per frame, and the MEASURE pass -
// the one whose results are thrown away - drained it. The live pass then re-seeded from the value JavaScript
// sent, found the slot already empty, and published the OLD value. On screen that looked exactly like clicking
// outside the popup: it opens, it highlights, it closes, and nothing changes.
//
// The fix guards the commit itself rather than the clear, so a passive pass observes without mutating - the same
// rule NowControlState.AdvanceTransition and RepeatByStateKey already followed. See NowDropdown.Draw. A host-side
// carry in Standalone/NowUI.Bridge/Replay.Controls.cs stood in until the package fix landed and is now gone. This
// page is kept because it is the end-to-end guard: NowPopupUXTests covers the package invariant in Unity, and
// this covers the JavaScript path that motivated finding it.
//
// Every control here commits through a deferred overlay, which is what makes them the affected family.
//
// ui.colorField is MISSING FROM THIS PAGE AND SHOULD BE ADDED. The reason recorded here previously - that the
// WebGL2 backend has no 'NowUI/Color Picker' material - is false, and was false when it was written:
// WebGL2Backend.cs:110-122 resolves that shader and wwwroot/shaders/nowui-colorpicker.{vert,frag} are ported.
// The same stale premise is why ?area=fields still leaves ColorPicker out. GradientField and CurveField are in
// the same position. All three took the same package fix as the four below, so what is missing is the coverage.
import { start, ui } from './nowui/nowui.js';

const ROLES  = ['Engineering', 'Compilers', 'Research', 'Design'];
const CITIES = ['Amsterdam', 'Berlin', 'Copenhagen', 'Dublin'];

const DAY = 86400000;

const state = {
  role:  'Engineering',
  city:  'Amsterdam',
  // Milliseconds since the epoch, and seconds since midnight: section 2.5's wire shapes for these two.
  date:  Date.UTC(2026, 8, 21),
  time:  7 * 3600 + 30 * 60,
};

start(() => {
  ui.column({ padding: 24, gap: 16, grow: 1 }, () => {
    ui.heading('Popup commits');

    ui.card({ padding: 16, gap: 12 }, () => {
      ui.text('dropdown');
      state.role = ui.dropdown('role', state.role, ROLES, { width: 220 });

      ui.text('combo');
      state.city = ui.combo('city', state.city, CITIES, { width: 220 });

      ui.text('datePicker');
      state.date = ui.datePicker('date', state.date, { width: 220 });

      ui.text('timePicker');
      state.time = ui.timePicker('time', state.time, { width: 220 });
    });
  });
});

// The values, for a driver, behind ?debug=1 - the convention main.js sets for window.nowui. A test that drives a
// popup can then read which control committed rather than infer it from pixels.
if (new URLSearchParams(location.search).get('debug') === '1') {
  window.nowuiPopups = {
    values: () =>
      'role=' + state.role +
      ';city=' + state.city +
      ';date=' + new Date(state.date).toISOString().slice(0, 10) +
      ';time=' + state.time,
  };
}

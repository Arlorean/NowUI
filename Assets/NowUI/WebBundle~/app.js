// app.js
import { start, ui } from './nowui/nowui.js';

const ROLES = ['Engineering', 'Compilers', 'Research', 'Design'];

const state = {
  name:     '',
  email:    '',
  priority: 3,
  team: [
    { id: 'ada',   name: 'Ada Lovelace', role: 'Engineering', active: true  },
    { id: 'grace', name: 'Grace Hopper', role: 'Compilers',   active: true  },
    { id: 'karen', name: 'Karen Jones',  role: 'Research',    active: false },
  ],
  selected: 'ada',
  status:   null,
};

function addPerson() {
  const name = state.name.trim();
  if (!name) { state.status = 'A name is required.'; return; }
  const id = name.toLowerCase().replace(/[^a-z0-9]+/g, '-');
  state.team.push({ id, name, role: ROLES[0], active: true });
  state.selected = id;
  state.name = state.email = '';
  state.status = `Added ${name}.`;
}

start(() => {
  ui.column({ padding: 24, gap: 16, grow: 1 }, () => {

    ui.heading('Team roster');

    // ---- the form -------------------------------------------------------
    ui.card({ padding: 16, gap: 12 }, () => {

      ui.row({ gap: 12 }, () => {
        state.name  = ui.textField('name',  state.name,  { placeholder: 'Full name',     grow: 1,
                                                           onSubmit: addPerson });
        state.email = ui.textField('email', state.email, { placeholder: 'name@team.dev', grow: 1 });
      });

      ui.row({ gap: 12, align: 'center' }, () => {
        ui.text('Priority', { width: 64 });
        state.priority = ui.slider('priority', state.priority, 1, 5, { step: 1, grow: 1 });
        ui.text(String(state.priority), { width: 24 });
      });

      ui.row({ gap: 8, justify: 'end' }, () => {
        if (ui.button('Clear', { style: 'ghost' })) {
          state.name = state.email = '';
          state.status = null;
        }
        if (ui.button('Add', { style: 'accent', disabled: state.name.trim() === '' })) {
          addPerson();
        }
      });
    });

    // ---- the list -------------------------------------------------------
    ui.scroll('roster', { grow: 1, gap: 2 }, () => {

      ui.list('team', state.team, p => p.id, (p) => {
        ui.row({ gap: 8, align: 'center' }, () => {
          if (ui.selectable('row', p.id === state.selected, { label: p.name, grow: 1 }))
            state.selected = p.id;

          p.role   = ui.dropdown('role', p.role, ROLES, { width: 150 });
          p.active = ui.switch('active', p.active);

          if (ui.button('Remove', { style: 'danger' })) {
            state.team = state.team.filter(x => x.id !== p.id);
            state.status = `Removed ${p.name}.`;
          }
        });
      });

      ui.when(state.team.length === 0, () => {
        ui.text('Nobody on the team yet.', { style: 'muted' });
      });
    });

    ui.when(state.status !== null, () => ui.caption(state.status));
  });
});

// The app's state, for a driver, behind ?debug=1 for the same reason main.js gates window.nowui: a page that
// always publishes its internals to the global scope has decided its internals are an API. With the flag on, a
// test that drives a role dropdown can read which ROW changed rather than infer it from pixels - which is the
// difference between "the popup committed to this row" and "some pixels in the roster changed".
if (new URLSearchParams(location.search).get('debug') === '1') {
  window.nowuiApp = {
    state: () => JSON.parse(JSON.stringify(state)),
    roles: () => state.team.map(p => p.id + '=' + p.role).join(';'),
  };
}
